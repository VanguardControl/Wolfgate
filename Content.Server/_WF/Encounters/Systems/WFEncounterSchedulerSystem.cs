using System.Numerics;
using Content.Server.GameTicking;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared.Mobs.Systems;
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
/// Paces encounters: at a random interval, while fewer than the cap are running, picks a weighted prototype that
/// fits the player count and its cooldown and places it in clear space away from a player ship.
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

    /// <summary>Space that has to be free of grids around the origin.</summary>
    private const float Clearance = 600f;
    private const int PlacementTries = 8;

    private bool _enabled;
    private float _intervalMin;
    private float _intervalMax;
    private int _maxActive;
    private TimeSpan? _next;
    private readonly Dictionary<string, TimeSpan> _lastStarted = new();
    private List<Entity<MapGridComponent>> _nearby = new();

    /// <summary>Stops the scheduler until resumed, whatever the cvar says.</summary>
    public bool Paused;

    /// <summary>When the scheduler next tries, while it is running.</summary>
    public TimeSpan? Next => _next;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, EncountersCVars.Enabled, value => _enabled = value, true);
        Subs.CVar(_config, EncountersCVars.IntervalMin, value => _intervalMin = value, true);
        Subs.CVar(_config, EncountersCVars.IntervalMax, value => _intervalMax = value, true);
        Subs.CVar(_config, EncountersCVars.MaxActive, value => _maxActive = value, true);
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
        return TimeSpan.FromSeconds(_random.NextFloat(_intervalMin, MathF.Max(_intervalMin, _intervalMax)));
    }

    /// <summary>Picks and starts one encounter now, if the cap, a candidate and a clear spot allow it.</summary>
    public bool TrySchedule(out EntityUid encounter)
    {
        encounter = default;
        if (_encounters.ActiveCount() >= _maxActive)
        {
            Log.Info("No encounter scheduled: the cap is reached.");
            return false;
        }

        if (Pick() is not { } prototype)
        {
            Log.Info("No encounter scheduled: no prototype fits the player count and cooldowns.");
            return false;
        }

        if (!TryPlace(prototype, out var origin))
        {
            Log.Info($"No encounter scheduled: no clear space for {prototype.ID}.");
            return false;
        }

        if (!_encounters.TrySpawn(prototype, origin, out encounter))
            return false;

        _lastStarted[prototype.ID] = _timing.CurTime;
        return true;
    }

    private WFEncounterPrototype? Pick()
    {
        var players = _playerManager.PlayerCount;
        var candidates = new List<WFEncounterPrototype>();
        var total = 0f;
        foreach (var prototype in _prototypes.EnumeratePrototypes<WFEncounterPrototype>())
        {
            // One of a kind at a time: an encounter that is still running is not picked again.
            if (!prototype.Scheduled || prototype.Weight <= 0f || players < prototype.MinPlayers
                || _encounters.IsRunning(prototype.ID)
                || _lastStarted.TryGetValue(prototype.ID, out var last)
                    && _timing.CurTime - last < TimeSpan.FromSeconds(prototype.Cooldown))
                continue;

            candidates.Add(prototype);
            total += prototype.Weight;
        }

        if (candidates.Count == 0)
            return null;

        var roll = _random.NextFloat(total);
        foreach (var prototype in candidates)
        {
            roll -= prototype.Weight;
            if (roll <= 0f)
                return prototype;
        }

        return candidates[^1];
    }

    /// <summary>A point in clear space at the prototype's distance from a random ship with a living player aboard.</summary>
    private bool TryPlace(WFEncounterPrototype prototype, out MapCoordinates origin)
    {
        origin = MapCoordinates.Nullspace;
        var anchors = new List<MapCoordinates>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != null && _mobs.IsAlive(uid))
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
