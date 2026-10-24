using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.Roofs;

/// <summary>
/// Where a vessel or station is roofed: its enclosed rooms and their walls, worked out again whenever a tile changes
/// or something is anchored or unanchored on it, so building, deconstructing and breaches show at once.
/// </summary>
public sealed partial class WFRoofSystem
{
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    /// <summary>How often grids whose roofs were never asked for again are forgotten.</summary>
    private const float SweepInterval = 10f;

    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _enclosed = new();
    private readonly HashSet<EntityUid> _stale = new();
    private float _sweep;

    private void InitializeVessels()
    {
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<AnchorStateChangedEvent>(OnAnchorChanged);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        _stale.Add(args.Entity);
    }

    private void OnAnchorChanged(ref AnchorStateChangedEvent args)
    {
        if (args.Transform.GridUid is { } grid)
            _stale.Add(grid);
    }

    private void UpdateVessels(float frameTime)
    {
        _sweep += frameTime;
        if (_sweep < SweepInterval)
            return;
        _sweep = 0f;
        foreach (var grid in _enclosed.Keys)
        {
            if (TerminatingOrDeleted(grid))
                _stale.Add(grid);
        }
        foreach (var grid in _stale)
        {
            if (TerminatingOrDeleted(grid))
                _enclosed.Remove(grid);
        }
    }

    /// <summary>The roofed tiles of a vessel or station grid, worked out afresh if it has changed since last asked.</summary>
    public HashSet<Vector2i> Enclosed(Entity<MapGridComponent> grid)
    {
        if (_enclosed.TryGetValue(grid, out var roofed) && !_stale.Contains(grid))
            return roofed;

        roofed ??= new HashSet<Vector2i>();
        _enclosed[grid] = roofed;
        _stale.Remove(grid);

        var floor = new HashSet<Vector2i>();
        var tiles = _maps.GetAllTilesEnumerator(grid, grid.Comp);
        while (tiles.MoveNext(out var tile))
            floor.Add(tile.Value.GridIndices);

        WFRoofReveal.Enclosed(floor,
            index => _maps.TryGetTileRef(grid, grid.Comp, index, out var tile)
                     && _tileDefs[tile.Tile.TypeId] is ContentTileDefinition { MapAtmosphere: true },
            index => Encloses(grid, grid.Comp, index),
            roofed);
        return roofed;
    }

    /// <summary>A wall, window or door, open or shut: what bounds a room.</summary>
    private bool Encloses(EntityUid grid, MapGridComponent gridComp, Vector2i index)
    {
        if (IsSolid(grid, gridComp, index))
            return true;
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, index);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<DoorComponent>(uid))
                return true;
        }
        return false;
    }
}
