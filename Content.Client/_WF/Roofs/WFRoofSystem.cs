using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Doors.Components;
using Content.Shared.Ghost;
using Content.Shared.Light.Components;
using Content.Shared.Physics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Client._WF.Roofs;

/// <summary>
/// Works out what the local player is under for <see cref="WFRoofOverlay"/>: aboard a ship its whole roof lifts, in a
/// roofed building the room they're in does. Walls, windows and doors are never roofed over, and a ghost sees no roofs.
/// </summary>
public sealed partial class WFRoofSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;

    /// <summary>Most tiles a room fills out to; past it the rest of a huge roofed space stays covered.</summary>
    private const int RoomCap = 4096;

    /// <summary>How often the room is filled again while standing still, so a door opening shows the next room.</summary>
    private const float Refresh = 0.25f;

    /// <summary>Collision layers that together make a structure full height, such as a diagonal wall.</summary>
    private const int FullHeight = (int) (CollisionGroup.HighImpassable | CollisionGroup.MidImpassable | CollisionGroup.LowImpassable);

    /// <summary>Whether roofs are drawn at all for the local player.</summary>
    public bool Enabled => SeesRoofs(_player.LocalEntity);

    /// <summary>The grid the player stands on, whose revealed tiles lift.</summary>
    public EntityUid? Grid { get; private set; }

    /// <summary>Aboard a vessel: its whole roof lifts.</summary>
    public bool WholeGrid { get; private set; }

    /// <summary>The roofed tiles of <see cref="Grid"/> that lift.</summary>
    public readonly HashSet<Vector2i> Revealed = new();

    /// <summary>Per grid, whether each tile asked about holds a wall, window or door.</summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, bool>> _structures = new();

    private Vector2i? _lastTile;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        InitializeVessels();
        _overlays.AddOverlay(new WFRoofOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WFRoofOverlay>();
    }

    /// <summary>Whether someone sees roofs: anyone with a body. A ghost, an observer or nobody at all sees none.</summary>
    public bool SeesRoofs(EntityUid? viewer)
    {
        return viewer is { } body && !TerminatingOrDeleted(body)
               && !HasComp<GhostComponent>(body) && !HasComp<SpectralComponent>(body);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _accumulator += frameTime;
        UpdateVessels(frameTime);

        var player = _player.LocalEntity;
        if (player is not { } body || !SeesRoofs(body) || Transform(body).GridUid is not { } grid
            || !TryComp(grid, out MapGridComponent? gridComp))
        {
            Clear();
            return;
        }

        if (HasComp<ImplicitRoofComponent>(grid))
        {
            Clear();
            Grid = grid;
            WholeGrid = true;
            return;
        }

        var tile = _maps.TileIndicesFor(grid, gridComp, Transform(body).Coordinates);
        if (Grid == grid && !WholeGrid && _lastTile == tile && _accumulator < Refresh)
            return;
        _accumulator = 0f;
        _lastTile = tile;
        Grid = grid;
        WholeGrid = false;

        if (!TryComp(grid, out RoofComponent? roof))
        {
            Revealed.Clear();
            return;
        }
        WFRoofReveal.Fill(tile, index => IsRoofed(roof, index), index => IsBarrier(grid, gridComp, index), RoomCap, Revealed);
    }

    private void Clear()
    {
        Grid = null;
        WholeGrid = false;
        _lastTile = null;
        Revealed.Clear();
    }

    /// <summary>Whether a roof is drawn over a tile of a grid for the local player, as <see cref="WFRoofOverlay"/> draws it.</summary>
    public bool Covers(Entity<MapGridComponent> grid, Vector2i index)
    {
        var implicitRoof = HasComp<ImplicitRoofComponent>(grid);
        TryComp(grid, out RoofComponent? roof);
        if (!Enabled || !implicitRoof && (roof == null || Underground(grid)))
            return false;
        var own = Grid == grid.Owner;
        if (implicitRoof && own && WholeGrid)
            return false;
        return Covers(grid, roof, implicitRoof ? Enclosed(grid) : null, own, index);
    }

    /// <summary>
    /// Whether a roof is drawn over a tile: roofed (by the grid's roof bits, or as part of a vessel's
    /// <paramref name="enclosed"/> rooms), not lifted for the local player and not holding a wall, window or door.
    /// </summary>
    public bool Covers(Entity<MapGridComponent> grid, RoofComponent? roof, HashSet<Vector2i>? enclosed, bool own, Vector2i index)
    {
        var roofed = enclosed?.Contains(index) ?? roof != null && IsRoofed(roof, index);
        if (!roofed)
            return false;
        return WFRoofReveal.Covers(true, IsStructure(grid, grid.Comp, index), own && Revealed.Contains(index));
    }

    /// <summary>Whether a tile's roof bit is set, without the per-tile entity lookup the roof system falls back on.</summary>
    public static bool IsRoofed(RoofComponent roof, Vector2i index)
    {
        var chunk = SharedMapSystem.GetChunkIndices(index, RoofComponent.ChunkSize);
        if (!roof.Data.TryGetValue(chunk, out var mask))
            return false;
        var relative = SharedMapSystem.GetChunkRelative(index, RoofComponent.ChunkSize);
        var bit = 1UL << (relative.X + relative.Y * RoofComponent.ChunkSize);
        return (mask & bit) != 0;
    }

    /// <summary>
    /// A wall, window, door (open or shut) or other full-height structure anchored on the tile. Remembered until
    /// something is anchored or unanchored there or the tile changes.
    /// </summary>
    public bool IsStructure(EntityUid grid, MapGridComponent gridComp, Vector2i index)
    {
        if (!_structures.TryGetValue(grid, out var tiles))
            _structures[grid] = tiles = new Dictionary<Vector2i, bool>();
        if (tiles.TryGetValue(index, out var known))
            return known;

        var structure = false;
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, index);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<DoorComponent>(uid) || TryComp(uid, out PhysicsComponent? body) && WallLike(body))
            {
                structure = true;
                break;
            }
        }
        tiles[index] = structure;
        return structure;
    }

    /// <summary>A structure nobody walks through: a wall, a window or a door that isn't open.</summary>
    public bool IsBarrier(EntityUid grid, MapGridComponent gridComp, Vector2i index)
    {
        if (!IsStructure(grid, gridComp, index))
            return false;
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, index);
        while (anchored.MoveNext(out var uid))
        {
            if (TryComp(uid, out DoorComponent? door) && door.State is DoorState.Open or DoorState.Opening)
                return false;
        }
        return true;
    }

    /// <summary>A hard body on the wall or window layer, or one that blocks at every height.</summary>
    private static bool WallLike(PhysicsComponent body)
    {
        var layer = body.CollisionLayer;
        return body.Hard && ((layer & (int) CollisionGroup.Impassable) != 0 || (layer & FullHeight) == FullHeight);
    }

    /// <summary>A layer under another world layer, such as a cavern: roofed all over by the ground above, not by buildings.</summary>
    public bool Underground(EntityUid grid)
    {
        return HasComp<MapComponent>(grid) && _zLevels.TryMapUp(grid, out var above)
               && HasComp<MapGridComponent>(above.Owner);
    }

    /// <summary>Forgets whether a tile holds a structure, so it's looked at again.</summary>
    private void ForgetStructure(EntityUid grid, Vector2i index)
    {
        if (_structures.TryGetValue(grid, out var tiles))
            tiles.Remove(index);
    }
}
