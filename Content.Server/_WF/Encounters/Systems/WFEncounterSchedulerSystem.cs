using System.Numerics;
using Content.Server._NF.GameTicking.Events;
using Content.Server.GameTicking;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station.Components;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// The storyteller. Works to the round's preset: places round-start encounters once the stations exist, then at a
/// random interval starts a weighted encounter that fits the budget, the player count and its cooldown, where its
/// placement says. It may bring back a replaceable round-start encounter that is gone.
/// </summary>
public sealed partial class WFEncounterSchedulerSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IMapManager _maps = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Space that has to be free of other grids around an open-space origin.</summary>
    private const float Clearance = 600f;
    private const int PlacementTries = 8;

    private bool _enabled;
    private float _intervalMin;
    private float _intervalMax;
    private int _maxActive;
    private string _presetId = string.Empty;
    private TimeSpan? _next;
    private readonly Dictionary<string, TimeSpan> _lastStarted = new();
    private readonly HashSet<string> _startedAtRoundStart = new();
    private List<Entity<MapGridComponent>> _nearby = new();

    /// <summary>Stops the scheduler until resumed, whatever the cvar says.</summary>
    public bool Paused;

    /// <summary>When the scheduler next tries, while it is running.</summary>
    public TimeSpan? Next => _next;

    /// <summary>The preset the storyteller works to, or null if the configured one does not exist.</summary>
    public WFEncounterPresetPrototype? Preset => _prototypes.TryIndex<WFEncounterPresetPrototype>(_presetId, out var preset) ? preset : null;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, EncountersCVars.Enabled, value => _enabled = value, true);
        Subs.CVar(_config, EncountersCVars.IntervalMin, value => _intervalMin = value, true);
        Subs.CVar(_config, EncountersCVars.IntervalMax, value => _intervalMax = value, true);
        Subs.CVar(_config, EncountersCVars.MaxActive, value => _maxActive = value, true);
        Subs.CVar(_config, EncountersCVars.Preset, value => _presetId = value, true);
        SubscribeLocalEvent<StationsGeneratedEvent>(OnStationsGenerated);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _lastStarted.Clear();
        _startedAtRoundStart.Clear();
        _next = null;
    }

    /// <summary>Round-start encounters go in once the sector's stations exist.</summary>
    private void OnStationsGenerated(StationsGeneratedEvent args)
    {
        if (_enabled && !Paused)
            StartRound();
    }

    /// <summary>Places the preset's share of round-start encounters. Returns how many started.</summary>
    public int StartRound()
    {
        var started = 0;
        var limit = Preset?.RoundStart ?? 1;
        var unplaceable = new HashSet<string>();
        while (started < limit && Pick(WFEncounterStart.RoundStart, unplaceable) is { } prototype)
        {
            if (!TryStart(prototype, out _))
            {
                unplaceable.Add(prototype.ID);
                continue;
            }

            _lastStarted[prototype.ID] = _timing.CurTime;
            _startedAtRoundStart.Add(prototype.ID);
            started++;
        }

        return started;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_enabled || Paused || _ticker.RunLevel != GameRunLevel.InRound)
        {
            _next = null;
            return;
        }

        _next ??= _timing.CurTime + Interval();
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval();
        TrySchedule(out _);
    }

    private TimeSpan Interval()
    {
        var scale = MathF.Max(0.1f, Preset?.IntervalScale ?? 1f);
        return TimeSpan.FromSeconds(_random.NextFloat(_intervalMin, MathF.Max(_intervalMin, _intervalMax)) * scale);
    }

    /// <summary>The budget for running encounters: the preset's, grown by the player count.</summary>
    public int Budget()
    {
        return Preset is { } preset
            ? preset.Budget + (int) (preset.BudgetPerPlayer * _playerManager.PlayerCount)
            : int.MaxValue;
    }

    /// <summary>Picks and starts one mid-round encounter now, if the caps, a candidate and a place allow it.</summary>
    public bool TrySchedule(out EntityUid encounter)
    {
        encounter = default;
        if (_encounters.ActiveCount() >= _maxActive)
        {
            Log.Info("No encounter scheduled: the cap is reached.");
            return false;
        }

        // A pick that has no place right now is passed over without using up its cooldown.
        var unplaceable = new HashSet<string>();
        while (Pick(WFEncounterStart.Scheduled, unplaceable) is { } prototype)
        {
            if (!TryStart(prototype, out encounter))
            {
                unplaceable.Add(prototype.ID);
                continue;
            }

            _lastStarted[prototype.ID] = _timing.CurTime;
            return true;
        }

        Log.Info("No encounter scheduled: nothing fits the budget, the player count, the cooldowns and the space.");
        return false;
    }

    private bool TryStart(WFEncounterPrototype prototype, out EntityUid encounter)
    {
        encounter = default;
        if (!TryPlace(prototype, out var origin, out var from, out var to))
        {
            Log.Info($"Encounter {prototype.ID} not started: no place for it.");
            return false;
        }

        return _encounters.TrySpawn(prototype, origin, out encounter, null, from, to);
    }

    /// <summary>A weighted pick among the prototypes of a start kind that fit right now.</summary>
    private WFEncounterPrototype? Pick(WFEncounterStart start, HashSet<string> skip)
    {
        var players = _playerManager.PlayerCount;
        var preset = Preset;
        var room = Budget() - _encounters.ActiveCost();
        var candidates = new List<(WFEncounterPrototype Prototype, float Weight)>();
        var total = 0f;
        foreach (var prototype in _prototypes.EnumeratePrototypes<WFEncounterPrototype>())
        {
            // Mid-round the storyteller may also bring back a replaceable round-start encounter that is gone.
            var fits = prototype.Start == start
                || start == WFEncounterStart.Scheduled && prototype.Start == WFEncounterStart.RoundStart
                    && prototype.Replaceable && _startedAtRoundStart.Contains(prototype.ID);
            var weight = prototype.Weight * (preset != null && preset.Weights.TryGetValue(prototype.Category, out var scale) ? scale : 1f);
            // One of a kind at a time: an encounter that is still running is not picked again.
            if (!fits || weight <= 0f || players < prototype.MinPlayers || prototype.Cost > room || skip.Contains(prototype.ID)
                || _encounters.IsRunning(prototype.ID)
                || _lastStarted.TryGetValue(prototype.ID, out var last)
                    && _timing.CurTime - last < TimeSpan.FromSeconds(prototype.Cooldown))
                continue;

            candidates.Add((prototype, weight));
            total += weight;
        }

        if (candidates.Count == 0)
            return null;

        var roll = _random.NextFloat(total);
        foreach (var (prototype, weight) in candidates)
        {
            roll -= weight;
            if (roll <= 0f)
                return prototype;
        }

        return candidates[^1].Prototype;
    }

    private bool TryPlace(WFEncounterPrototype prototype, out MapCoordinates origin, out EntityUid? from, out EntityUid? to)
    {
        from = null;
        to = null;
        if (prototype.Placement == WFEncounterPlacement.OpenSpace)
            return TryPlaceInOpenSpace(prototype, out origin);

        origin = MapCoordinates.Nullspace;
        var stations = Stations();
        if (stations.Count == 0 || prototype.Placement == WFEncounterPlacement.Route && stations.Count < 2)
            return false;

        var station = _random.PickAndTake(stations);
        from = station;
        if (prototype.Placement == WFEncounterPlacement.Route)
            to = _random.Pick(stations).Owner;

        var centre = _transform.GetMapCoordinates(station);
        var reach = station.Comp.LocalAABB.Size.Length() / 2f + prototype.Standoff;
        origin = new MapCoordinates(centre.Position + _random.NextAngle().ToVec() * reach, centre.MapId);
        return true;
    }

    /// <summary>The sector's stations and outposts: station grids on the round's map that nobody holds a deed to.</summary>
    private List<Entity<MapGridComponent>> Stations()
    {
        var stations = new List<Entity<MapGridComponent>>();
        var query = EntityQueryEnumerator<StationMemberComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var grid, out var xform))
        {
            if (xform.MapID == _ticker.DefaultMap && !HasComp<ShuttleDeedComponent>(uid))
                stations.Add((uid, grid));
        }

        return stations;
    }

    /// <summary>A point in clear space at the prototype's distance from a random living player.</summary>
    private bool TryPlaceInOpenSpace(WFEncounterPrototype prototype, out MapCoordinates origin)
    {
        origin = MapCoordinates.Nullspace;
        var anchors = new List<MapCoordinates>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            // A body without a mob state still counts; ghosts and the dead don't.
            if (xform.MapID != MapId.Nullspace && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid) && !_mobs.IsCritical(uid))
                anchors.Add(_transform.GetMapCoordinates(uid, xform));
        }

        if (anchors.Count == 0)
            return false;

        for (var i = 0; i < PlacementTries; i++)
        {
            var anchor = _random.Pick(anchors);
            var distance = _random.NextFloat(prototype.MinDistance, MathF.Max(prototype.MinDistance, prototype.MaxDistance));
            var point = anchor.Position + _random.NextAngle().ToVec() * distance;
            _nearby.Clear();
            _maps.FindGridsIntersecting(anchor.MapId, Box2.CenteredAround(point, new Vector2(Clearance * 2f)), ref _nearby, approx: true, includeMap: false);
            if (_nearby.Count > 0)
                continue;

            origin = new MapCoordinates(point, anchor.MapId);
            return true;
        }

        return false;
    }
}
