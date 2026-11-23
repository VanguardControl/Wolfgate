using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.Underground;
using Content.Shared.Buckle.Components;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
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
/// Looks after the Underground's station: arms its turrets in a raid or on the Godfather's word, parks a safehouse
/// ship away from it as a second place to wake up, and adds Fugitive slots as the round fills.
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

    private const int HopTries = 30;

    /// <summary>Whether the Godfather has armed the turrets outside a raid.</summary>
    public bool Armed { get; private set; }

    private TimeSpan _nextStep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGUndergroundHeatChangedEvent>(OnHeatChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextStep)
            return;

        _nextStep = now + Step;

        // A turret with no power or shots refuses to deploy, so keep asking while they should be up.
        if (Armed || _heat.Level == WGUndergroundHeatSystem.Raid)
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

    /// <summary>Arms the station's turrets or stands them down. A raid keeps them armed either way.</summary>
    public void SetArmed(bool armed)
    {
        Armed = armed;
        SetTurrets(armed || _heat.Level == WGUndergroundHeatSystem.Raid);
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
        if (ev.Level == WGUndergroundHeatSystem.Raid || ev.Old == WGUndergroundHeatSystem.Raid)
            SetTurrets(Armed || ev.Level == WGUndergroundHeatSystem.Raid);
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        Armed = false;
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

        // One docked ship that can't jump would hold the whole station back, so it is cast off.
        var docked = new HashSet<EntityUid>();
        _shuttle.GetAllDockedShuttles(home, docked);
        foreach (var ship in docked)
        {
            if (ship != home.Owner && !_shuttle.CanFTL(ship, out _))
                _dock.UndockDocks(ship);
        }

        var spool = Math.Max(0f, _cfg.GetCVar(WGUndergroundCVars.HopSpool));
        var target = new EntityCoordinates(map, HopDestination(home, xform));
        _shuttle.FTLToCoordinates(home, shuttle, target, _transform.GetWorldRotation(xform), spool, HopTravelSeconds);
        if (!HasComp<FTLComponent>(home))
        {
            refusal = Loc.GetString("wg-underground-hop-failed");
            return false;
        }

        home.Comp.NextHop = _timing.CurTime + TimeSpan.FromMinutes(_cfg.GetCVar(WGUndergroundCVars.HopMinutes));
        _heat.TellFaction(Loc.GetString("wg-underground-hop-told", ("seconds", (int) spool)), docked);
        return true;
    }

    /// <summary>A place at least a jump's length away, in the station's band of the sector and clear of other grids.</summary>
    private Vector2 HopDestination(EntityUid home, TransformComponent xform)
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

        Vector2? best = null;
        var bestGap = -1f;
        for (var i = 0; i < HopTries; i++)
        {
            var spot = _random.NextAngle().ToVec() * _random.NextFloat(min, max);
            if ((spot - here).Length() < move)
                continue;

            var gap = float.MaxValue;
            foreach (var other in others)
            {
                gap = Math.Min(gap, (other - spot).Length());
            }

            if (gap >= HopClearance)
                return spot;

            if (gap > bestGap)
            {
                best = spot;
                bestGap = gap;
            }
        }

        // Nowhere in the band is far enough: go a jump's length in any direction.
        return best ?? here + _random.NextAngle().ToVec() * Math.Max(move, HopClearance);
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
