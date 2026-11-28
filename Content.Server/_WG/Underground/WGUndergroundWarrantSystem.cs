using Content.Server._Mono.PDV.Components;
using Content.Server.Stack;
using Content.Shared._WG.Newbie;
using Content.Shared._WG.Underground;
using Content.Shared.Contraband;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Store;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Underground;

/// <summary>
/// Personal warrants on Underground members: what the TSF pays for each of them alive, how it grows with their own
/// jobs, the bail the Underground can pay while a pallet holds them, dearer with each capture in the round, and the
/// time they can't be handed in again unless they attack the TSF or PDV.
/// Also decides which prisoners and bodies a contraband pallet takes.
/// </summary>
public sealed partial class WGUndergroundWarrantSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private WGHereticMarkSystem _marks = default!;
    [Dependency] private WGUndergroundHeatSystem _heat = default!;

    /// <summary>The currency the TSF pays warrants in.</summary>
    public static readonly ProtoId<CurrencyPrototype> Credits = "FederationMilitaryCredit";

    /// <summary>The currency the PDV pays for heretics in.</summary>
    public static readonly ProtoId<CurrencyPrototype> Doubloons = "Doubloon";

    private const string Cash = "SpaceCash";
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeldGrace = TimeSpan.FromMinutes(10);

    /// <summary>Players who were handed in or bailed, and until when their next body can't be.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _served = new();

    /// <summary>How many times each player has been handed in or bailed this round.</summary>
    private readonly Dictionary<NetUserId, int> _captures = new();

    private TimeSpan _nextStep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGUndergroundMemberComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ActorComponent, DamageChangedEvent>(OnPlayerDamaged);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawned);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    /// <summary>A member who hurts a TSF or PDV player gives up the time they were serving, as Rule 7 says.</summary>
    private void OnPlayerDamaged(Entity<ActorComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased
            || args.Origin is not { } attacker
            || attacker == ent.Owner
            || !TryComp<WGUndergroundMemberComponent>(attacker, out var member)
            || !_heat.IsHunter(ent))
            return;

        Unserve(attacker, member);
    }

    /// <summary>Ends a member's served time, so pallets take them again.</summary>
    public void Unserve(EntityUid mob, WGUndergroundMemberComponent member)
    {
        if (member.LockedUntil <= _timing.CurTime)
            return;

        member.LockedUntil = TimeSpan.Zero;
        if (PlayerOf(mob) is { } user)
            _served.Remove(user);
        Apply(mob, member);
    }

    /// <summary>How many times a member's player has been handed in or bailed this round.</summary>
    public int CapturesOf(EntityUid mob)
    {
        return PlayerOf(mob) is { } user ? _captures.GetValueOrDefault(user) : 0;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextStep)
            return;

        _nextStep = now + Step;
        var members = EntityQueryEnumerator<WGUndergroundMemberComponent>();
        while (members.MoveNext(out var uid, out var member))
        {
            if (member.LockedUntil > now)
                continue;

            // The lock has run out, or something replaced the body's contraband values and dropped the warrant.
            var expired = member.LockedUntil != TimeSpan.Zero;
            if (!expired && (member.Warrant <= 0 || CompOrNull<ContrabandComponent>(uid)?.TurnInValues.ContainsKey(Credits) == true))
                continue;

            member.LockedUntil = TimeSpan.Zero;
            Apply(uid, member);
        }

        // A prisoner who got away starts a fresh bail clock the next time a pallet sees them.
        var held = EntityQueryEnumerator<WGUndergroundHeldComponent>();
        while (held.MoveNext(out var uid, out var hold))
        {
            if (now > hold.Until + HeldGrace)
                RemCompDeferred<WGUndergroundHeldComponent>(uid);
        }
    }

    /// <summary>Raises a member's warrant for jobs they sold themself.</summary>
    public void Worked(EntityUid seller, int jobs = 1)
    {
        if (!TryComp<WGUndergroundMemberComponent>(seller, out var member))
            return;

        member.Warrant = Math.Min(member.MaxWarrant, member.Warrant + jobs * _cfg.GetCVar(WGUndergroundCVars.WarrantJob));
        Apply(seller, member);
    }

    /// <summary>Whether a player was handed in or bailed recently enough that no pallet takes their next body.</summary>
    public bool Served(NetUserId user)
    {
        return _served.TryGetValue(user, out var until) && until > _timing.CurTime;
    }

    /// <summary>The player a body belongs to, found even after they have ghosted or disconnected.</summary>
    public NetUserId? PlayerOf(EntityUid mob)
    {
        if (TryComp<ActorComponent>(mob, out var actor))
            return actor.PlayerSession.UserId;

        if (TryComp<WGUndergroundMemberComponent>(mob, out var member) && member.User is { } known)
            return known;

        return _mind.TryGetMind(mob, out _, out var mind) ? mind.UserId ?? mind.OriginalOwnerUserId : null;
    }

    /// <summary>Writes a member's warrant back after something replaced their contraband values, such as a heretic mark.</summary>
    public void Refresh(EntityUid uid)
    {
        if (TryComp<WGUndergroundMemberComponent>(uid, out var member))
            Apply(uid, member);
    }

    /// <summary>Whether a pallet paying in this currency takes this mob right now.</summary>
    public bool CanTurnIn(EntityUid mob, string rewardType)
    {
        // Only the Sultanate pays for a body, and only a marked heretic's.
        if (_mobState.IsDead(mob))
            return rewardType == Doubloons.Id && HasComp<HereticComponent>(mob);

        // Selling a tagged new player is round removal.
        if (HasComp<WGNewbieComponent>(mob))
            return false;

        if (rewardType != Credits.Id || !TryComp<WGUndergroundMemberComponent>(mob, out var member))
            return true;

        return BailRun(mob, member);
    }

    /// <summary>What a mob pays at a pallet: a heretic's body is worth half of what they pay alive.</summary>
    public int TurnInValue(EntityUid ent, string rewardType, int value)
    {
        if (rewardType != Doubloons.Id || !HasComp<HereticComponent>(ent) || !HasComp<MobStateComponent>(ent))
            return value;

        return _mobState.IsDead(ent) ? value / 2 : value;
    }

    /// <summary>
    /// Records what a pallet sale took. A heretic keeps their body and loses the mark, so they are taken out of the
    /// set the pallet deletes; a member handed in to the TSF is deleted with the rest and can't be handed in again
    /// for a while.
    /// </summary>
    public void TurnedIn(HashSet<EntityUid> sold, string rewardType)
    {
        // A sold container is deleted with all it holds, so a heretic or member shut inside is put out on the pallet first.
        foreach (var ent in sold)
        {
            if (!HasComp<MobStateComponent>(ent))
                PutOut(ent);
        }

        foreach (var ent in new List<EntityUid>(sold))
        {
            if (!HasComp<MobStateComponent>(ent))
                continue;

            if (rewardType == Doubloons.Id && HasComp<HereticComponent>(ent))
            {
                _marks.Resolve(ent);
                RemComp<HereticComponent>(ent);
                if (TryComp<ContrabandComponent>(ent, out var contraband) && contraband.TurnInValues.Remove(Doubloons))
                    Dirty(ent, contraband);
                sold.Remove(ent);
                continue;
            }

            if (rewardType != Credits.Id || !TryComp<WGUndergroundMemberComponent>(ent, out var member))
                continue;

            Serve(ent, member);
            _heat.Captures++;
            _heat.Add(-_cfg.GetCVar(WGUndergroundCVars.HeatCapture));
            _heat.TellFaction(Loc.GetString("wg-underground-captured", ("name", Name(ent))));
        }
    }

    /// <summary>Takes marked heretics and Underground members out of a container, however deep, and leaves them where it stands.</summary>
    private void PutOut(EntityUid holder)
    {
        var inside = new List<EntityUid>();
        var children = Transform(holder).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            inside.Add(child);
        }

        foreach (var child in inside)
        {
            if (!HasComp<MobStateComponent>(child))
            {
                PutOut(child);
                continue;
            }

            if (!HasComp<HereticComponent>(child) && !HasComp<WGUndergroundMemberComponent>(child))
                continue;

            // Straight to the grid: taking them out of one container would only drop them into the one around it.
            _transform.AttachToGridOrMap(child);
        }
    }

    /// <summary>The members pallets are holding for bail.</summary>
    public List<Entity<WGUndergroundHeldComponent>> Held()
    {
        var list = new List<Entity<WGUndergroundHeldComponent>>();
        var held = EntityQueryEnumerator<WGUndergroundHeldComponent>();
        while (held.MoveNext(out var uid, out var hold))
        {
            list.Add((uid, hold));
        }

        return list;
    }

    /// <summary>Pays a held member's bail from the treasury. The cash lands beside them, and no pallet takes them for a while.</summary>
    public bool PayBail(EntityUid prisoner)
    {
        if (!TryComp<WGUndergroundHeldComponent>(prisoner, out var held) || !TryComp<WGUndergroundMemberComponent>(prisoner, out var member))
            return false;

        if (!_heat.Spend(held.Bail))
            return false;

        if (held.Bail > 0)
            _stack.SpawnMultiple(Cash, held.Bail, Transform(prisoner).Coordinates);
        _heat.BailPaid += held.Bail;
        _popup.PopupEntity(Loc.GetString("wg-underground-bail-paid-popup", ("amount", held.Bail)), prisoner, PopupType.Large);
        _heat.TellFaction(Loc.GetString("wg-underground-bail-paid", ("name", Name(prisoner)), ("amount", held.Bail)));
        RemComp<WGUndergroundHeldComponent>(prisoner);
        Serve(prisoner, member);
        member.Warrant = member.BaseWarrant;
        Apply(prisoner, member);
        return true;
    }

    /// <summary>Starts the bail clock the first time a pallet sees a member, and holds them until it runs out.</summary>
    private bool BailRun(EntityUid mob, WGUndergroundMemberComponent member)
    {
        var now = _timing.CurTime;
        if (member.LockedUntil > now || member.Warrant <= 0)
            return false;

        if (TryComp<WGUndergroundHeldComponent>(mob, out var held))
            return now >= held.Until;

        // Each earlier capture this round makes the hold longer and the bail dearer.
        var repeats = CapturesOf(mob);
        var minutes = _cfg.GetCVar(WGUndergroundCVars.BailMinutes);
        if (minutes <= 0)
            return true;

        minutes += repeats * Math.Max(0f, _cfg.GetCVar(WGUndergroundCVars.BailRepeatMinutes));
        held = AddComp<WGUndergroundHeldComponent>(mob);
        held.Until = now + TimeSpan.FromMinutes(minutes);
        var rate = 1f + repeats * Math.Max(0f, _cfg.GetCVar(WGUndergroundCVars.BailRepeat));
        held.Bail = (int) MathF.Round(member.Warrant * _cfg.GetCVar(WGUndergroundCVars.BailRate) * rate);
        var place = Transform(mob).GridUid is { } grid ? Name(grid) : Loc.GetString("wg-underground-bail-somewhere");
        _heat.TellFaction(Loc.GetString("wg-underground-bail-told",
            ("name", Name(mob)), ("place", place), ("amount", held.Bail), ("minutes", (int) minutes)));
        _popup.PopupEntity(Loc.GetString("wg-underground-bail-popup", ("minutes", (int) minutes)), mob, PopupType.Large);
        return false;
    }

    /// <summary>Locks a member, and the player's next body, out of pallets for the served time, and counts the capture.</summary>
    private void Serve(EntityUid mob, WGUndergroundMemberComponent member)
    {
        var until = _timing.CurTime + TimeSpan.FromMinutes(_cfg.GetCVar(WGUndergroundCVars.ServedMinutes));
        member.LockedUntil = until;
        if (PlayerOf(mob) is not { } user)
            return;

        _served[user] = until;
        _captures[user] = _captures.GetValueOrDefault(user) + 1;
    }

    /// <summary>Writes the warrant onto the member as the military credits a pallet pays for them.</summary>
    private void Apply(EntityUid uid, WGUndergroundMemberComponent member)
    {
        var contraband = EnsureComp<ContrabandComponent>(uid);
        contraband.HideValues = true;
        if (member.LockedUntil > _timing.CurTime || member.Warrant <= 0)
            contraband.TurnInValues.Remove(Credits);
        else
            contraband.TurnInValues[Credits] = member.Warrant;
        Dirty(uid, contraband);
    }

    private void OnStartup(Entity<WGUndergroundMemberComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.Warrant = ent.Comp.BaseWarrant;
        Apply(ent, ent.Comp);
    }

    /// <summary>Ties a member's body to the player who spawned in it, and carries over a lock they are still serving.</summary>
    public void Claim(EntityUid mob, NetUserId user)
    {
        if (!TryComp<WGUndergroundMemberComponent>(mob, out var member))
            return;

        member.User = user;
        if (_served.TryGetValue(user, out var until) && until > _timing.CurTime)
            member.LockedUntil = until;
        Apply(mob, member);
    }

    private void OnSpawned(PlayerSpawnCompleteEvent ev)
    {
        Claim(ev.Mob, ev.Player.UserId);
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _served.Clear();
        _captures.Clear();
    }
}
