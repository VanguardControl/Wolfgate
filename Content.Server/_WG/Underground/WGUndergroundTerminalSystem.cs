using Content.Server._NF.Bank;
using Content.Server.Administration.Logs;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Underground;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Verbs;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Underground;

/// <summary>
/// The Underground's faction business, run from its cargo console's verbs: market passes, bail, payroll, closing the
/// market, arming the turrets and declaring a vendetta.
/// </summary>
public sealed partial class WGUndergroundTerminalSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGUndergroundBaseSystem _base = default!;
    [Dependency] private WGUndergroundHeatSystem _heat = default!;
    [Dependency] private WGUndergroundWarrantSystem _warrants = default!;

    private static readonly ProtoId<AccessLevelPrototype> MemberAccess = "Underground";
    private static readonly ProtoId<AccessLevelPrototype> BossAccess = "Godfather";
    private static readonly ProtoId<AccessLevelPrototype> OfficerAccess = "WGUndergroundFixer";
    private static readonly ProtoId<RadioChannelPrototype> OpenChannel = "Common";

    /// <summary>How near a ship has to be to the terminal for a vendetta to be declared on it, in metres.</summary>
    private const float VendettaRange = 1500f;

    /// <summary>Ships under a vendetta, and when it ends.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _vendettas = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGUndergroundTerminalComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    /// <summary>Whether the Underground has a vendetta running against a ship.</summary>
    public bool UnderVendetta(EntityUid ship)
    {
        return _vendettas.TryGetValue(ship, out var until) && until > _timing.CurTime;
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _vendettas.Clear();
    }

    private void OnVerbs(Entity<WGUndergroundTerminalComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        if (!Authorized(ent, user, out var boss, out var officer))
            return;

        // A verb can be picked after the menu was built, so each one checks again before it acts.
        bool Member() => Authorized(ent, user, out _, out _);
        bool Officer() => Authorized(ent, user, out _, out var still) && still;
        bool Boss() => Authorized(ent, user, out var still, out _) && still;
        var priority = 20;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wg-underground-verb-status"),
            Priority = priority--,
            Act = () =>
            {
                if (Member())
                    _popup.PopupEntity(Status(), ent, user, PopupType.Medium);
            },
        });

        if (!officer)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wg-underground-verb-pass"),
            Priority = priority--,
            Act = () =>
            {
                if (Officer())
                    IssuePass(ent, user);
            },
        });

        foreach (var held in _warrants.Held())
        {
            var prisoner = held.Owner;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wg-underground-verb-bail", ("name", Name(prisoner)), ("amount", held.Comp.Bail)),
                Priority = priority--,
                Act = () =>
                {
                    if (Officer())
                        PayBail(ent, user, prisoner);
                },
            });
        }

        if (!boss)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(_heat.Closed ? "wg-underground-verb-open" : "wg-underground-verb-close"),
            Priority = priority--,
            Act = () =>
            {
                if (Boss())
                    _heat.SetClosed(!_heat.Closed);
            },
        });

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(_base.Armed ? "wg-underground-verb-disarm" : "wg-underground-verb-arm"),
            Priority = priority--,
            Act = () =>
            {
                if (!Boss())
                    return;

                _base.SetArmed(!_base.Armed);
                _heat.TellFaction(Loc.GetString(_base.Armed ? "wg-underground-turrets-armed" : "wg-underground-turrets-down"));
            },
        });

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wg-underground-verb-payroll", ("amount", _cfg.GetCVar(WGUndergroundCVars.PayrollEach))),
            Priority = priority--,
            Act = () =>
            {
                if (Boss())
                    PayCrew(ent, user);
            },
        });

        foreach (var ship in NearbyShips(ent))
        {
            if (UnderVendetta(ship))
                continue;

            var target = ship;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wg-underground-verb-vendetta", ("ship", Name(target))),
                Priority = priority--,
                Act = () =>
                {
                    if (Boss())
                        DeclareVendetta(user, target);
                },
            });
        }
    }

    /// <summary>
    /// Whether someone may use the terminal at all, and as what. The console's company lock applies to its verbs as
    /// it does to its window, so a stolen card is not enough for the TSF or PDV.
    /// </summary>
    private bool Authorized(EntityUid terminal, EntityUid user, out bool boss, out bool officer)
    {
        boss = false;
        officer = false;
        if (TryComp<CompanyAccessReaderComponent>(terminal, out var reader))
        {
            if (!TryComp<CompanyComponent>(user, out var company))
            {
                if (!reader.Inverted)
                    return false;
            }
            else if (reader.RequiredCompanies.Contains(company.CompanyName) == reader.Inverted)
            {
                return false;
            }
        }

        var tags = _access.FindAccessTags(user);
        if (!tags.Contains(MemberAccess))
            return false;

        boss = tags.Contains(BossAccess);
        officer = boss || tags.Contains(OfficerAccess);
        return true;
    }

    private string Status()
    {
        return Loc.GetString("wg-underground-status",
            ("level", Loc.GetString(WGUndergroundHeatSystem.LevelName(_heat.Level))),
            ("treasury", _heat.Treasury),
            ("market", Loc.GetString(_heat.Closed ? "wg-underground-status-closed" : "wg-underground-status-open")));
    }

    private void IssuePass(Entity<WGUndergroundTerminalComponent> ent, EntityUid user)
    {
        var now = _timing.CurTime;
        if (_heat.Closed)
        {
            _popup.PopupEntity(Loc.GetString("wg-underground-pass-closed"), ent, user);
            return;
        }

        if (now < ent.Comp.NextPass)
        {
            _popup.PopupEntity(Loc.GetString("wg-underground-pass-wait", ("seconds", (int) (ent.Comp.NextPass - now).TotalSeconds + 1)), ent, user);
            return;
        }

        ent.Comp.NextPass = now + TimeSpan.FromSeconds(_cfg.GetCVar(WGUndergroundCVars.PassCooldown));
        var pass = Spawn(ent.Comp.Pass, Transform(ent).Coordinates);
        _hands.PickupOrDrop(user, pass);
        _popup.PopupEntity(Loc.GetString("wg-underground-pass-issued"), ent, user);
    }

    private void PayBail(EntityUid terminal, EntityUid user, EntityUid prisoner)
    {
        if (_warrants.PayBail(prisoner))
        {
            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):player} paid the Underground's bail for {ToPrettyString(prisoner):target}");
            return;
        }

        _popup.PopupEntity(Loc.GetString("wg-underground-bail-failed"), terminal, user);
    }

    private void PayCrew(EntityUid terminal, EntityUid user)
    {
        var each = _cfg.GetCVar(WGUndergroundCVars.PayrollEach);
        var crew = new List<EntityUid>();
        var members = EntityQueryEnumerator<WGUndergroundMemberComponent, ActorComponent>();
        while (members.MoveNext(out var uid, out _, out _))
        {
            if (!_mobState.IsDead(uid))
                crew.Add(uid);
        }

        if (each <= 0 || crew.Count == 0 || !_heat.Spend(each * crew.Count))
        {
            _popup.PopupEntity(Loc.GetString("wg-underground-payroll-failed"), terminal, user);
            return;
        }

        var unpaid = 0;
        foreach (var member in crew)
        {
            if (!_bank.TryBankDeposit(member, each, tax: false))
                unpaid++;
        }

        // Pay that couldn't land, such as a locked account, goes back to the treasury.
        _heat.Earn(unpaid * each);
        _heat.TellFaction(Loc.GetString("wg-underground-payroll-paid", ("amount", each), ("count", crew.Count - unpaid)));
        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):player} paid {crew.Count - unpaid} Underground members {each} each from the treasury");
    }

    private void DeclareVendetta(EntityUid user, EntityUid ship)
    {
        if (TerminatingOrDeleted(ship) || UnderVendetta(ship))
            return;

        var minutes = _cfg.GetCVar(WGUndergroundCVars.VendettaMinutes);
        _vendettas[ship] = _timing.CurTime + TimeSpan.FromMinutes(minutes);
        var line = Loc.GetString("wg-underground-vendetta-declared", ("ship", Name(ship)), ("minutes", (int) minutes));
        _heat.Radio(line, OpenChannel, Loc.GetString("wg-underground-sender"));
        _heat.TellFaction(line);
        if (!_base.Armed)
            _base.SetArmed(true);
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(user):player} declared an Underground vendetta on {ToPrettyString(ship):target}");
    }

    /// <summary>Player ships of other companies near the terminal.</summary>
    private List<EntityUid> NearbyShips(EntityUid terminal)
    {
        var ships = new List<EntityUid>();
        var here = Transform(terminal);
        var origin = _transform.GetWorldPosition(here);
        var grids = EntityQueryEnumerator<ShuttleDeedComponent, MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.MapID != here.MapID || uid == here.GridUid)
                continue;

            if (CompOrNull<CompanyComponent>(uid)?.CompanyName.Id == WGUndergroundHeatSystem.Company)
                continue;

            if ((_transform.GetWorldPosition(xform) - origin).Length() <= VendettaRange)
                ships.Add(uid);
        }

        return ships;
    }
}
