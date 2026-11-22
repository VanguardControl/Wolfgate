using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Content.Server._Mono.Cleanup;
using Content.Server._Mono.Radar;
using Content.Server._WG.Shipyard;
using Content.Shared._Mono.Radar;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Serilog.Events;

namespace Content.Server._WG.ColdStorage;

/// <summary>
/// The Stored tier of Cold Storage, for ownerless wrecks: a grid that has sat in stasis long enough is saved, deleted
/// and replaced by one stand-in entity that keeps its radar blip, and is rebuilt where it was when anyone or anything
/// comes near. One grid is stored at a time, its save and deletion sliced over ticks by <see cref="WGHullRetireSystem"/>.
/// </summary>
public sealed partial class WGColdStoreSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logs = default!;
    [Dependency] private WGGridStasisSystem _stasis = default!;
    [Dependency] private WGEntityPressureSystem _pressure = default!;
    [Dependency] private WGHullRetireSystem _hullRetire = default!;
    [Dependency] private GridCleanupSystem _gridCleanup = default!;
    [Dependency] private CleanupHelperSystem _cleanupHelper = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    private static readonly TimeSpan CandidateEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IneligibleFor = TimeSpan.FromSeconds(60);

    private static readonly Gauge GridsGauge = Metrics.CreateGauge("wg_coldstore_grids", "Grids stored, each standing as one entity");
    private static readonly Gauge EntitiesGauge = Metrics.CreateGauge("wg_coldstore_entities", "Entities not loaded because their grid is stored");
    private static readonly Gauge BytesGauge = Metrics.CreateGauge("wg_coldstore_bytes", "Compressed bytes held for stored grids");
    private static readonly Gauge WorstRestoreGauge = Metrics.CreateGauge("wg_coldstore_worst_restore_ms", "Longest restore since the last sample");
    private static readonly Counter StoresCounter = Metrics.CreateCounter("wg_coldstore_stores_total", "Grids stored");
    private static readonly Counter RestoresCounter = Metrics.CreateCounter("wg_coldstore_restores_total", "Grids restored");
    private static readonly Counter AbortsCounter = Metrics.CreateCounter("wg_coldstore_aborts_total", "Stores given up, the grid put back");
    private static readonly Counter FailuresCounter = Metrics.CreateCounter("wg_coldstore_failures_total", "Restores whose load failed");
    private static readonly Counter ExpiredCounter = Metrics.CreateCounter("wg_coldstore_expired_total", "Stored grids that grid cleanup's clock ran out on");

    /// <summary>Where stored grids' bytes are kept.</summary>
    public IWGColdStoreBackend Backend { get; internal set; } = new WGColdStoreMemoryBackend();

    /// <summary>Stored grids by their stand-in.</summary>
    private readonly Dictionary<EntityUid, WGStoredGrid> _stored = new();

    /// <summary>Grids never to be stored this round, with the Fluent id of why.</summary>
    private readonly Dictionary<EntityUid, string> _neverStore = new();

    /// <summary>Grids found ineligible lately, not looked at again until then.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _skipUntil = new();

    private ErrorCounter _serializerErrors = default!;
    private StoreJob? _job;
    private TimeSpan _nextCandidates;

    private bool _enabled;
    private float _idle;
    private int _minEntities;
    private float _restoreRange;
    private float _maxRestoreMs;
    private bool _playerShips;

    /// <summary>Counts since the round began, for admins and tests.</summary>
    public int Stores, Restores, Aborts, Failures, Expired;

    /// <summary>The last store finished: entities, save, write and delete time, and bytes.</summary>
    public (string Name, int Entities, double SaveMs, int SaveTicks, double WriteMs, double DeleteMs, int Bytes) LastStore { get; private set; }

    /// <summary>Stored grids by their stand-in.</summary>
    public IReadOnlyDictionary<EntityUid, WGStoredGrid> Stored => _stored;

    /// <summary>Entities not loaded because their grid is stored.</summary>
    public int StoredEntities { get; private set; }

    /// <summary>Seconds a grid must have been in stasis before it is stored, at the current entity pressure.</summary>
    public float EffectiveIdle => _idle * _pressure.Scale(1f, 0.25f);

    /// <summary>Whether a store is under way.</summary>
    public bool Storing => _job != null;

    private sealed class StoreJob
    {
        public EntityUid Grid;
        public WGStoredGrid Record = default!;
        public WGParkedCopy Copy = default!;
        public Task<(byte[]? Bytes, double Ms)>? Zip;
        public double WriteMs;
        public bool Deleting;
    }

    /// <summary>
    /// Counts errors the entity serializer logs on the main thread while a kept copy's slice runs, which a store treats
    /// as a failed save. Other saves between slices, or on other threads, aren't counted.
    /// </summary>
    private sealed class ErrorCounter(WGHullRetireSystem retire) : ILogHandler
    {
        public void Log(string sawmillName, LogEvent message)
        {
            if (message.Level >= LogEventLevel.Error
                && retire.SlicingThread == Environment.CurrentManagedThreadId
                && retire.Slicing is { } copy)
                copy.Errors++;
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, WGColdStoreCVars.Enabled, value => _enabled = value, true);
        Subs.CVar(_cfg, WGColdStoreCVars.Idle, value => _idle = value, true);
        Subs.CVar(_cfg, WGColdStoreCVars.MinEntities, value => _minEntities = value, true);
        Subs.CVar(_cfg, WGColdStoreCVars.RestoreRange, value => _restoreRange = value, true);
        Subs.CVar(_cfg, WGColdStoreCVars.MaxRestoreMs, value => _maxRestoreMs = value, true);
        Subs.CVar(_cfg, WGColdStoreCVars.RestoreBudgetMs, value => _restoreBudgetMs = value, true);
        Subs.CVar(_cfg, WGColdStoreCVars.PlayerShips, value => _playerShips = value, true);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<WGStoredGridComponent, EntityTerminatingEvent>(OnStandInTerminating);
        _serializerErrors = new ErrorCounter(_hullRetire);
        _logs.GetSawmill("entity_serializer").AddHandler(_serializerErrors);

        InitializeEligibility();
        InitializeRestore();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        AbortStagedRestore();
        _logs.GetSawmill("entity_serializer").RemoveHandler(_serializerErrors);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_job != null)
            StepJob(_job);

        GiveBackKept();

        var now = _timing.CurTime;
        if (!_enabled || !_stasis.Enabled)
        {
            QueueAll();
        }
        else
        {
            UpdateRestoreTriggers(now);
            if (now >= _nextCandidates && _job == null)
            {
                _nextCandidates = now + CandidateEvery;
                PickCandidate(now);
            }
        }

        RestoreNext(now);
    }

    /// <summary>Looks for one grid that has been in stasis long enough and may be stored, and starts storing it.</summary>
    private void PickCandidate(TimeSpan now)
    {
        var idle = TimeSpan.FromSeconds(EffectiveIdle);
        foreach (var (grid, rec) in _stasis.Grids)
        {
            if (rec.Policy != WGStasisPolicy.Ownerless
                || rec.State != WGGridStasisState.Paused
                || rec.StasisSince is not { } since
                || now - since < idle
                || _neverStore.ContainsKey(grid)
                || _skipUntil.TryGetValue(grid, out var until) && until > now)
                continue;

            if (Ineligible(grid, false) is { } reason)
            {
                _skipUntil[grid] = now + IneligibleFor;
                Log.Debug($"Not storing {ToPrettyString(grid)}: {reason}.");
                continue;
            }

            BeginStore(grid);
            return;
        }
    }

    /// <summary>
    /// Stores a grid now if it may be stored. <paramref name="force"/>, for admins and tests, skips having been in
    /// stasis. Gives the Fluent id of why not otherwise.
    /// </summary>
    public bool TryStore(EntityUid grid, bool force, out string? reason)
    {
        if (!_enabled || !_stasis.Enabled)
            reason = "wg-coldstore-why-disabled";
        else
            reason = _job != null ? "wg-coldstore-why-busy" : Ineligible(grid, force);

        if (reason != null)
            return false;

        BeginStore(grid);
        return true;
    }

    private void BeginStore(EntityUid grid)
    {
        var xform = Transform(grid);
        var gridComp = Comp<MapGridComponent>(grid);
        var iff = CompOrNull<IFFComponent>(grid);
        var record = new WGStoredGrid
        {
            Key = Guid.NewGuid().ToString("N"),
            Name = MetaData(grid).EntityName,
            Map = xform.MapUid!.Value,
            MapId = xform.MapID,
            Position = xform.LocalPosition,
            Rotation = xform.LocalRotation,
            LocalBounds = gridComp.LocalAABB,
            WorldBounds = _transform.GetWorldMatrix(xform).TransformBox(gridComp.LocalAABB),
            Mass = CompOrNull<PhysicsComponent>(grid)?.Mass ?? 0f,
            IffFlags = iff?.Flags ?? IFFFlags.None,
            IffColor = iff?.Color,
            Entities = CountTree(grid),
            StoredAt = _timing.CurTime,
            Hollow = _stasis.PolicyOf(grid, xform) == WGStasisPolicy.PlayerShip,
        };

        record.CleanupCandidate = _gridCleanup.WGColdStoreCandidate(grid, out record.CleanupAccumulator,
            out record.CleanupRate, out record.CleanupDistance, out record.CleanupDistanceFixed);
        record.CleanupSeen = record.StoredAt;

        // The park map is paused, which resets every paused flag on the way; nothing is left for stasis to undo.
        _stasis.Forget(grid);
        KeepUnsaved(grid);
        _job = new StoreJob
        {
            Grid = grid,
            Record = record,
            Copy = _hullRetire.CopyParked(grid),
        };
    }

    /// <summary>Moves a store on: waits for the copy and its compression, then leaves the stand-in and deletes the grid.</summary>
    private void StepJob(StoreJob job)
    {
        if (job.Deleting)
        {
            if (job.Record.Hollow)
            {
                StepHollow(job);
                return;
            }

            if (!Deleted(job.Grid))
                return;

            FinishStore(job);
            return;
        }

        if (!job.Copy.Done)
            return;

        if (job.Zip == null)
        {
            if (!job.Copy.Text.IsCompleted)
                return;

            var text = job.Copy.Text.GetAwaiter().GetResult();
            if (text == null || job.Copy.Error != null)
            {
                Abort(job, job.Copy.Error ?? "the copy was not written");
                return;
            }

            if (job.Copy.Errors != 0)
            {
                Abort(job, "the serializer logged errors");
                return;
            }

            job.Zip = Task.Run(() => Compress(text));
            return;
        }

        if (!job.Zip.IsCompleted)
            return;

        var (bytes, writeMs) = job.Zip.GetAwaiter().GetResult();
        job.WriteMs = writeMs;
        if (bytes == null)
        {
            Abort(job, "compression failed");
            return;
        }

        Commit(job, bytes);
    }

    /// <summary>Keeps the bytes, puts the stand-in where the grid was and hands the grid to be deleted.</summary>
    private void Commit(StoreJob job, byte[] bytes)
    {
        var record = job.Record;
        if (Deleted(job.Grid))
        {
            _job = null;
            return;
        }

        if (!Exists(record.Map))
        {
            // The map went with everything on it; so does the wreck.
            Log.Warning($"Map of {record.Name} is gone while storing it; deleting it as the map would have.");
            _hullRetire.Retire(job.Grid);
            _job = null;
            return;
        }

        record.Bytes = bytes.Length;
        Backend.Put(record.Key, bytes, new WGColdStoreMeta(record.Name, record.Entities, record.StoredAt));
        if (record.Hollow)
        {
            // The grid stays; from here atmos and grid cleanup must leave it alone, as its walls are about to go.
            MarkStored(job.Grid, record);
            _stasis.SetHollow(job.Grid, true);
            _hullRetire.RetireContents(job.Grid);
            job.Deleting = true;
            return;
        }

        record.StandIn = SpawnStandIn(record);
        _stored[record.StandIn] = record;
        StoredEntities += record.Entities;

        var ev = new WGGridStoredEvent(job.Grid, record.StandIn);
        RaiseLocalEvent(ref ev);

        _hullRetire.Retire(job.Grid);
        job.Deleting = true;
        Stores++;
        StoresCounter.Inc();
    }

    /// <summary>
    /// Once a kept grid has been emptied, puts it back where it was as its own stand-in.
    /// </summary>
    private void StepHollow(StoreJob job)
    {
        var record = job.Record;
        if (Deleted(job.Grid))
        {
            _hullRetire.Emptied.Remove(job.Grid);
            Backend.Remove(record.Key);
            _job = null;
            return;
        }

        if (!_hullRetire.Emptied.Remove(job.Grid))
            return;

        if (!Exists(record.Map))
        {
            Log.Warning($"Map of {record.Name} is gone while storing it; deleting it as the map would have.");
            Backend.Remove(record.Key);
            _stasis.SetHollow(job.Grid, false);
            _hullRetire.ReleaseParked(job.Grid);
            Del(job.Grid);
            _job = null;
            return;
        }

        _transform.SetCoordinates(job.Grid, new EntityCoordinates(record.Map, record.Position));
        _transform.SetLocalRotation(job.Grid, record.Rotation);
        Still(job.Grid);

        record.StandIn = job.Grid;
        _stored[job.Grid] = record;
        StoredEntities += record.Entities;

        var ev = new WGGridStoredEvent(job.Grid, job.Grid);
        RaiseLocalEvent(ref ev);

        Stores++;
        StoresCounter.Inc();
        FinishStore(job);
    }

    private void MarkStored(EntityUid uid, WGStoredGrid record)
    {
        var stored = EnsureComp<WGStoredGridComponent>(uid);
        stored.Key = record.Key;
        stored.Bounds = record.WorldBounds;
        stored.Entities = record.Entities;
        stored.GridName = record.Name;
    }

    private void FinishStore(StoreJob job)
    {
        var record = job.Record;
        // The grid is gone before stasis saw it again, so nothing else would drop its park pin.
        _hullRetire.ReleaseParked(job.Grid);
        var deleted = _hullRetire.Last;
        var deleteMs = deleted.Name.Contains(record.Name) ? deleted.DeleteMs : 0;
        LastStore = (record.Name, record.Entities, job.Copy.CopyMs, job.Copy.CopyTicks, job.WriteMs, deleteMs, record.Bytes);
        Log.Info($"Stored {record.Name} ({record.Key}): {record.Entities} entities, save {job.Copy.CopyMs:0.#} ms over {job.Copy.CopyTicks} ticks (worst {job.Copy.WorstMs:0.#}), write and gzip {job.WriteMs:0} ms on a worker, delete {deleteMs:0.#} ms, {record.Bytes} bytes.");
        _job = null;
    }

    /// <summary>Puts a grid whose store failed back exactly where it was, and never tries it again this round.</summary>
    private void Abort(StoreJob job, string why)
    {
        _job = null;
        Aborts++;
        AbortsCounter.Inc();
        var record = job.Record;
        _neverStore[job.Grid] = "wg-coldstore-why-aborted";
        if (Deleted(job.Grid))
            return;

        if (!Exists(record.Map))
        {
            // Nowhere to put it back: it goes as the map would have taken it.
            _hullRetire.Retire(job.Grid);
            Log.Warning($"Could not store {record.Name}: {why}. Its map is gone, so it is deleted from the parking map.");
            return;
        }

        _hullRetire.ReleaseParked(job.Grid);
        DropKept(job.Grid);
        _transform.SetCoordinates(job.Grid, new EntityCoordinates(record.Map, record.Position));
        _transform.SetLocalRotation(job.Grid, record.Rotation);
        Still(job.Grid);

        Log.Warning($"Could not store {record.Name}: {why}. It is back where it was and won't be stored again this round.");
    }

    /// <summary>The stand-in: one entity in space where the grid was, showing the grid's outline on radar.</summary>
    private EntityUid SpawnStandIn(WGStoredGrid record)
    {
        // Made off the map and only then placed, so it is never picked up by a grid it stands over.
        var standIn = Spawn(null, MapCoordinates.Nullspace);
        Transform(standIn).GridTraversal = false;
        var centre = record.WorldBounds.Center;
        _transform.SetCoordinates(standIn, new EntityCoordinates(record.Map, centre));
        _meta.SetEntityName(standIn, record.Name);

        var stored = AddComp<WGStoredGridComponent>(standIn);
        stored.Key = record.Key;
        stored.Bounds = record.WorldBounds;
        stored.Entities = record.Entities;
        stored.GridName = record.Name;

        // A loose entity in space with a body is space cleanup's to take; this one is grid cleanup's, through its record.
        EnsureComp<CleanupImmuneComponent>(standIn);

        // A hidden grid leaves no trace on radar.
        if ((record.IffFlags & IFFFlags.Hide) != 0)
            return standIn;

        // Radar blips need a body: a static one with no fixtures, which nothing moves or collides with.
        var body = EnsureComp<PhysicsComponent>(standIn);
        _physics.SetBodyType(standIn, BodyType.Static, body: body);
        _physics.SetCanCollide(standIn, false, body: body);

        var half = record.WorldBounds.Size / 2f;
        var blip = EnsureComp<RadarBlipComponent>(standIn);
        blip.Config = new BlipConfig
        {
            Bounds = new Box2(-half, half),
            Color = record.IffColor ?? IFFComponent.IFFColor,
            Shape = RadarBlipShape.Square,
            RespectZoom = true,
            Rotate = true,
        };
        blip.MaxDistance = 4096f;
        return standIn;
    }

    /// <summary>Zeroes a grid's motion.</summary>
    private void Still(EntityUid grid)
    {
        if (!TryComp<PhysicsComponent>(grid, out var body))
            return;

        _physics.SetLinearVelocity(grid, System.Numerics.Vector2.Zero, body: body);
        _physics.SetAngularVelocity(grid, 0f, body: body);
    }

    private int CountTree(EntityUid root)
    {
        var count = 0;
        _walk.Clear();
        _walk.Add(root);
        for (var i = 0; i < _walk.Count; i++)
        {
            count++;
            var children = Transform(_walk[i]).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _walk.Add(child);
            }
        }

        _walk.Clear();
        return count;
    }

    private static (byte[]? Bytes, double Ms) Compress(string text)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
            {
                var raw = Encoding.UTF8.GetBytes(text);
                gzip.Write(raw, 0, raw.Length);
            }

            return (output.ToArray(), watch.Elapsed.TotalMilliseconds);
        }
        catch (Exception)
        {
            return (null, watch.Elapsed.TotalMilliseconds);
        }
    }

    private static string Decompress(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>A stand-in deleted by anything but a restore takes its grid with it.</summary>
    private void OnStandInTerminating(Entity<WGStoredGridComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!_stored.Remove(ent.Owner, out var record))
            return;

        AbortStagedRestore(record);
        _stasis.SetHollow(ent.Owner, false);
        Backend.Remove(record.Key);
        StoredEntities -= record.Entities;
        _restoreQueue.Remove(ent.Owner);
        Log.Warning($"Stand-in of stored grid {record.Name} ({record.Key}) was deleted; the grid is gone with it.");
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        AbortStagedRestore();
        _stored.Clear();
        _neverStore.Clear();
        _skipUntil.Clear();
        _restoreQueue.Clear();
        Backend.Clear();
        StoredEntities = 0;
        _job = null;
        _stagingMap = null;
        Stores = Restores = Aborts = Failures = Expired = 0;
    }

    private void SampleGauges()
    {
        GridsGauge.Set(_stored.Count);
        EntitiesGauge.Set(StoredEntities);
        BytesGauge.Set(Backend.TotalBytes);
        WorstRestoreGauge.Set(_sampleWorstRestore);
        _sampleWorstRestore = 0;
    }
}
