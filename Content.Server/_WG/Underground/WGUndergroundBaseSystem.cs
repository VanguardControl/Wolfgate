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
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private SharedDeployableTurretSystem _turrets = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private StationJobsSystem _jobs = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private WGUndergroundHeatSystem _heat = default!;

    private const string ScaledJob = "Fugitive";
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(10);

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
