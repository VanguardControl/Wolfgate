using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.ColdStorage;
using Content.Server.Shuttles.Systems;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Events;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Shipyard;

/// <summary>
/// Copies and deletes a sold hull a few milliseconds per tick, since doing it at once stalled the server for most of a
/// second. The hull waits on a paused map, which keeps it still between ticks and out of everyone's sight.
/// </summary>
public sealed partial class WGHullRetireSystem : EntitySystem
{
    [Dependency] private IDependencyCollection _dependencies = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private WGGridStasisSystem _stasis = default!;

    /// <summary>Held until the hull is deleted.</summary>
    private const string StasisPin = "hull-retire";

    private const double TickBudgetMs = 4;

    /// <summary>Space between parked hulls, so they can't overlap.</summary>
    private const float ParkSpacing = 2000f;

    private readonly Queue<RetiringHull> _hulls = new();
    private readonly Stopwatch _timer = new();
    private EntityUid? _parkMap;
    private float _parkOffset;

    /// <summary>The last hull finished, for logs and tests: time spent, ticks taken and the longest tick.</summary>
    public (string Name, double CopyMs, int CopyTicks, double DeleteMs, int DeleteTicks, double WorstMs) Last { get; private set; }

    /// <summary>Makes the next kept copy throw, for tests of what a failed save leaves.</summary>
    internal bool FailNextKeptCopy;

    /// <summary>The kept copy whose slice is running on the main thread right now, so errors logged meanwhile are its own.</summary>
    public WGParkedCopy? Slicing { get; private set; }

    /// <summary>The thread running <see cref="Slicing"/>; 0 when none is.</summary>
    public int SlicingThread { get; private set; }

    private sealed class RetiringHull
    {
        public EntityUid Grid;
        public string Name = string.Empty;
        public HullCopy? Copy;

        /// <summary>Copied only: the hull stays parked afterwards until <see cref="Retire"/> or <see cref="ReleaseParked"/>.</summary>
        public bool Keep;

        /// <summary>Only what is on the grid is deleted; the grid itself stays parked for whoever asked.</summary>
        public bool KeepGrid;
        public List<EntityUid>? Children;
        public int NextChild;
        public double CopyMs;
        public int CopyTicks;
        public double DeleteMs;
        public int DeleteTicks;
        public double WorstMs;
    }

    private sealed class HullCopy
    {
        public readonly TaskCompletionSource<string?> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<EntityUid> Carry = new();
        public EntitySerializer? Serializer;
        public HashSet<EntityUid> Roots = new();
        public List<EntityUid> Entities = new();
        public int Next;
        public bool Retried;

        /// <summary>Set for a kept copy, which is never stripped: a throw fails it.</summary>
        public WGParkedCopy? Parked;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>Parks the hull, and deletes it once any copy of it is done.</summary>
    public void Retire(EntityUid grid)
    {
        GetOrPark(grid);
    }

    /// <summary>
    /// Parks the hull and deletes everything on it in slices, leaving the grid itself parked and empty. The grid is
    /// in <see cref="Emptied"/> once that is done.
    /// </summary>
    public void RetireContents(EntityUid grid)
    {
        GetOrPark(grid).KeepGrid = true;
    }

    /// <summary>Grids <see cref="RetireContents"/> has finished with; whoever asked takes theirs out.</summary>
    public readonly HashSet<EntityUid> Emptied = new();

    /// <summary>
    /// Retires the hull and copies it first. The task gives its YAML, or null if the copy failed.
    /// </summary>
    public Task<string?> RetireWithCopy(EntityUid grid, List<EntityUid> carry)
    {
        var hull = GetOrPark(grid);
        hull.Copy ??= new HullCopy { Carry = carry };
        return hull.Copy.Result.Task;
    }

    /// <summary>
    /// Parks the hull and copies it in slices as it stands, without deleting it or stripping anything. It stays parked
    /// and pinned until <see cref="Retire"/> deletes it or <see cref="ReleaseParked"/> lets it go.
    /// </summary>
    public WGParkedCopy CopyParked(EntityUid grid)
    {
        var hull = GetOrPark(grid);
        hull.Keep = true;
        hull.Copy ??= new HullCopy();
        hull.Copy.Parked ??= new WGParkedCopy { Text = hull.Copy.Result.Task };
        return hull.Copy.Parked;
    }

    /// <summary>Drops the park pin of a hull from <see cref="CopyParked"/>: one put back, or one already deleted.</summary>
    public void ReleaseParked(EntityUid grid)
    {
        _stasis.Unpin(grid, StasisPin);
    }

    private RetiringHull GetOrPark(EntityUid grid)
    {
        foreach (var existing in _hulls)
        {
            if (existing.Grid == grid)
                return existing;
        }

        var hull = new RetiringHull { Grid = grid, Name = ToPrettyString(grid).ToString() };
        _stasis.Pin(grid, StasisPin);
        Park(grid);
        _hulls.Enqueue(hull);
        return hull;
    }

    private void Park(EntityUid grid)
    {
        // Copied and kept earlier: already parked.
        if (_parkMap != null && Transform(grid).MapUid == _parkMap)
            return;

        _docking.UndockDocks(grid);
        KeepPlayersBehind(grid);

        if (!Exists(_parkMap))
        {
            _parkMap = _maps.CreateMap(out var mapId);
            _maps.SetPaused(mapId, true);
            _parkOffset = 0;
        }

        _parkOffset += ParkSpacing;
        _transform.SetCoordinates(grid, new EntityCoordinates(_parkMap.Value, new Vector2(_parkOffset, 0f)));
    }

    /// <summary>Ghosts can drift over a hull being sold; they stay where they are when it goes.</summary>
    private void KeepPlayersBehind(EntityUid grid)
    {
        if (Transform(grid).MapUid is not { } map)
            return;

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } player
                || Transform(player).GridUid != grid
                || _containers.IsEntityOrParentInContainer(player))
            {
                continue;
            }

            var position = _transform.GetWorldPosition(player);
            _transform.SetCoordinates(player, new EntityCoordinates(map, position));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_hulls.Count == 0)
            return;

        _timer.Restart();
        while (_timer.Elapsed.TotalMilliseconds < TickBudgetMs && _hulls.TryPeek(out var hull))
        {
            if (Deleted(hull.Grid))
            {
                hull.Copy?.Result.TrySetResult(null);
                FinishKept(hull, "deleted");
                _hulls.Dequeue();
                continue;
            }

            if (hull.Copy is { Next: >= 0 } copy)
            {
                var start = _timer.Elapsed.TotalMilliseconds;
                CopySlice(hull, copy);
                var spent = _timer.Elapsed.TotalMilliseconds - start;
                hull.CopyMs += spent;
                hull.CopyTicks++;
                hull.WorstMs = Math.Max(hull.WorstMs, spent);

                if (copy.Next >= 0)
                    continue;
            }

            if (hull.Keep)
            {
                FinishKept(hull, null);
                _hulls.Dequeue();
                continue;
            }

            var deleteStart = _timer.Elapsed.TotalMilliseconds;
            var deleted = DeleteSlice(hull);
            var deleteSpent = _timer.Elapsed.TotalMilliseconds - deleteStart;
            hull.DeleteMs += deleteSpent;
            hull.DeleteTicks++;
            hull.WorstMs = Math.Max(hull.WorstMs, deleteSpent);

            if (!deleted)
                continue;

            _hulls.Dequeue();
            if (hull.KeepGrid)
                Emptied.Add(hull.Grid);
            else
                _stasis.Unpin(hull.Grid, StasisPin);

            Last = (hull.Name, hull.CopyMs, hull.CopyTicks, hull.DeleteMs, hull.DeleteTicks, hull.WorstMs);
            Log.Info($"Retired {hull.Name}: copy {hull.CopyMs:0} ms over {hull.CopyTicks} ticks, delete {hull.DeleteMs:0} ms over {hull.DeleteTicks} ticks, longest {hull.WorstMs:0} ms.");
        }
    }

    /// <summary>Marks a kept copy finished with its timings, or with why it failed.</summary>
    private static void FinishKept(RetiringHull hull, string? error)
    {
        if (hull.Copy?.Parked is not { } parked || parked.Done)
            return;

        parked.Error ??= error;
        parked.CopyMs = hull.CopyMs;
        parked.CopyTicks = hull.CopyTicks;
        parked.WorstMs = hull.WorstMs;
        parked.Done = true;
    }

    /// <summary>Serialises entities until the tick's budget is gone. Sets <see cref="HullCopy.Next"/> to -1 when finished.</summary>
    private void CopySlice(RetiringHull hull, HullCopy copy)
    {
        var lifted = LiftCarried(copy);
        Slicing = copy.Parked;
        SlicingThread = Environment.CurrentManagedThreadId;
        try
        {
            copy.Serializer ??= BeginCopy(hull, copy);

            if (copy.Parked != null && FailNextKeptCopy)
            {
                FailNextKeptCopy = false;
                throw new InvalidOperationException("Failure forced by a test.");
            }

            while (copy.Next < copy.Entities.Count)
            {
                if (_timer.Elapsed.TotalMilliseconds >= TickBudgetMs)
                    return;

                copy.Serializer.SerializeEntity(copy.Entities[copy.Next]);
                copy.Next++;
            }

            FinishCopy(hull, copy);
        }
        catch (Exception e)
        {
            // A kept copy is never stripped: whoever asked decides what a failure means.
            if (copy.Parked != null)
            {
                copy.Parked.Error = $"{e.GetType().Name}: {e.Message}";
                copy.Result.TrySetResult(null);
                copy.Next = -1;
                return;
            }

            if (copy.Retried)
            {
                Log.Error($"Failed to copy {hull.Name} for resale: {e}");
                copy.Result.TrySetResult(null);
                copy.Next = -1;
                return;
            }

            // One bad component shouldn't lose the ship: take off what won't save and start over.
            Log.Warning($"Copying {hull.Name} for resale failed, so looking for what won't save: {e.Message}");
            copy.Retried = true;
            copy.Serializer = null;
            copy.Next = 0;
            _shipyard.WGStripUnsavable(hull.Grid, CopyOptions());
        }
        finally
        {
            Slicing = null;
            SlicingThread = 0;
            foreach (var proto in lifted)
            {
                proto.MapSavable = false;
            }
        }
    }

    private EntitySerializer BeginCopy(RetiringHull hull, HullCopy copy)
    {
        // A kept hull may come back, so it is never changed: dead keys that break the save fail the copy instead.
        if (copy.Parked == null)
            _shipyard.WGPruneDeadKeys(hull.Grid);

        // The event can add entities to save, such as a grid's chunk entities.
        copy.Roots = new HashSet<EntityUid> { hull.Grid };
        var maps = new HashSet<MapId> { Transform(hull.Grid).MapID };
        RaiseLocalEvent(new BeforeSerializationEvent(copy.Roots, maps, FileCategory.Grid));

        // A kept copy is read back by this server in the same round, so what it points at off the hull (a station
        // record on a door's reader, a card) is still there. A resale copy outlives all of that.
        var serializer = new EntitySerializer(_dependencies, CopyOptions() with { KeepExternalReferences = copy.Parked != null });
        var entities = new HashSet<EntityUid>();
        foreach (var root in copy.Roots)
        {
            Include(serializer, root, entities);
        }

        copy.Entities = new List<EntityUid>(entities);
        serializer.ReserveYamlIds(entities);
        return serializer;
    }

    private void Include(EntitySerializer serializer, EntityUid uid, HashSet<EntityUid> entities)
    {
        if (!serializer.IsSerializable(uid))
            return;

        entities.Add(uid);
        var children = Transform(uid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            Include(serializer, child, entities);
        }
    }

    private void FinishCopy(RetiringHull hull, HullCopy copy)
    {
        var serializer = copy.Serializer!;

        // Only the parked hull is paused. The copy would otherwise load frozen.
        foreach (var (_, entity) in serializer.EntityData)
        {
            entity.Node.Remove("paused");
        }

        var node = serializer.Write();
        var category = serializer.GetCategory();
        RaiseLocalEvent(new AfterSerializationEvent(copy.Roots, node, category));

        if (category != FileCategory.Grid)
            throw new InvalidOperationException($"Saved as {category}, not as a grid.");

        var name = hull.Name;
        var result = copy.Result;
        Task.Run(() => result.TrySetResult(_shipyard.WGWriteCopy(node, name)));
        copy.Next = -1;
    }

    /// <summary>Deletes the hull's contents until the tick's budget is gone, then the hull. True when it's gone.</summary>
    private bool DeleteSlice(RetiringHull hull)
    {
        if (hull.Children == null)
        {
            hull.Children = new List<EntityUid>();
            var children = Transform(hull.Grid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                hull.Children.Add(child);
            }
        }

        while (hull.NextChild < hull.Children.Count)
        {
            if (_timer.Elapsed.TotalMilliseconds >= TickBudgetMs)
                return false;

            var child = hull.Children[hull.NextChild++];
            if (!Deleted(child))
                Del(child);
        }

        if (!hull.KeepGrid)
            Del(hull.Grid);

        return true;
    }

    /// <summary>Makes carried prototypes savable for one tick. Other saves can run between ticks and must not include them.</summary>
    private List<EntityPrototype> LiftCarried(HullCopy copy)
    {
        var lifted = new List<EntityPrototype>();
        foreach (var uid in copy.Carry)
        {
            if (!TryComp(uid, out MetaDataComponent? meta) || meta.EntityPrototype is not { MapSavable: false } proto)
                continue;

            proto.MapSavable = true;
            lifted.Add(proto);
        }

        return lifted;
    }

    private static SerializationOptions CopyOptions()
    {
        // Ignore: a docked ship points at the station's docks, and none of that comes with it.
        // No orphan error: the hull's map is left out on purpose.
        return SerializationOptions.Default with
        {
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
            LogAutoInclude = null,
            Category = FileCategory.Grid,
            ErrorOnOrphan = false,
        };
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        foreach (var hull in _hulls)
        {
            hull.Copy?.Result.TrySetResult(null);
            FinishKept(hull, "round restart");
        }

        _hulls.Clear();
        _parkMap = null;
    }
}

/// <summary>A copy of a parked hull that is kept: its YAML, written on a worker, and what the copy cost.</summary>
public sealed class WGParkedCopy
{
    /// <summary>The YAML, or null if the copy failed.</summary>
    public Task<string?> Text = default!;

    /// <summary>Why the copy failed, if it did.</summary>
    public string? Error;

    /// <summary>Set once serialising is over; <see cref="Text"/> may still be being written.</summary>
    public bool Done;

    /// <summary>Errors the serializer logged while this copy's own slices ran.</summary>
    public int Errors;

    public double CopyMs;
    public int CopyTicks;
    public double WorstMs;
}
