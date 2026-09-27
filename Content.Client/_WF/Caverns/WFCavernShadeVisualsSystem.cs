using Content.Shared._WF.Caverns;
using Robust.Client.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.Caverns;

/// <summary>Draws each pit tile as four corners, with a rim and shaft wall wherever the pit meets solid ground.</summary>
// Each corner state comes in several drawings; a stable hash of the tile picks one, so long edges don't repeat.
public sealed partial class WFCavernShadeVisualsSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    /// <summary>How many drawings of each corner state the pit RSIs hold.</summary>
    public const int Variants = 3;

    /// <summary>Each corner's state prefix, the neighbour above or below it and the neighbour beside it.</summary>
    private static readonly (string Name, Vector2i Vertical, Vector2i Side)[] Corners =
    {
        ("ne", new Vector2i(0, 1), new Vector2i(1, 0)),
        ("nw", new Vector2i(0, 1), new Vector2i(-1, 0)),
        ("se", new Vector2i(0, -1), new Vector2i(1, 0)),
        ("sw", new Vector2i(0, -1), new Vector2i(-1, 0)),
    };

    // Shades are unanchored, so icon smoothing can't see them: the pits are tracked here by grid tile.
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, EntityUid>> _pits = new();
    private readonly Dictionary<EntityUid, (EntityUid Grid, Vector2i Tile)> _placed = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCavernShaftComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFCavernShaftComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<WFCavernShaftComponent> ent, ref ComponentStartup args)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var tile = _map.CoordinatesToTile(grid, gridComp, xform.Coordinates);
        if (!_pits.TryGetValue(grid, out var pits))
            _pits[grid] = pits = new Dictionary<Vector2i, EntityUid>();

        pits[tile] = ent.Owner;
        _placed[ent.Owner] = (grid, tile);
        RefreshAround(grid, tile);
    }

    private void OnShutdown(Entity<WFCavernShaftComponent> ent, ref ComponentShutdown args)
    {
        if (!_placed.Remove(ent.Owner, out var placed) || !_pits.TryGetValue(placed.Grid, out var pits))
            return;

        if (pits.TryGetValue(placed.Tile, out var there) && there == ent.Owner)
            pits.Remove(placed.Tile);

        if (pits.Count == 0)
            _pits.Remove(placed.Grid);
        else
            RefreshAround(placed.Grid, placed.Tile);
    }

    private void RefreshAround(EntityUid grid, Vector2i centre)
    {
        if (!_pits.TryGetValue(grid, out var pits))
            return;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var tile = centre + new Vector2i(dx, dy);
                if (pits.TryGetValue(tile, out var pit))
                    Refresh(pit, tile, pits);
            }
        }
    }

    private void Refresh(EntityUid uid, Vector2i tile, Dictionary<Vector2i, EntityUid> pits)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        Entity<SpriteComponent?> ent = (uid, sprite);

        // The prototype's whole-tile layer is only the placement icon.
        _sprite.LayerSetVisible(ent, 0, false);

        for (var i = 0; i < Corners.Length; i++)
        {
            var (name, vertical, side) = Corners[i];
            var mask = (pits.ContainsKey(tile + vertical) ? 1 : 0)
                       | (pits.ContainsKey(tile + side) ? 2 : 0)
                       | (pits.ContainsKey(tile + vertical + side) ? 4 : 0);

            var layer = _sprite.LayerMapReserve(ent, $"wf-pit-{name}");
            _sprite.LayerSetRsiState(ent, layer, $"{name}{mask}_{Variant(tile, i)}");
        }
    }

    /// <summary>Which drawing of a corner state a tile uses: plain arithmetic, so it never changes between sessions.</summary>
    public static int Variant(Vector2i tile, int corner)
    {
        var hash = unchecked((uint) (tile.X * 374761393 + tile.Y * 668265263 + corner * 1274126177));
        hash = unchecked((hash ^ (hash >> 13)) * 1274126177u);
        hash ^= hash >> 16;
        return (int) (hash % Variants);
    }
}
