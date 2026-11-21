using Content.Server._WG.Dormancy;
using Content.Shared.Mobs.Components;
using Content.Shared.Shuttles.Components;
using Prometheus;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WG.ColdStorage;

/// <summary>
/// Puts grids in space nobody is aboard or near into stasis: everything on them but walls and cables is paused where it
/// stands, keeping all its state at no cost a tick, and wakes in one tick when someone comes. The grid entity itself
/// stays awake, so a hull keeps drifting, colliding and answering to its deed and IFF.
/// </summary>
public sealed partial class WGGridStasisSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WGSiteStashSystem _stash = default!;
    [Dependency] private WGEntityPressureSystem _pressure = default!;

    /// <summary>Entities paused or woken between two looks at the clock.</summary>
    private const int Slice = 32;

    /// <summary>Metres the wake range keeps past PVS, and the stash range past the wake range.</summary>
    public const float Margin = 32f;

    private static readonly TimeSpan EvaluateEvery = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan RegistryEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DocksEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultHold = TimeSpan.FromSeconds(60);

    private static readonly Gauge GridsGauge = Metrics.CreateGauge(
        "wg_stasis_grids",
        "Grids in space by stasis state",
        new GaugeConfiguration { LabelNames = ["state"] });

    private static readonly Gauge EntitiesGauge = Metrics.CreateGauge(
        "wg_stasis_entities",
        "Entities paused by grid stasis");

    private static readonly Gauge WorstTickGauge = Metrics.CreateGauge(
        "wg_stasis_worst_tick_ms",
        "Worst tick grid stasis spent pausing and waking since the last sample");

    private readonly Dictionary<EntityUid, WGStasisGrid> _grids = new();
    private readonly Dictionary<EntityUid, HashSet<string>> _pins = new();
    private readonly Dictionary<EntityUid, TimeSpan> _holds = new();
    private readonly List<WGStasisGrid> _work = new();

    /// <summary>Grids being paused or woken, so a tick with nothing to do walks none.</summary>
    private readonly HashSet<WGStasisGrid> _inFlight = new();

    private readonly List<EntityUid> _tree = new();
    private readonly List<EntityUid> _mobs = new();
    private readonly List<EntityUid> _gone = new();
    private readonly System.Diagnostics.Stopwatch _watch = new();
    private readonly int[] _stateCounts = new int[5];
    private TimeSpan _nextEvaluate;
    private TimeSpan _nextRegistry;
    private TimeSpan _nextDocks;
    private double _sampleWorst;

    private bool _enabled;
    private bool _playerShips;
    private float _idle;
    private float _range;
    private float _wakeRange;
    private float _budget;
    private float _sweep;
    private float _pvsRange;

    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<MetaDataComponent> _metaQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<FTLComponent> _ftlQuery;

    /// <summary>Metres within which a viewer wakes a grid in stasis.</summary>
    public float WakeRange => MathF.Max(_wakeRange, _pvsRange + Margin);

    /// <summary>Metres within which a viewer keeps an awake grid awake.</summary>
    public float StashRange => MathF.Max(_range, WakeRange + Margin);

    /// <summary>Metres within which a grid is woken whole in the tick it is seen, not over several.</summary>
    public float UrgentRange => _pvsRange + Margin;

    /// <summary>Seconds a grid must go unattended before stasis; down to a quarter at full entity pressure.</summary>
    internal float EffectiveIdle => _idle * _pressure.Scale(1f, 0.25f);

    /// <summary>Every grid being watched, for tests and admins.</summary>
    public IReadOnlyDictionary<EntityUid, WGStasisGrid> Grids => _grids;

    /// <summary>The worst time one tick spent pausing and waking, since it was last reset.</summary>
    public double WorstTickMs;

    /// <summary>What the worst tick was doing.</summary>
    public string WorstTickWork = string.Empty;

    private string _tickWork = string.Empty;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();
        _metaQuery = GetEntityQuery<MetaDataComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _ftlQuery = GetEntityQuery<FTLComponent>();

        Subs.CVar(_cfg, WGStasisCVars.Enabled, OnEnabled, true);
        Subs.CVar(_cfg, WGStasisCVars.PlayerShips, value => _playerShips = value, true);
        Subs.CVar(_cfg, WGStasisCVars.Idle, value => _idle = value, true);
        Subs.CVar(_cfg, WGStasisCVars.Range, value => _range = value, true);
        Subs.CVar(_cfg, WGStasisCVars.WakeRange, value => _wakeRange = value, true);
        Subs.CVar(_cfg, WGStasisCVars.Budget, value => _budget = value, true);
        Subs.CVar(_cfg, WGStasisCVars.Sweep, value => _sweep = value, true);
        Subs.CVar(_cfg, CVars.NetMaxUpdateRange, value => _pvsRange = value, true);

        InitializeAttendance();
        InitializeWakes();
    }

    /// <summary>Whether a grid is in stasis or going into it.</summary>
    public bool IsInStasis(EntityUid grid)
    {
        return _grids.TryGetValue(grid, out var rec) && rec.State is WGGridStasisState.Pausing or WGGridStasisState.Paused;
    }

    /// <summary>Wakes everything on a grid this tick and keeps it awake for at least <paramref name="hold"/> (60 s by default).</summary>
    public void Wake(EntityUid grid, TimeSpan? hold = null)
    {
        if (!_enabled)
            return;

        var until = _timing.CurTime + (hold ?? DefaultHold);
        if (!_holds.TryGetValue(grid, out var held) || held < until)
            _holds[grid] = until;

        if (_grids.TryGetValue(grid, out var rec))
            WakeWhole(rec);
    }

    /// <summary>Marks a grid busy for a reason: it can't enter stasis while any pin is held, and wakes now if it is in it.</summary>
    public void Pin(EntityUid grid, string reason)
    {
        if (!_pins.TryGetValue(grid, out var reasons))
            _pins[grid] = reasons = new HashSet<string>();

        reasons.Add(reason);
        if (_grids.TryGetValue(grid, out var rec))
            WakeWhole(rec);
    }

    /// <summary>Lets go of a pin; the grid's idle time counts from now.</summary>
    public void Unpin(EntityUid grid, string reason)
    {
        if (!_pins.TryGetValue(grid, out var reasons) || !reasons.Remove(reason))
            return;

        if (reasons.Count == 0)
            _pins.Remove(grid);

        if (_grids.TryGetValue(grid, out var rec))
            rec.IdleSince = null;
    }

    /// <summary>Whether a grid holds any pin.</summary>
    public bool IsPinned(EntityUid grid) => _pins.ContainsKey(grid);

    /// <summary>Whether grid stasis is switched on.</summary>
    public bool Enabled => _enabled;

    /// <summary>
    /// Drops a grid's record without unpausing anything, for a grid about to change map: the move sets every
    /// descendant's paused flag to the new map's. Pins and holds stay.
    /// </summary>
    public void Forget(EntityUid grid)
    {
        if (!_grids.Remove(grid, out var rec))
            return;

        _inFlight.Remove(rec);
        if (rec.State != WGGridStasisState.Awake)
            RaiseChanged(grid, false);
    }

    /// <summary>Looks at every grid on the next tick instead of waiting for the next scheduled look; for tests and admins.</summary>
    public void EvaluateNow()
    {
        _nextEvaluate = TimeSpan.Zero;
        _nextRegistry = TimeSpan.Zero;
        _nextDocks = TimeSpan.Zero;
    }

    /// <summary>Turned off, every grid in stasis wakes at once and nothing is watched any more.</summary>
    private void OnEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
        {
            EvaluateNow();
            return;
        }

        foreach (var rec in _grids.Values)
        {
            WakeWhole(rec);
        }

        _grids.Clear();
        _inFlight.Clear();
        _arrivals.Clear();
        _viewers.Clear();
        SampleGauges();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
        {
            PruneIdle();
            return;
        }

        _watch.Restart();
        _tickWork = string.Empty;
        var now = _timing.CurTime;
        var evaluated = now >= _nextEvaluate;
        if (evaluated)
        {
            _nextEvaluate = now + EvaluateEvery;
            if (now >= _nextRegistry)
            {
                _nextRegistry = now + RegistryEvery;
                RefreshRegistry();
            }

            if (now >= _nextDocks)
            {
                _nextDocks = now + DocksEvery;
                RebuildDocks();
                SampleGauges();
            }

            ComputeAttendance();
            Evaluate();
        }

        DoWork(evaluated);
        var ms = _watch.Elapsed.TotalMilliseconds;
        _sampleWorst = Math.Max(_sampleWorst, ms);
        if (ms > WorstTickMs)
        {
            WorstTickMs = ms;
            WorstTickWork = _tickWork;
        }
    }

    /// <summary>Decides, from who attends each grid, which to wake and which have been left long enough to pause.</summary>
    private void Evaluate()
    {
        var now = _timing.CurTime;
        _gone.Clear();
        foreach (var rec in _grids.Values)
        {
            var grid = rec.Grid;
            if (!_xformQuery.TryComp(grid, out var xform) || TerminatingOrDeleted(grid))
            {
                _gone.Add(grid);
                continue;
            }

            // Changing map resets every descendant's paused flag to the new map's, so what was recorded no longer holds.
            if (rec.State != WGGridStasisState.Awake && xform.MapUid != rec.Map)
            {
                WakeWhole(rec);
                continue;
            }

            // A paused map pauses everything on it already, and unpausing anything there would be wrong.
            if (xform.MapUid is not { } map || _metaQuery.GetComponent(map).EntityPaused)
                continue;

            if (_holds.TryGetValue(grid, out var hold) && hold <= now)
            {
                _holds.Remove(grid);
                hold = TimeSpan.Zero;
            }

            var distance = _distances.GetValueOrDefault(grid, float.MaxValue);
            rec.Distance = distance;
            var busy = rec.Policy == WGStasisPolicy.Never
                       || rec.Policy == WGStasisPolicy.PlayerShip && !_playerShips
                       || hold > now
                       || _pins.ContainsKey(grid)
                       || _ftlQuery.HasComp(grid);

            switch (rec.State)
            {
                case WGGridStasisState.Awake:
                    if (busy || distance <= StashRange)
                    {
                        rec.IdleSince = null;
                    }
                    else if (rec.IdleSince is { } since && (now - since).TotalSeconds >= EffectiveIdle)
                    {
                        if (rec.Policy == WGStasisPolicy.PlayerShip && IsWorking(grid))
                            rec.IdleSince = now;
                        else
                            BeginPausing(rec);
                    }
                    else
                    {
                        rec.IdleSince ??= now;
                    }
                    break;
                case WGGridStasisState.Pausing:
                case WGGridStasisState.Paused:
                    if (busy || distance <= WakeRange)
                        BeginWaking(rec, busy || distance <= UrgentRange);
                    break;
                case WGGridStasisState.Waking:
                    rec.Urgent |= busy || distance <= UrgentRange;
                    break;
            }
        }

        foreach (var grid in _gone)
        {
            if (_grids.Remove(grid, out var gone))
                _inFlight.Remove(gone);

            _pins.Remove(grid);
            _holds.Remove(grid);
        }
    }

    private void BeginPausing(WGStasisGrid rec)
    {
        rec.State = WGGridStasisState.Pausing;
        rec.StasisSince = _timing.CurTime;
        rec.Map = _xformQuery.TryComp(rec.Grid, out var xform) ? xform.MapUid : null;
        rec.Tops.Clear();
        rec.Paused.Clear();
        rec.Cursor = 0;
        rec.Gathered = false;
        _inFlight.Add(rec);
        RaiseChanged(rec.Grid, true);
    }

    private void BeginWaking(WGStasisGrid rec, bool urgent)
    {
        rec.State = WGGridStasisState.Waking;
        rec.StasisSince = null;
        rec.Cursor = 0;
        rec.Urgent = urgent;
        rec.Tops.Clear();
        _inFlight.Add(rec);
        RaiseChanged(rec.Grid, false);
    }

    /// <summary>Wakes all a grid paused at once and marks it awake.</summary>
    private void WakeWhole(WGStasisGrid rec)
    {
        if (rec.State == WGGridStasisState.Awake)
        {
            rec.IdleSince = null;
            return;
        }

        if (rec.State != WGGridStasisState.Waking)
            RaiseChanged(rec.Grid, false);

        WakeFrom(rec, 0);
        FinishWaking(rec);
    }

    private void FinishWaking(WGStasisGrid rec)
    {
        rec.State = WGGridStasisState.Awake;
        rec.StasisSince = null;
        rec.Paused.Clear();
        rec.Tops.Clear();
        rec.Cursor = 0;
        rec.Urgent = false;
        rec.IdleSince = null;
        _inFlight.Remove(rec);
    }

    /// <summary>Unpauses what a grid paused from an index on, unless its map is paused.</summary>
    private void WakeFrom(WGStasisGrid rec, int start)
    {
        if (_xformQuery.TryComp(rec.Grid, out var xform) && xform.MapUid is { } map && _metaQuery.TryComp(map, out var mapMeta) && mapMeta.EntityPaused)
            return;

        for (var i = start; i < rec.Paused.Count; i++)
        {
            _stash.WakeEntity(rec.Paused[i]);
        }
    }

    private void RaiseChanged(EntityUid grid, bool inStasis)
    {
        if (TerminatingOrDeleted(grid))
            return;

        var ev = new WGGridStasisChangedEvent(inStasis);
        RaiseLocalEvent(grid, ref ev);
    }

    /// <summary>
    /// Wakes grids, urgent and nearest first, then pauses, within the tick's budget; on a tick that looked at the grids,
    /// sweeps those in stasis too.
    /// </summary>
    private void DoWork(bool sweep)
    {
        _work.Clear();
        foreach (var rec in _inFlight)
        {
            if (rec.State == WGGridStasisState.Waking)
                _work.Add(rec);
        }

        _work.Sort(static (a, b) => a.Urgent != b.Urgent ? (a.Urgent ? -1 : 1) : a.Distance.CompareTo(b.Distance));
        foreach (var rec in _work)
        {
            _tickWork += $" wake {rec.Grid};";
            if (rec.Urgent)
            {
                WakeFrom(rec, rec.Cursor);
                FinishWaking(rec);
                continue;
            }

            while (rec.Cursor < rec.Paused.Count)
            {
                if (OverBudget())
                    return;

                var end = Math.Min(rec.Paused.Count, rec.Cursor + Slice);
                for (; rec.Cursor < end; rec.Cursor++)
                {
                    _stash.WakeEntity(rec.Paused[rec.Cursor]);
                }
            }

            FinishWaking(rec);
        }

        _work.Clear();
        foreach (var rec in _inFlight)
        {
            if (rec.State == WGGridStasisState.Pausing)
                _work.Add(rec);
        }

        foreach (var rec in _work)
        {
            if (OverBudget())
                return;

            _tickWork += $" pause {rec.Grid};";
            if (!rec.Gathered)
            {
                Gather(rec);
                rec.Gathered = true;
            }

            while (rec.Cursor < rec.Tops.Count)
            {
                if (OverBudget())
                    return;

                var end = Math.Min(rec.Tops.Count, rec.Cursor + Slice);
                for (; rec.Cursor < end; rec.Cursor++)
                {
                    PauseTree(rec, rec.Tops[rec.Cursor]);
                }
            }

            rec.State = WGGridStasisState.Paused;
            rec.Tops.Clear();
            rec.Cursor = 0;
            rec.NextSweep = _timing.CurTime + TimeSpan.FromSeconds(_sweep);
            _inFlight.Remove(rec);
        }

        if (!sweep)
            return;

        // Whatever has turned up on a grid in stasis since, such as a thrown item or a wandering mob, is paused with it.
        foreach (var rec in _grids.Values)
        {
            if (rec.State != WGGridStasisState.Paused || _timing.CurTime < rec.NextSweep)
                continue;

            if (OverBudget())
                return;

            rec.NextSweep = _timing.CurTime + TimeSpan.FromSeconds(_sweep);
            _tickWork += $" sweep {rec.Grid};";
            Sweep(rec);
        }
    }

    private bool OverBudget()
    {
        return _watch.Elapsed.TotalMilliseconds >= _budget;
    }

    /// <summary>Lists a grid's own children to pause, things first and mobs last, leaving walls, cables and other grids.</summary>
    private void Gather(WGStasisGrid rec)
    {
        rec.Tops.Clear();
        _mobs.Clear();
        var children = Transform(rec.Grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!_xformQuery.TryComp(child, out var xform) || _gridQuery.HasComp(child) || _stash.IsInert(child, xform)
                || ExemptionOf(child) != Exemption.None)
                continue;

            (_mobQuery.HasComp(child) ? _mobs : rec.Tops).Add(child);
        }

        rec.Tops.AddRange(_mobs);
    }

    /// <summary>Pauses the children of a grid in stasis that aren't paused yet.</summary>
    private void Sweep(WGStasisGrid rec)
    {
        var children = Transform(rec.Grid).ChildEnumerator;
        _mobs.Clear();
        while (children.MoveNext(out var child))
        {
            if (_metaQuery.TryComp(child, out var meta) && !meta.EntityPaused
                && _xformQuery.TryComp(child, out var xform) && !_gridQuery.HasComp(child) && !_stash.IsInert(child, xform)
                && ExemptionOf(child) == Exemption.None)
                _mobs.Add(child);
        }

        foreach (var child in _mobs)
        {
            PauseTree(rec, child);
        }

        _mobs.Clear();
    }

    /// <summary>
    /// Pauses an entity and all it holds, unless someone is in it: a player, a mind or a vehicle with a cabin keeps the
    /// whole of it awake.
    /// </summary>
    private void PauseTree(WGStasisGrid rec, EntityUid root)
    {
        // Gathered a tick or more ago: it may have gone or left the grid since.
        if (!_xformQuery.TryComp(root, out var rootXform) || rootXform.ParentUid != rec.Grid || TerminatingOrDeleted(root))
            return;

        _tree.Clear();
        _skipped.Clear();
        _tree.Add(root);
        for (var i = 0; i < _tree.Count; i++)
        {
            var uid = _tree[i];
            if (_stash.Blocks(uid))
                return;

            var exempt = ExemptionOf(uid);
            if (exempt != Exemption.None)
                _skipped.Add(uid);

            if (exempt == Exemption.Tree)
                continue;

            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _tree.Add(child);
            }
        }

        foreach (var uid in _tree)
        {
            if (!_skipped.Contains(uid) && (uid == root || !_stash.IsInert(uid, Transform(uid))) && _stash.PauseEntity(uid))
                rec.Paused.Add(uid);
        }
    }

    private void SampleGauges()
    {
        Array.Clear(_stateCounts);
        var paused = 0;
        foreach (var rec in _grids.Values)
        {
            var index = rec.Policy == WGStasisPolicy.Never ? 4 : (int) rec.State;
            _stateCounts[index]++;
            paused += rec.Paused.Count;
        }

        GridsGauge.WithLabels("awake").Set(_stateCounts[(int) WGGridStasisState.Awake]);
        GridsGauge.WithLabels("pausing").Set(_stateCounts[(int) WGGridStasisState.Pausing]);
        GridsGauge.WithLabels("paused").Set(_stateCounts[(int) WGGridStasisState.Paused]);
        GridsGauge.WithLabels("waking").Set(_stateCounts[(int) WGGridStasisState.Waking]);
        GridsGauge.WithLabels("excluded").Set(_stateCounts[4]);
        EntitiesGauge.Set(paused);
        WorstTickGauge.Set(_sampleWorst);
        _sampleWorst = 0;
    }
}
