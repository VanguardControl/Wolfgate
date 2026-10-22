using Content.Shared.Ghost;
using Content.Shared.Light.Components;
using Content.Shared.Physics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Client._WF.Roofs;

/// <summary>
/// Works out what the local player is under for <see cref="WFRoofOverlay"/>: aboard a ship its whole roof lifts, in a
/// roofed building the room they're in does. A ghost sees no roofs at all.
/// </summary>
public sealed partial class WFRoofSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    /// <summary>Most tiles a room fills out to; past it the rest of a huge roofed space stays covered.</summary>
    private const int RoomCap = 4096;

    /// <summary>How often the room is filled again while standing still, so a door opening shows the next room.</summary>
    private const float Refresh = 0.25f;

    /// <summary>Whether roofs are drawn at all for the local player.</summary>
    public bool Enabled { get; private set; }

    /// <summary>The grid the player stands on, whose revealed tiles lift.</summary>
    public EntityUid? Grid { get; private set; }

    /// <summary>Aboard a vessel: its whole roof lifts.</summary>
    public bool WholeGrid { get; private set; }

    /// <summary>The roofed tiles of <see cref="Grid"/> that lift.</summary>
    public readonly HashSet<Vector2i> Revealed = new();

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

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _accumulator += frameTime;
        UpdateVessels(frameTime);

        var player = _player.LocalEntity;
        Enabled = player != null && !HasComp<GhostComponent>(player);
        if (player is not { } body || !Enabled || Transform(body).GridUid is not { } grid
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
        WFRoofReveal.Fill(tile, index => IsRoofed(roof, index), index => IsSolid(grid, gridComp, index), RoomCap, Revealed);
    }

    private void Clear()
    {
        Grid = null;
        WholeGrid = false;
        _lastTile = null;
        Revealed.Clear();
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

    /// <summary>A wall, window or shut door: something anchored there that nobody walks through.</summary>
    public bool IsSolid(EntityUid grid, MapGridComponent gridComp, Vector2i index)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, index);
        while (anchored.MoveNext(out var uid))
        {
            if (TryComp(uid, out PhysicsComponent? body) && body.CanCollide && body.Hard
                && (body.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
                return true;
        }
        return false;
    }
}
