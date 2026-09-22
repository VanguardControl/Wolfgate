using System.IO;
using System.Linq;
using System.Numerics;
using Content.Shared.GameTicking;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Placement;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Mapping;

/// <summary>What a single recorded action did.</summary>
public enum MappingUndoKind : byte
{
    Created,
    Erased,
    Tile,
}

/// <summary>
/// One entity across restores. Restoring gives the entity a new uid, so every stack entry that talks about
/// it holds this handle instead of a raw uid.
/// </summary>
public sealed class MappingUndoHandle
{
    public EntityUid Uid;

    public MappingUndoHandle(EntityUid uid)
    {
        Uid = uid;
    }
}

/// <summary>One entity that was placed or erased, and everything needed to put it back.</summary>
public sealed class MappingUndoEntity
{
    public MappingUndoHandle Handle = default!;

    /// <summary>What the entity hung off: the grid, or the map for unparented things.</summary>
    public MappingUndoHandle Parent = default!;

    /// <summary>True when the mapper placed it (undo deletes it), false when the mapper erased it (undo restores it).</summary>
    public bool Created;

    public string Label = string.Empty;

    public Vector2 LocalPosition;

    public Angle LocalRotation;

    public bool Anchored;

    /// <summary>Serialised state while the entity is gone; null while it is alive.</summary>
    public string? Data;
}

/// <summary>One tile the mapper changed.</summary>
public sealed class MappingUndoTile
{
    public MappingUndoHandle Grid = default!;

    public Vector2i Indices;

    public Tile Old;

    public Tile New;
}

/// <summary>One undo step: everything the mapper did in a single burst.</summary>
public sealed class MappingUndoStep
{
    public EntityUid MapUid;

    public MappingUndoKind Kind;

    public GameTick Tick;

    public TimeSpan Time;

    /// <summary>The burst hit the per-step capture cap and the rest of it cannot be put back.</summary>
    public bool Truncated;

    public readonly List<MappingUndoEntity> Entities = new();

    public readonly List<MappingUndoTile> Tiles = new();

    public int Count => Entities.Count + Tiles.Count;
}

/// <summary>What one mapundo/mapredo call did, for the shell and the popup.</summary>
public sealed class MappingUndoReport
{
    public readonly List<string> Lines = new();

    /// <summary>Something in a step was already gone and was left alone.</summary>
    public bool Skipped;

    /// <summary>At least one entity came back with a new uid.</summary>
    public bool Restored;

    /// <summary>A step was recorded with more entities than the capture cap allows.</summary>
    public bool Truncated;

    public bool Any => Lines.Count > 0;
}

/// <summary>
/// Per-mapper undo and redo for in-game mapping. Records placements, erases and tile changes made through the
/// engine's placement manager, but only on maps that have not been map-initialised, so ordinary rounds and
/// admin spawning are never touched.
/// </summary>
public sealed partial class MappingUndoSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapMan = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How many steps a mapper keeps.</summary>
    public const int MaxSteps = 200;

    /// <summary>Entities captured in one step before the rest of the burst is given up on.</summary>
    public const int MaxStepCapture = 500;

    /// <summary>Consecutive actions closer together than this join the same step.</summary>
    public static readonly TimeSpan GroupWindow = TimeSpan.FromSeconds(0.25);

    private const string Source = "mapping undo";

    /// <summary>
    /// External references are dropped rather than chased: an erased wall must not drag half the grid into its
    /// snapshot, and an unreachable reference would otherwise be logged as an error on every capture.
    /// </summary>
    private static readonly SerializationOptions SaveOptions = SerializationOptions.Default with
    {
        MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
        ErrorOnOrphan = false,
        LogAutoInclude = null,
        ExpectPreInit = false,
    };

    /// <summary>The dropped references above come back as "invalid", which is expected here and not worth logging.</summary>
    private static readonly DeserializationOptions LoadOptions = DeserializationOptions.Default with
    {
        LogInvalidEntities = false,
        LogOrphanedGrids = false,
    };

    private readonly Dictionary<NetUserId, MappingUndoState> _states = new();

    /// <summary>Tile changes seen this tick, waiting for the placement event that says who made them.</summary>
    private readonly List<PendingTile> _pendingTiles = new();

    private GameTick _pendingTick;

    /// <summary>Set while a step is being applied, so our own tile writes are not recorded again.</summary>
    private bool _applying;

    private EntityQuery<MapComponent> _mapQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<MetaDataComponent> _metaQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _mapQuery = GetEntityQuery<MapComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _metaQuery = GetEntityQuery<MetaDataComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<PlacementEntityEvent>(OnPlacementEntity);
        SubscribeLocalEvent<PlacementTileEvent>(OnPlacementTile);
        SubscribeLocalEvent<MapGridComponent, TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<MapComponent, EntityTerminatingEvent>(OnMapTerminating);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    #region Recording

    private void OnPlacementEntity(PlacementEntityEvent ev)
    {
        // Ordinary rounds and admin spawning leave here: their maps have been map-initialised.
        if (_applying
            || ev.PlacerNetUserId is not { } user
            || !_xformQuery.TryGetComponent(ev.EditedEntity, out var xform)
            || !IsMappingMap(xform.MapUid, out var mapUid))
        {
            return;
        }

        switch (ev.PlacementEventAction)
        {
            case PlacementEventAction.Create:
                RecordCreated(user, mapUid, (ev.EditedEntity, xform));
                break;
            case PlacementEventAction.Erase:
                RecordErased(user, mapUid, (ev.EditedEntity, xform));
                break;
        }
    }

    /// <summary>Records a placement. The entity is alive; its state is only captured if the mapper undoes it.</summary>
    public void RecordCreated(NetUserId user, EntityUid mapUid, Entity<TransformComponent> entity)
    {
        var state = GetState(user);
        var step = GetStep(state, mapUid, MappingUndoKind.Created);
        if (step.Entities.Count >= MaxStepCapture)
        {
            step.Truncated = true;
            return;
        }

        step.Entities.Add(new MappingUndoEntity
        {
            Handle = GetHandle(state, entity.Owner),
            Parent = GetHandle(state, entity.Comp.ParentUid),
            Created = true,
            Label = Label(entity.Owner),
        });
    }

    /// <summary>
    /// Records an erase. The engine raises this before it deletes, so the entity and everything it contains is
    /// still there to be snapshotted.
    /// </summary>
    public void RecordErased(NetUserId user, EntityUid mapUid, Entity<TransformComponent> entity)
    {
        var state = GetState(user);
        var step = GetStep(state, mapUid, MappingUndoKind.Erased);
        if (step.Entities.Count >= MaxStepCapture)
        {
            step.Truncated = true;
            return;
        }

        var action = new MappingUndoEntity
        {
            Handle = GetHandle(state, entity.Owner),
            Parent = GetHandle(state, entity.Comp.ParentUid),
            Created = false,
            Label = Label(entity.Owner),
        };

        if (!TryCapture(action))
        {
            step.Truncated = true;
            return;
        }

        step.Entities.Add(action);
    }

    /// <summary>
    /// Stashes the old tile for every change on a mapping map. The placement event that follows says which of
    /// them a mapper made; anything left over is dropped on the next tick.
    /// </summary>
    private void OnTileChanged(Entity<MapGridComponent> grid, ref TileChangedEvent args)
    {
        if (_applying
            || !_xformQuery.TryGetComponent(grid.Owner, out var xform)
            || !IsMappingMap(xform.MapUid, out _))
        {
            return;
        }

        if (_pendingTick != _timing.CurTick)
        {
            _pendingTiles.Clear();
            _pendingTick = _timing.CurTick;
        }

        foreach (var change in args.Changes)
        {
            _pendingTiles.Add(new PendingTile(grid.Owner, xform.MapUid!.Value, change.GridIndices, change.OldTile, change.NewTile));
        }

        // A mapper cannot outrun this; anything past it came from somewhere else.
        if (_pendingTiles.Count > MaxStepCapture)
            _pendingTiles.RemoveRange(0, _pendingTiles.Count - MaxStepCapture);
    }

    /// <summary>
    /// Claims the stashed change for the tile this placement was about. The placement event only carries the
    /// new tile, so the old one has to come from the engine's own tile event, raised a moment earlier in the
    /// same call.
    /// </summary>
    private void OnPlacementTile(PlacementTileEvent ev)
    {
        if (_applying || _pendingTiles.Count == 0)
            return;

        if (_pendingTick != _timing.CurTick || ev.PlacerNetUserId is not { } user)
        {
            _pendingTiles.Clear();
            return;
        }

        if (!TryResolveTile(ev.Coordinates, out var gridUid, out var indices))
            return;

        // Newest first: if the mapper painted the same tile twice this tick, the last change is this one.
        for (var i = _pendingTiles.Count - 1; i >= 0; i--)
        {
            var pending = _pendingTiles[i];
            if (pending.Grid != gridUid || pending.Indices != indices)
                continue;

            _pendingTiles.RemoveAt(i);

            var state = GetState(user);
            var step = GetStep(state, pending.MapUid, MappingUndoKind.Tile);
            if (step.Tiles.Count >= MaxStepCapture)
            {
                step.Truncated = true;
                return;
            }

            step.Tiles.Add(new MappingUndoTile
            {
                Grid = GetHandle(state, pending.Grid),
                Indices = pending.Indices,
                Old = pending.Old,
                New = pending.New,
            });

            return;
        }
    }

    /// <summary>The grid and tile a placement's coordinates land on, including a grid the placement just made.</summary>
    private bool TryResolveTile(EntityCoordinates coords, out EntityUid gridUid, out Vector2i indices)
    {
        gridUid = default;
        indices = default;

        if (!coords.IsValid(EntityManager))
            return false;

        if (_gridQuery.TryGetComponent(coords.EntityId, out var grid))
        {
            gridUid = coords.EntityId;
            indices = _map.TileIndicesFor((gridUid, grid), coords);
            return true;
        }

        var mapCoords = _transform.ToMapCoordinates(coords);
        if (!_mapMan.TryFindGridAt(mapCoords, out var found, out var foundGrid))
            return false;

        gridUid = found;
        indices = _map.TileIndicesFor((found, foundGrid), mapCoords);
        return true;
    }

    /// <summary>The step a new action joins: the last one when it is part of the same burst, otherwise a fresh one.</summary>
    private MappingUndoStep GetStep(MappingUndoState state, EntityUid mapUid, MappingUndoKind kind)
    {
        ClearRedo(state);

        var now = _timing.CurTime;
        var tick = _timing.CurTick;

        if (state.Undo.Count > 0)
        {
            var last = state.Undo[^1];

            // Same tick covers rect erase, line and grid placement modes; the window covers a dragged line.
            if (last.MapUid == mapUid
                && (last.Tick == tick || (last.Kind == kind && now - last.Time <= GroupWindow)))
            {
                last.Tick = tick;
                last.Time = now;
                return last;
            }
        }

        var step = new MappingUndoStep
        {
            MapUid = mapUid,
            Kind = kind,
            Tick = tick,
            Time = now,
        };

        state.Undo.Add(step);

        if (state.Undo.Count > MaxSteps)
        {
            state.Undo.RemoveRange(0, state.Undo.Count - MaxSteps);
            PruneHandles(state);
        }

        return step;
    }

    private void ClearRedo(MappingUndoState state)
    {
        if (state.Redo.Count == 0)
            return;

        state.Redo.Clear();
        PruneHandles(state);
    }

    #endregion

    #region Applying

    /// <summary>Undoes or redoes up to <paramref name="count"/> of a mapper's steps.</summary>
    public MappingUndoReport Apply(NetUserId user, int count, bool redo)
    {
        var report = new MappingUndoReport();
        if (!_states.TryGetValue(user, out var state))
            return report;

        var from = redo ? state.Redo : state.Undo;
        var to = redo ? state.Undo : state.Redo;

        _applying = true;
        try
        {
            for (var i = 0; i < count && from.Count > 0; i++)
            {
                var step = from[^1];
                from.RemoveAt(from.Count - 1);

                ApplyStep(state, step, redo, report);
                to.Add(step);

                report.Lines.Add(Describe(step));
                if (step.Truncated)
                    report.Truncated = true;
            }
        }
        finally
        {
            _applying = false;
        }

        return report;
    }

    private void ApplyStep(MappingUndoState state, MappingUndoStep step, bool redo, MappingUndoReport report)
    {
        foreach (var tile in step.Tiles)
        {
            if (!TrySetTile(tile, redo ? tile.New : tile.Old))
                report.Skipped = true;
        }

        // Undo walks backwards so a container is restored before the things that were inside it; redo replays
        // the burst in the order the mapper made it.
        if (redo)
        {
            for (var i = 0; i < step.Entities.Count; i++)
            {
                ApplyEntity(state, step.Entities[i], step.Entities[i].Created, report);
            }
        }
        else
        {
            for (var i = step.Entities.Count - 1; i >= 0; i--)
            {
                ApplyEntity(state, step.Entities[i], !step.Entities[i].Created, report);
            }
        }
    }

    /// <summary><paramref name="restore"/> puts the entity back, otherwise it is captured and deleted.</summary>
    private void ApplyEntity(MappingUndoState state, MappingUndoEntity action, bool restore, MappingUndoReport report)
    {
        if (restore)
        {
            if (TryRestore(state, action))
                report.Restored = true;
            else
                report.Skipped = true;

            return;
        }

        if (!TryResolve(action.Handle, out var uid))
        {
            report.Skipped = true;
            return;
        }

        if (!TryCapture(action))
        {
            report.Skipped = true;
            return;
        }

        Del(uid);
    }

    /// <summary>Snapshots an entity and where it sits, so it can be put back later.</summary>
    private bool TryCapture(MappingUndoEntity action)
    {
        if (!TryResolve(action.Handle, out var uid) || !_xformQuery.TryGetComponent(uid, out var xform))
            return false;

        // Never snapshot somebody's body, even on a mapping map.
        if (HasComp<ActorComponent>(uid))
            return false;

        action.LocalPosition = xform.LocalPosition;
        action.LocalRotation = xform.LocalRotation;
        action.Anchored = xform.Anchored;
        action.Parent.Uid = xform.ParentUid;
        action.Label = Label(uid);

        using var writer = new StringWriter();

        // Mobs and other `save: false` prototypes are kept out of map files, and the serializer drops them
        // without asking. A mapper who erased one still expects it back, so the flag is lifted for the length
        // of this one call and put back straight after.
        var lifted = new List<EntityPrototype>();
        CollectUnsavable(uid, lifted);
        foreach (var proto in lifted)
        {
            proto.MapSavable = true;
        }

        bool saved;
        try
        {
            saved = _loader.TrySaveEntity(uid, writer, SaveOptions);
        }
        finally
        {
            foreach (var proto in lifted)
            {
                proto.MapSavable = false;
            }
        }

        if (!saved)
            return false;

        action.Data = writer.ToString();
        return true;
    }

    /// <summary>Every unsavable prototype in an entity's tree, so the whole thing can be snapshotted in one go.</summary>
    private void CollectUnsavable(EntityUid uid, List<EntityPrototype> protos)
    {
        if (!_metaQuery.TryGetComponent(uid, out var meta) || !_xformQuery.TryGetComponent(uid, out var xform))
            return;

        if (meta.EntityPrototype is { MapSavable: false } proto && !protos.Contains(proto))
            protos.Add(proto);

        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            CollectUnsavable(child, protos);
        }
    }

    /// <summary>
    /// Loads a snapshot back. The entity arrives orphaned in null-space, which drops its anchoring, so it is
    /// re-parented to the grid at its old local position and rotation and anchored again by hand.
    /// </summary>
    private bool TryRestore(MappingUndoState state, MappingUndoEntity action)
    {
        if (action.Data is not { } data || !TryResolve(action.Parent, out var parent))
            return false;

        Entity<TransformComponent>? loaded;
        try
        {
            using var reader = new StringReader(data);
            if (!_loader.TryLoadEntity(reader, Source, out loaded, LoadOptions))
                return false;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to restore a mapping undo entity: {e}");
            return false;
        }

        var uid = loaded.Value.Owner;
        var xform = loaded.Value.Comp;

        _transform.SetCoordinates((uid, xform, _metaQuery.GetComponent(uid)),
            new EntityCoordinates(parent, action.LocalPosition),
            action.LocalRotation,
            unanchor: false);

        if (action.Anchored)
            _transform.AnchorEntity((uid, xform));

        Rebind(state, action.Handle, uid);
        action.Data = null;
        return true;
    }

    private bool TrySetTile(MappingUndoTile tile, Tile value)
    {
        if (!TryResolve(tile.Grid, out var gridUid) || !_gridQuery.TryGetComponent(gridUid, out var grid))
            return false;

        _map.SetTile((gridUid, grid), tile.Indices, value);
        return true;
    }

    #endregion

    #region Description

    private string Describe(MappingUndoStep step)
    {
        var parts = new List<string>();

        AppendEntities(parts, step, true, "mapping-undo-part-placed");
        AppendEntities(parts, step, false, "mapping-undo-part-erased");

        if (step.Tiles.Count > 0)
        {
            var name = _tileDefs.TryGetDefinition(step.Tiles[^1].New.TypeId, out var def)
                ? def.Name
                : step.Tiles[^1].New.TypeId.ToString();

            parts.Add(Loc.GetString("mapping-undo-part-tiles", ("what", Quantity(step.Tiles.Count, name))));
        }

        return parts.Count == 0
            ? Loc.GetString("mapping-undo-part-nothing")
            : string.Join(", ", parts);
    }

    private void AppendEntities(List<string> parts, MappingUndoStep step, bool created, string key)
    {
        var groups = new Dictionary<string, int>();
        foreach (var action in step.Entities)
        {
            if (action.Created != created)
                continue;

            groups[action.Label] = groups.GetValueOrDefault(action.Label) + 1;
        }

        if (groups.Count == 0)
            return;

        var ordered = groups.OrderByDescending(pair => pair.Value).ToList();
        var shown = ordered.Take(2).Select(pair => Quantity(pair.Value, pair.Key)).ToList();
        if (ordered.Count > shown.Count)
            shown.Add(Loc.GetString("mapping-undo-part-more", ("count", ordered.Count - shown.Count)));

        parts.Add(Loc.GetString(key, ("what", string.Join(" + ", shown))));
    }

    private static string Quantity(int count, string what)
    {
        return count == 1 ? what : $"{count}× {what}";
    }

    /// <summary>The prototype id if it has one, otherwise the name, so a step reads like the mapper's palette.</summary>
    private string Label(EntityUid uid)
    {
        if (!_metaQuery.TryGetComponent(uid, out var meta))
            return "entity";

        return meta.EntityPrototype?.ID ?? meta.EntityName;
    }

    #endregion

    #region State

    /// <summary>Whether a map is one being mapped on: created by the mapping command and never map-initialised.</summary>
    private bool IsMappingMap(EntityUid? candidate, out EntityUid mapUid)
    {
        mapUid = default;
        if (candidate is not { } uid || !_mapQuery.TryGetComponent(uid, out var map) || map.MapInitialized)
            return false;

        mapUid = uid;
        return true;
    }

    private MappingUndoState GetState(NetUserId user)
    {
        if (_states.TryGetValue(user, out var state))
            return state;

        state = new MappingUndoState();
        _states[user] = state;
        return state;
    }

    private MappingUndoHandle GetHandle(MappingUndoState state, EntityUid uid)
    {
        if (state.Handles.TryGetValue(uid, out var handle))
            return handle;

        handle = new MappingUndoHandle(uid);
        state.Handles[uid] = handle;
        return handle;
    }

    private bool TryResolve(MappingUndoHandle handle, out EntityUid uid)
    {
        uid = handle.Uid;
        return uid.IsValid() && !TerminatingOrDeleted(uid);
    }

    private void Rebind(MappingUndoState state, MappingUndoHandle handle, EntityUid uid)
    {
        state.Handles.Remove(handle.Uid);
        handle.Uid = uid;
        state.Handles[uid] = handle;
    }

    /// <summary>Drops handles that no step talks about any more.</summary>
    private static void PruneHandles(MappingUndoState state)
    {
        state.Handles.Clear();

        foreach (var step in state.Undo.Concat(state.Redo))
        {
            foreach (var action in step.Entities)
            {
                state.Handles[action.Handle.Uid] = action.Handle;
                state.Handles[action.Parent.Uid] = action.Parent;
            }

            foreach (var tile in step.Tiles)
            {
                state.Handles[tile.Grid.Uid] = tile.Grid;
            }
        }
    }

    /// <summary>How many steps a mapper can still undo and redo.</summary>
    public (int Undo, int Redo) GetCounts(NetUserId user)
    {
        return _states.TryGetValue(user, out var state)
            ? (state.Undo.Count, state.Redo.Count)
            : (0, 0);
    }

    /// <summary>A mapper's steps, oldest first. For tests and tooling.</summary>
    public IReadOnlyList<MappingUndoStep> GetSteps(NetUserId user, bool redo)
    {
        if (!_states.TryGetValue(user, out var state))
            return Array.Empty<MappingUndoStep>();

        return redo ? state.Redo : state.Undo;
    }

    /// <summary>Forgets everything a mapper did on a map. The map going away takes its history with it.</summary>
    public void DropMap(EntityUid mapUid)
    {
        foreach (var state in _states.Values)
        {
            var dropped = state.Undo.RemoveAll(step => step.MapUid == mapUid);
            dropped += state.Redo.RemoveAll(step => step.MapUid == mapUid);

            if (dropped > 0)
                PruneHandles(state);
        }
    }

    private void OnMapTerminating(Entity<MapComponent> map, ref EntityTerminatingEvent args)
    {
        if (_states.Count == 0)
            return;

        DropMap(map.Owner);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _states.Clear();
        _pendingTiles.Clear();
    }

    private sealed class MappingUndoState
    {
        public readonly List<MappingUndoStep> Undo = new();

        public readonly List<MappingUndoStep> Redo = new();

        public readonly Dictionary<EntityUid, MappingUndoHandle> Handles = new();
    }

    private readonly record struct PendingTile(EntityUid Grid, EntityUid MapUid, Vector2i Indices, Tile Old, Tile New);

    #endregion
}
