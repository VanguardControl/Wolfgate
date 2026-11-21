using System.Diagnostics;
using System.IO;
using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._WG.Shipyard;
using Content.Shared._WG.Explosion;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.ColdStorage;

/// <summary>Bringing a stored grid back where it was when a viewer, a grid or a blast comes near, or an admin asks.</summary>
public sealed partial class WGColdStoreSystem
{
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private UsedShipReinitSystem _reinit = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private static readonly TimeSpan ViewersEvery = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan GridsEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RestoreHold = TimeSpan.FromSeconds(60);

    /// <summary>Loads tried before a stored grid is left for an admin.</summary>
    public const int MaxFailures = 3;

    /// <summary>How many times a restored hull is moved on to clear a grid it landed on.</summary>
    private const int ClearAttempts = 8;

    /// <summary>Stand-ins to restore, by how near what woke them is; the nearest goes first, one a tick.</summary>
    private readonly Dictionary<EntityUid, float> _restoreQueue = new();

    private List<Entity<MapGridComponent>> _overlaps = new();
    private EntityUid? _stagingMap;
    private TimeSpan _nextViewers;
    private TimeSpan _nextGrids;
    private double _sampleWorstRestore;

    /// <summary>The last restore: entities, load and total time, and bytes.</summary>
    public (string Name, int Entities, double LoadMs, double TotalMs, int Bytes) LastRestore { get; private set; }

    private void InitializeRestore()
    {
        SubscribeLocalEvent<ExplosionShockwaveEvent>(OnShockwave);
    }

    /// <summary>Queues the stand-ins a viewer, an arrival or another grid has come near.</summary>
    private void UpdateRestoreTriggers(TimeSpan now)
    {
        if (now >= _nextViewers)
        {
            _nextViewers = now + ViewersEvery;
            foreach (var (standIn, record) in _stored)
            {
                var distance = NearestViewer(record.MapId, record.WorldBounds);
                if (distance <= _restoreRange)
                    Queue(standIn, distance);
            }
        }

        if (now < _nextGrids)
            return;

        _nextGrids = now + GridsEvery;
        foreach (var (standIn, record) in _stored)
        {
            if (GridNear(null, record.MapId, record.WorldBounds))
                Queue(standIn, _restoreRange / 2f);
        }

        AdvanceCleanup(now);
        SampleGauges();
    }

    private void Queue(EntityUid standIn, float priority)
    {
        if (!_restoreQueue.TryGetValue(standIn, out var known) || priority < known)
            _restoreQueue[standIn] = priority;
    }

    /// <summary>Whether a stored grid may be tried now: not failed, or due a retry under the cap, or asked for by an admin.</summary>
    private static bool Due(WGStoredGrid record, TimeSpan now)
    {
        return record.State != WGStoredState.Failed
               || record.RetryOverride
               || record.Failures < MaxFailures && now >= record.NextRetry;
    }

    /// <summary>Whether a stored grid will never be tried again without an admin.</summary>
    private static bool GivenUp(WGStoredGrid record)
    {
        return record.State == WGStoredState.Failed && record.Failures >= MaxFailures && !record.RetryOverride;
    }

    /// <summary>
    /// Queues every stored grid to come back, one a tick; for admins. Each gets one more try even past the failure
    /// cap. Gives how many were queued.
    /// </summary>
    public int QueueRestoreAll()
    {
        foreach (var (standIn, record) in _stored)
        {
            record.RetryOverride = true;
            Queue(standIn, float.MaxValue);
        }

        return _stored.Count;
    }

    /// <summary>With storing off, every stored grid comes back, one a tick.</summary>
    private void QueueAll()
    {
        foreach (var (standIn, record) in _stored)
        {
            if (!GivenUp(record))
                Queue(standIn, float.MaxValue);
        }

        if (_timing.CurTime >= _nextGrids)
        {
            _nextGrids = _timing.CurTime + GridsEvery;
            SampleGauges();
        }
    }

    /// <summary>A blast wakes what is stored about it, as it wakes grids in stasis.</summary>
    private void OnShockwave(ref ExplosionShockwaveEvent args)
    {
        if (args.Epicenter.MapId == MapId.Nullspace || _stored.Count == 0)
            return;

        var radius = args.Iterations + 2f + WGGridStasisSystem.Margin;
        foreach (var (standIn, record) in _stored)
        {
            if (record.MapId == args.Epicenter.MapId && Distance(record.WorldBounds, args.Epicenter.Position) <= radius)
                Queue(standIn, 0f);
        }
    }

    /// <summary>
    /// Restores the nearest queued stand-in that is due. Failed ones waiting for a retry stay queued; gone ones and
    /// ones given up on are dropped.
    /// </summary>
    private void RestoreNext(TimeSpan now)
    {
        if (_restoreQueue.Count == 0)
            return;

        _dropped.Clear();
        WGStoredGrid? best = null;
        var bestPriority = float.MaxValue;
        foreach (var (standIn, priority) in _restoreQueue)
        {
            if (!_stored.TryGetValue(standIn, out var record) || GivenUp(record))
            {
                _dropped.Add(standIn);
                continue;
            }

            if (!Due(record, now))
                continue;

            if (best == null || priority < bestPriority)
            {
                best = record;
                bestPriority = priority;
            }
        }

        foreach (var standIn in _dropped)
        {
            _restoreQueue.Remove(standIn);
        }

        if (best == null)
            return;

        _restoreQueue.Remove(best.StandIn);
        TryRestore(best, bestPriority);
    }

    /// <summary>Restores a stored grid now, whatever its state; for admins and tests. False if it failed.</summary>
    public bool RestoreNow(EntityUid standIn)
    {
        _restoreQueue.Remove(standIn);
        return _stored.TryGetValue(standIn, out var record) && TryRestore(record, 0f);
    }

    /// <summary>Whether a stored grid's bytes are still held.</summary>
    public bool HasBytes(EntityUid standIn)
    {
        return _stored.TryGetValue(standIn, out var record) && Backend.TryGet(record.Key, out _);
    }

    /// <summary>
    /// Loads a stored grid onto a paused staging map, redoes what loading skips, and moves it where its stand-in is.
    /// A failure keeps the stand-in and the bytes, and queues a retry at <paramref name="priority"/> until the cap.
    /// </summary>
    private bool TryRestore(WGStoredGrid record, float priority)
    {
        record.RetryOverride = false;
        if (!Backend.TryGet(record.Key, out var bytes))
        {
            Fail(record, "its bytes are missing", priority);
            return false;
        }

        var watch = Stopwatch.StartNew();
        var staging = Staging();
        Entity<MapGridComponent>? loaded;
        double loadMs;
        try
        {
            var text = Decompress(bytes);
            var loadStart = watch.Elapsed.TotalMilliseconds;
            if (!_loader.TryLoadGrid(Comp<MapComponent>(staging).MapId, new StringReader(text), record.Key, out loaded))
            {
                DropStaging();
                Fail(record, "the load returned nothing", priority);
                return false;
            }

            loadMs = watch.Elapsed.TotalMilliseconds - loadStart;
        }
        catch (Exception e)
        {
            DropStaging();
            Fail(record, $"{e.GetType().Name}: {e.Message}", priority);
            return false;
        }

        var grid = loaded.Value.Owner;
        _reinit.ReinitLoadedShip(grid);

        _transform.SetCoordinates(grid, new EntityCoordinates(record.Map, record.Position));
        _transform.SetLocalRotation(grid, record.Rotation);
        Still(grid);
        if (!ClearOverlaps(grid))
        {
            // The copy goes; the stand-in and bytes stay for a later try, when the way may be clear.
            Del(grid);
            Fail(record, "it would overlap another grid wherever it was moved", priority);
            return false;
        }

        var cleanup = EnsureComp<GridCleanupGridComponent>(grid);
        cleanup.CleanupAccumulator = record.CleanupAccumulator;
        _stasis.Wake(grid, RestoreHold);

        var standIn = record.StandIn;
        var ev = new WGGridRestoredEvent(standIn, grid);
        RaiseLocalEvent(ref ev);

        Forget(record);
        Del(standIn);

        var total = watch.Elapsed.TotalMilliseconds;
        _sampleWorstRestore = Math.Max(_sampleWorstRestore, total);
        LastRestore = (record.Name, record.Entities, loadMs, total, record.Bytes);
        Restores++;
        RestoresCounter.Inc();
        var message = $"Restored {record.Name} ({record.Key}): {record.Entities} entities, load {loadMs:0.#} ms, all {total:0.#} ms in one tick, {record.Bytes} bytes.";
        if (total > _maxRestoreMs)
            Log.Warning($"{message} Over wg.coldstore.max_restore_ms ({_maxRestoreMs:0} ms).");
        else
            Log.Info(message);

        return true;
    }

    /// <summary>Marks a failed try and keeps a retry queued until the cap, so a one-off trigger isn't lost.</summary>
    private void Fail(WGStoredGrid record, string why, float priority)
    {
        record.State = WGStoredState.Failed;
        record.Failures++;
        record.NextRetry = _timing.CurTime + RetryEvery;
        record.LastError = why;
        Failures++;
        FailuresCounter.Inc();
        var retry = record.Failures < MaxFailures;
        if (retry)
            Queue(record.StandIn, priority);

        var kept = Backend.TryGet(record.Key, out _) ? "Its stand-in and bytes are kept" : "Its stand-in is kept";
        var next = retry ? $"; tried again in {RetryEvery.TotalSeconds:0} s" : "; left for an admin";
        Log.Error($"Could not restore stored grid {record.Name} ({record.Key}), try {record.Failures} of {MaxFailures}: {why}. {kept}{next}.");
    }

    /// <summary>Drops a stored grid's record and bytes; the stand-in is the caller's.</summary>
    private void Forget(WGStoredGrid record)
    {
        _stored.Remove(record.StandIn);
        _restoreQueue.Remove(record.StandIn);
        Backend.Remove(record.Key);
        StoredEntities -= record.Entities;
    }

    private EntityUid Staging()
    {
        if (_stagingMap is { } existing && Exists(existing))
            return existing;

        _stagingMap = _map.CreateMap(out var mapId);
        _map.SetPaused(mapId, true);
        return _stagingMap.Value;
    }

    /// <summary>Throws away the staging map with whatever a failed load left on it.</summary>
    private void DropStaging()
    {
        if (_stagingMap is { } map && Exists(map))
            Del(map);

        _stagingMap = null;
    }

    /// <summary>Moves a restored hull off any grid it landed on, a hull's width at a time. False if it is still on one.</summary>
    private bool ClearOverlaps(EntityUid grid)
    {
        var xform = Transform(grid);
        for (var i = 0; ; i++)
        {
            var bounds = _lookup.GetWorldAABB(grid, xform);
            _overlaps.Clear();
            _map.FindGridsIntersecting(xform.MapID, bounds.Enlarged(2f), ref _overlaps, includeMap: false);
            var other = EntityUid.Invalid;
            foreach (var found in _overlaps)
            {
                if (found.Owner != grid)
                {
                    other = found.Owner;
                    break;
                }
            }

            if (!other.IsValid())
                return true;

            if (i == ClearAttempts)
                return false;

            var away = bounds.Center - _lookup.GetWorldAABB(other).Center;
            var direction = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : Vector2.UnitY;
            var step = MathF.Max(bounds.Width, bounds.Height) + 20f;
            _transform.SetWorldPosition(grid, _transform.GetWorldPosition(xform) + direction * step);
        }
    }

    /// <summary>
    /// Runs grid cleanup's clock on stored candidates: reset by a player near, else advanced; one that runs out is
    /// deleted with its bytes, as grid cleanup would have deleted the grid. Players are measured from the grid's
    /// position, and a grid's own distance override isn't scaled by pressure, both as grid cleanup does.
    /// </summary>
    private void AdvanceCleanup(TimeSpan now)
    {
        var duration = _gridCleanup.WGCleanupDuration;
        _expired.Clear();
        foreach (var (standIn, record) in _stored)
        {
            if (!record.CleanupCandidate)
                continue;

            var seconds = (float) (now - record.CleanupSeen).TotalSeconds;
            record.CleanupSeen = now;
            var distance = record.CleanupDistanceFixed
                ? record.CleanupDistance
                : record.CleanupDistance * _gridCleanup.WGRadiusScale;
            if (_cleanupHelper.HasNearbyPlayers(new EntityCoordinates(record.Map, record.Position), distance))
            {
                record.CleanupAccumulator = TimeSpan.Zero;
                continue;
            }

            if (seconds <= 0f)
                continue;

            record.CleanupAccumulator += TimeSpan.FromSeconds(seconds * record.CleanupRate);
            if (record.CleanupAccumulator >= duration)
                _expired.Add(standIn);
        }

        foreach (var standIn in _expired)
        {
            if (!_stored.TryGetValue(standIn, out var record))
                continue;

            Forget(record);
            Del(standIn);
            Expired++;
            ExpiredCounter.Inc();
            Log.Info($"Stored grid {record.Name} ({record.Key}) ran out grid cleanup's clock; deleted with its {record.Bytes} bytes.");
        }
    }

    private readonly List<EntityUid> _expired = new();
    private readonly List<EntityUid> _dropped = new();
}
