using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ShipShields;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Stack;
using Content.Shared._Mono.Company;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Rewards players who take a side in an encounter. Whose side you are on is decided by who you shoot: ship
/// weapon hits on one side's ships count as help for the others. A ship that has helped a side and not fired on
/// it becomes that side's ally for the encounter. When one side is the only one left, everyone who helped it
/// shares its reward equally, paid into their bank accounts, with faction credits on top for its own company.
/// </summary>
public sealed partial class WFEncounterRewardSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MobStateSystem _mobs = default!;

    /// <summary>Hits on a side's ships before a player ship counts as fighting it.</summary>
    public const int MinimumHits = 5;

    private static readonly ProtoId<RadioChannelPrototype> ThanksChannel = "Common";
    private static readonly TimeSpan CapWindow = TimeSpan.FromHours(1);
    private readonly Dictionary<NetUserId, (TimeSpan Since, int Paid)> _paid = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewHullHitEvent>(OnHullHit);
        SubscribeLocalEvent<WFShipShieldAttackedEvent>(OnShieldHit);
        SubscribeLocalEvent<WFEncounterResolvedEvent>(OnResolved);
    }

    private void OnHullHit(ref WFCrewHullHitEvent args)
    {
        RecordHit(args.Grid, args.AttackerGrid);
    }

    private void OnShieldHit(ref WFShipShieldAttackedEvent args)
    {
        RecordHit(args.Grid, args.AttackerGrid);
    }

    /// <summary>Counts a ship weapon hit by a player-crewed ship on an encounter ship as help for the other sides.</summary>
    public void RecordHit(EntityUid victim, EntityUid attacker)
    {
        if (!TryComp<WFEncounterGridComponent>(victim, out var marker) || HasComp<WFEncounterGridComponent>(attacker)
            || !TryComp<WFEncounterComponent>(marker.Encounter, out var encounter) || encounter.Resolution != null
            || !encounter.Ships.TryGetValue(marker.Key, out var ship))
            return;

        var crew = new List<NetUserId>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.GridUid == attacker && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                crew.Add(actor.PlayerSession.UserId);
        }

        if (crew.Count == 0)
            return;

        foreach (var player in crew)
        {
            if (!encounter.Hits.TryGetValue(player, out var bySide))
                encounter.Hits[player] = bySide = new Dictionary<string, int>();
            bySide[ship.Side] = bySide.GetValueOrDefault(ship.Side) + 1;
        }

        var hits = encounter.ShipHits.GetValueOrDefault((attacker, ship.Side)) + 1;
        encounter.ShipHits[(attacker, ship.Side)] = hits;
        if (hits != MinimumHits)
            return;

        // Having fought this side and no other, the ship is on the others' side: they hold their fire for it.
        foreach (var other in encounter.Ships.Values)
        {
            if (other.Side == ship.Side || TerminatingOrDeleted(other.Grid)
                || encounter.ShipHits.GetValueOrDefault((attacker, other.Side)) > 0)
                continue;

            _escorts.AddAlly(other.Grid, attacker);
            encounter.Allies.Add((other.Grid, attacker));
        }
    }

    private void OnResolved(ref WFEncounterResolvedEvent args)
    {
        if (!TryComp<WFEncounterComponent>(args.Encounter, out var encounter))
            return;

        foreach (var (ship, ally) in encounter.Allies)
        {
            _escorts.RemoveAlly(ship, ally);
        }

        encounter.Allies.Clear();
        if (args.Resolution != WFEncounterResolution.Decided || !_prototypes.TryIndex(encounter.Prototype, out var prototype))
            return;

        // The winner is the one side with a ship still in the fight.
        WFEncounterShipState? winner = null;
        foreach (var ship in encounter.Ships.Values)
        {
            if (!TerminatingOrDeleted(ship.Grid) && !_status.IsDisabled(ship.Grid))
                winner = ship;
        }

        if (winner == null || prototype.Rewards.FirstOrDefault(reward => reward.Side == winner.Side) is not { } reward)
            return;

        var losers = new HashSet<string>();
        foreach (var ship in encounter.Ships.Values)
        {
            if (ship.Side != winner.Side && !TerminatingOrDeleted(ship.Grid)
                && CompOrNull<CompanyComponent>(ship.Grid)?.CompanyName.Id is { Length: > 0 } company)
                losers.Add(company);
        }

        var helpers = new List<(ICommonSession Session, EntityUid Mob)>();
        foreach (var (user, bySide) in encounter.Hits)
        {
            var against = bySide.Where(pair => pair.Key != winner.Side).Sum(pair => pair.Value);
            if (against < MinimumHits || bySide.GetValueOrDefault(winner.Side) > 0
                || !_playerManager.TryGetSessionById(user, out var session) || session.AttachedEntity is not { } mob
                || HasComp<GhostComponent>(mob) || _mobs.IsDead(mob))
                continue;

            // Nobody is paid for helping to destroy a ship of their own company.
            if (CompOrNull<CompanyComponent>(mob)?.CompanyName.Id is { Length: > 0 } own && losers.Contains(own))
                continue;

            helpers.Add((session, mob));
        }

        if (helpers.Count == 0)
            return;

        var share = reward.Spesos / helpers.Count;
        var cap = _config.GetCVar(EncountersCVars.PayoutHourlyCap);
        foreach (var (session, mob) in helpers)
        {
            var paid = Capped(session.UserId, share, cap);
            if (paid > 0)
                _bank.TryBankDeposit(mob, paid, false);

            var credits = 0;
            if (reward.Credits > 0 && reward.CreditEntity is { } creditEntity
                && CompOrNull<CompanyComponent>(mob)?.CompanyName.Id is { } company && reward.CreditCompanies.Contains(company))
            {
                credits = reward.Credits;
                foreach (var stack in _stack.SpawnMultiple(creditEntity, credits, Transform(mob).Coordinates))
                {
                    _hands.PickupOrDrop(mob, stack);
                }
            }

            _chat.DispatchServerMessage(session, Loc.GetString(credits > 0 ? "wf-encounter-reward-paid-credits" : "wf-encounter-reward-paid",
                ("name", encounter.Name), ("amount", paid), ("credits", credits)));
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{session.Name} was paid {paid} spesos and {credits} faction credits for helping side {winner.Side} win encounter {encounter.Prototype} ({ToPrettyString(args.Encounter):entity})");
        }

        if (reward.Thanks is { } thanks)
            _encounters.TrySay(winner, ThanksChannel, Loc.GetString(thanks, ("count", helpers.Count)));
    }

    /// <summary>What of an amount a player may still be paid within the hourly cap, and counts it.</summary>
    private int Capped(NetUserId user, int amount, int cap)
    {
        var now = _timing.CurTime;
        var (since, paid) = _paid.GetValueOrDefault(user);
        if (now - since >= CapWindow)
        {
            since = now;
            paid = 0;
        }

        var allowed = Math.Clamp(cap - paid, 0, amount);
        _paid[user] = (since, paid + allowed);
        return allowed;
    }
}
