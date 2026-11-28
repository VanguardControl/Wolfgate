using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.Underground;
using Content.Shared.Buckle.Components;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Store;
using Content.Shared.Turrets;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Underground;

/// <summary>
/// Looks after the Underground's station: arms its turrets on the Godfather's word outside a raid, locks its vend in a
/// lockdown, parks a safehouse ship away from it as a second place to wake up, and adds Fugitive slots as the round fills.
/// </summary>
public sealed partial class WGUndergroundBaseSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DockingSystem _dock = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedDeployableTurretSystem _turrets = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private StationJobsSystem _jobs = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private WGUndergroundHeatSystem _heat = default!;

    private const string ScaledJob = "Fugitive";
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(10);

    /// <summary>Seconds a jump spends off the map.</summary>
    private const float HopTravelSeconds = 30f;

    /// <summary>How far a jump's arrival point keeps from every other grid, in metres.</summary>
    private const float HopClearance = 2000f;

    private const int HopTries = 60;

    /// <summary>Whether the Godfather has armed the turrets outside a raid.</summary>
    public bool Armed { get; private set; }

    /// <summary>Whether the turrets should be up: armed by the Godfather, and no raid window open. A raid is fought without them.</summary>
    public bool WantTurrets => Armed && _heat.RaidEnds == null;

    /// <summary>The UndergroundVend, which a lockdown keeps from selling.</summary>
    private const string Vend = "VendingMachineSyndieContraband";

    /// <summary>When turrets that have been ordered up deploy, once the warning to everyone aboard has run.</summary>
    private TimeSpan? _deployAt;

    /// <summary>Ships told the station is closed to guests since the turrets were ordered up, and when.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _closedTold = new();

    private static readonly TimeSpan ClosedRepeat = TimeSpan.FromSeconds(30);

    private TimeSpan _nextStep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGUndergroundHeatChangedEvent>(OnHeatChanged);
        SubscribeLocalEvent<DockEvent>(OnDock);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<BoundUserInterfaceMessageAttempt>(OnUiMessageAttempt);
    }

    /// <summary>During a raid window and the lockdown after it, the UndergroundVend sells nothing.</summary>
    private void OnUiMessageAttempt(BoundUserInterfaceMessageAttempt args)
    {
        if (args.Cancelled
            || args.Message is not StoreBuyListingMessage
            || !_heat.Lockdown
            || MetaData(args.Target).EntityPrototype?.ID != Vend)
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("wg-underground-vend-locked"), args.Target, args.Actor);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (_deployAt is { } deploy && now >= deploy)
        {
            _deployAt = null;
            if (WantTurrets)
                SetTurrets(true);
        }

        if (now < _nextStep)
            return;

        _nextStep = now + Step;

        // A turret with no power or shots refuses to deploy, so keep asking while they should be up.
        if (WantTurrets && _deployAt == null)
            SetTurrets(true);

        var bases = EntityQueryEnumerator<WGUndergroundBaseComponent>();
        while (bases.MoveNext(out var uid, out var home))
        {
            if (_station.GetOwningStation(uid) is not { } station)
                continue;

            if (!home.SafehouseTried)
            {
                home.SafehouseTried = true;
                ParkSafehouse(uid, station, home);
            }

            ScaleSlots(station, home);
        }
    }

    /// <summary>Arms the station's turrets or stands them down. False when arming during a raid window, which keeps them down.</summary>
    public bool SetArmed(bool armed)
    {
        if (armed && _heat.RaidEnds != null)
            return false;

        var were = WantTurrets;
        Armed = armed;
        OrderTurrets(were);
        return true;
    }

    /// <summary>
    /// Stands the turrets down at once, or brings them up after a warning to everyone aboard. The turrets can't tell
    /// a guest from a raider, so the warning is what gives the market truce's guests time to leave.
    /// </summary>
    private void OrderTurrets(bool were)
    {
        var want = WantTurrets;
        if (want == were)
        {
            if (_deployAt == null)
                SetTurrets(want);
            return;
        }

        if (!want)
        {
            _deployAt = null;
            _closedTold.Clear();
            SetTurrets(false);
            return;
        }

        var warning = Math.Max(0f, _cfg.GetCVar(WGUndergroundCVars.TurretWarning));
        if (warning <= 0)
        {
            SetTurrets(true);
            _heat.TellFaction(Loc.GetString("wg-underground-turrets-armed"), Aboard());
            return;
        }

        _deployAt = _timing.CurTime + TimeSpan.FromSeconds(warning);
        _heat.TellFaction(Loc.GetString("wg-underground-turrets-warning", ("seconds", (int) warning)), Aboard());
    }

    private void OnDock(DockEvent args)
    {
        Docked(args.GridAUid, args.GridBUid);
    }

    /// <summary>
    /// Tells a ship that has docked to the station while the turrets are up, or coming up, that it is closed to
    /// guests. Those already docked when the order went out had the warning; a late arrival would get none.
    /// </summary>
    public void Docked(EntityUid gridA, EntityUid gridB)
    {
        if (!WantTurrets)
            return;

        EntityUid ship;
        if (HasComp<WGUndergroundBaseComponent>(gridA))
            ship = gridB;
        else if (HasComp<WGUndergroundBaseComponent>(gridB))
            ship = gridA;
        else
            return;

        if (CompOrNull<CompanyComponent>(ship)?.CompanyName.Id == WGUndergroundHeatSystem.Company)
            return;

        // A ship with several airlocks docks once for each.
        var now = _timing.CurTime;
        if (_closedTold.TryGetValue(ship, out var told) && now < told + ClosedRepeat)
            return;

        _closedTold[ship] = now;
        _heat.TellAboard(Loc.GetString("wg-underground-turrets-closed"), new HashSet<EntityUid> { ship });
    }

    /// <summary>Whether a ship has been told, since the turrets were last ordered up, that the station is closed to guests.</summary>
    public bool ToldClosed(EntityUid ship)
    {
        return _closedTold.ContainsKey(ship);
    }

    /// <summary>The station's grids and every ship docked to them.</summary>
    private HashSet<EntityUid> Aboard()
    {
        var aboard = new HashSet<EntityUid>();
        var bases = EntityQueryEnumerator<WGUndergroundBaseComponent>();
        while (bases.MoveNext(out var uid, out _))
        {
            _shuttle.GetAllDockedShuttlesIgnoringFTLLock(uid, aboard);
        }

        return aboard;
    }

    private void SetTurrets(bool on)
    {
        var turrets = EntityQueryEnumerator<WGUndergroundTurretComponent, DeployableTurretComponent>();
        while (turrets.MoveNext(out var uid, out _, out var deployable))
        {
            if (deployable.Enabled != on)
                _turrets.TrySetState((uid, deployable), on);
        }
    }

    private void OnHeatChanged(WGUndergroundHeatChangedEvent ev)
    {
        // A raid stands the Godfather's turrets down; when it closes they come back up after the usual warning.
        if (ev.Level == WGUndergroundHeatSystem.Raid)
        {
            OrderTurrets(Armed);
            if (Armed)
                _heat.TellFaction(Loc.GetString("wg-underground-turrets-raid"));
        }
        else if (ev.Old == WGUndergroundHeatSystem.Raid)
        {
            OrderTurrets(false);
        }

        if (ev.Level != WGUndergroundHeatSystem.Raid)
            return;

        // A raid that opens while the drive is still spooling holds the station where it is, and gives the hour back.
        var bases = EntityQueryEnumerator<WGUndergroundBaseComponent>();
        while (bases.MoveNext(out var uid, out var home))
        {
            if (!_shuttle.WgAbortFtlSpool(uid))
                continue;

            home.NextHop = _timing.CurTime;
            _heat.TellFaction(Loc.GetString("wg-underground-hop-aborted"), Aboard());
        }
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        Armed = false;
        _deployAt = null;
        _closedTold.Clear();
        _nextStep = TimeSpan.Zero;
    }

    /// <summary>Whether the station can jump now, with the reason for the Godfather when it can't.</summary>
    public bool CanHop(Entity<WGUndergroundBaseComponent> home, [NotNullWhen(false)] out string? refusal)
    {
        refusal = null;
        var now = _timing.CurTime;
        if (_cfg.GetCVar(WGUndergroundCVars.HopMinutes) <= 0 || !HasComp<ShuttleComponent>(home))
            refusal = Loc.GetString("wg-underground-hop-failed");
        else if (HasComp<FTLComponent>(home))
            refusal = Loc.GetString("wg-underground-hop-jumping");
        else if (_heat.RaidEnds != null)
            refusal = Loc.GetString("wg-underground-hop-raid");
        else if (now < home.Comp.NextHop)
            refusal = Loc.GetString("wg-underground-hop-wait", ("minutes", (int) Math.Ceiling((home.Comp.NextHop - now).TotalMinutes)));

        return refusal == null;
    }

    /// <summary>
    /// Jumps the station to a new place in its band of the sector, after a warning to everyone aboard. Ships docked
    /// with their FTL lock on come along; the safehouse stays where it is.
    /// </summary>
    public bool TryHop(Entity<WGUndergroundBaseComponent> home, [NotNullWhen(false)] out string? refusal)
    {
        if (!CanHop(home, out refusal))
            return false;

        var xform = Transform(home);
        if (!TryComp<ShuttleComponent>(home, out var shuttle) || xform.MapUid is not { } map)
        {
            refusal = Loc.GetString("wg-underground-hop-failed");
            return false;
        }

        // Nothing is cast off or charged for a jump that has nowhere to go.
        if (!TryHopDestination(home, xform, out var spot))
        {
            refusal = Loc.GetString("wg-underground-hop-nowhere");
            return false;
        }

        // Everyone docked is warned, whether or not their ship comes along.
        var aboard = new HashSet<EntityUid>();
        _shuttle.GetAllDockedShuttlesIgnoringFTLLock(home, aboard);

        // One docked ship that can't jump would hold the whole station back, so it is cast off.
        var docked = new HashSet<EntityUid>();
        _shuttle.GetAllDockedShuttles(home, docked);
        foreach (var ship in docked)
        {
            if (ship != home.Owner && !_shuttle.CanFTL(ship, out _))
                _dock.UndockDocks(ship);
        }

        var spool = Math.Max(0f, _cfg.GetCVar(WGUndergroundCVars.HopSpool));
        var target = new EntityCoordinates(map, spot);
        _shuttle.FTLToCoordinates(home, shuttle, target, _transform.GetWorldRotation(xform), spool, HopTravelSeconds);
        if (!HasComp<FTLComponent>(home))
        {
            refusal = Loc.GetString("wg-underground-hop-failed");
            return false;
        }

        home.Comp.NextHop = _timing.CurTime + TimeSpan.FromMinutes(_cfg.GetCVar(WGUndergroundCVars.HopMinutes));
        _heat.TellFaction(Loc.GetString("wg-underground-hop-told", ("seconds", (int) spool)), aboard);
        return true;
    }

    /// <summary>
    /// Finds a place at least a jump's length away, in the station's band of the sector and clear of other grids.
    /// False when the settings leave no such place, or none turned up in the tries allowed.
    /// </summary>
    private bool TryHopDestination(EntityUid home, TransformComponent xform, out Vector2 spot)
    {
        var here = _transform.GetWorldPosition(xform);
        var min = Math.Max(0f, _cfg.GetCVar(WGUndergroundCVars.HopMinRange));
        var max = Math.Max(min, _cfg.GetCVar(WGUndergroundCVars.HopMaxRange));
        var move = _cfg.GetCVar(WGUndergroundCVars.HopMove);

        var others = new List<Vector2>();
        var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var uid, out _, out var gridXform))
        {
            if (uid != home && gridXform.MapUid == xform.MapUid)
                others.Add(_transform.GetWorldPosition(gridXform));
        }

        for (var i = 0; i < HopTries; i++)
        {
            spot = _random.NextAngle().ToVec() * _random.NextFloat(min, max);
            if ((spot - here).Length() < move)
                continue;

            var clear = true;
            foreach (var other in others)
            {
                if ((other - spot).Length() >= HopClearance)
                    continue;

                clear = false;
                break;
            }

            if (clear)
                return true;
        }

        spot = default;
        return false;
    }

    /// <summary>Adds a Fugitive slot for every so many players in the round, and never takes one back.</summary>
    private void ScaleSlots(EntityUid station, WGUndergroundBaseComponent home)
    {
        var per = _cfg.GetCVar(WGUndergroundCVars.SlotPlayers);
        if (per <= 0)
            return;

        var wanted = Math.Min(_cfg.GetCVar(WGUndergroundCVars.SlotBonusMax), _players.PlayerCount / per);
        if (wanted <= home.BonusSlots)
            return;

        if (_jobs.TryAdjustJobSlot(station, ScaledJob, wanted - home.BonusSlots, clamp: true))
            home.BonusSlots = wanted;
    }

    /// <summary>
    /// Parks one of the Underground's ships well away from the station, as part of the station, with spawn markers on
    /// its seats. A raid on the station then can't hold everyone who joins.
    /// </summary>
    private void ParkSafehouse(EntityUid home, EntityUid station, WGUndergroundBaseComponent component)
    {
        var min = _cfg.GetCVar(WGUndergroundCVars.SafehouseMin);
        var max = Math.Max(min, _cfg.GetCVar(WGUndergroundCVars.SafehouseMax));
        if (min <= 0 || !_proto.TryIndex(component.SafehouseVessel, out var vessel))
            return;

        var xform = Transform(home);
        var offset = _transform.GetWorldPosition(xform) + _random.NextAngle().ToVec() * _random.NextFloat(min, max);
        if (!_mapLoader.TryLoadGrid(xform.MapID, vessel.ShuttlePath, out var grid, offset: offset))
        {
            Log.Error($"Failed to load the Underground safehouse {vessel.ShuttlePath}");
            return;
        }

        var ship = grid.Value.Owner;
        component.Safehouse = ship;
        _station.AddGridToStation(station, ship, name: Loc.GetString("wg-underground-safehouse-name"));
        var company = EnsureComp<CompanyComponent>(ship);
        company.CompanyName = WGUndergroundHeatSystem.Company;
        Dirty(ship, company);
        _shuttle.AddIFFFlag(ship, IFFFlags.HideLabel);

        var seated = 0;
        var seats = EntityQueryEnumerator<StrapComponent, TransformComponent>();
        while (seated < component.SafehouseSeats && seats.MoveNext(out _, out _, out var seat))
        {
            if (seat.GridUid != ship)
                continue;

            seated++;
            foreach (var marker in component.SafehouseSpawns)
            {
                Spawn(marker, seat.Coordinates);
            }
        }

        if (seated == 0)
            Log.Warning($"The Underground safehouse {vessel.ShuttlePath} has no seats to wake up on");
    }
}
