using System.Numerics;
using Content.Server.Parallax;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    [Dependency] private BiomeSystem _wfLandingBiome = default!;
    public const float WFLandingClearance = 1f;

    /// <summary>Clear a tile of breathing room around an impacting hull, without touching other grids or occupants.</summary>
    public void WfClearLandingObstacles(EntityUid hull, bool reportImpacts = false)
    {
        if (HasComp<WFPlanetChunkComponent>(hull) || !WfHasSkidGround(hull)
            || !TryComp<MapGridComponent>(hull, out var hullGrid)
            || Transform(hull).MapUid is not { } ground
            || !TryComp<MapGridComponent>(ground, out var groundGrid))
            return;

        var bounds = _transform.GetWorldMatrix(hull).TransformBox(hullGrid.LocalAABB).Enlarged(WFLandingClearance + 1f);
        var obstacles = new HashSet<EntityUid>();
        foreach (var obstacle in _map.GetAnchoredEntities(ground, groundGrid, new Box2Rotated(bounds, Angle.Zero)))
        {
            if (TerminatingOrDeleted(obstacle) || Transform(obstacle).GridUid != ground
                || !TryComp<FixturesComponent>(obstacle, out var fixtures))
                continue;
            var blocking = false;
            foreach (var fixture in fixtures.Fixtures.Values)
                blocking |= fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.Impassable) != 0;
            if (!blocking)
                continue;

            var pos = _transform.GetWorldPosition(obstacle);
            var half = new Vector2(groundGrid.TileSize * 0.5f + WFLandingClearance);
            // Query real hull tiles so empty corners and large gaps between wings are preserved, even at an angle.
            foreach (var tile in _map.GetTilesIntersecting(hull, hullGrid, new Box2(pos - half, pos + half)))
            {
                if (tile.Tile.IsEmpty)
                    continue;
                obstacles.Add(obstacle);
                break;
            }
        }

        var reserved = new List<(Vector2i, Tile)>();
        foreach (var obstacle in obstacles)
        {
            var index = _map.WorldToTile(ground, groundGrid, _transform.GetWorldPosition(obstacle));
            var min = new Vector2(index.X, index.Y) * groundGrid.TileSize;
            reserved.Clear();
            _wfLandingBiome.ReserveTiles(ground,
                new Box2(min, min + new Vector2(groundGrid.TileSize)), reserved, mapGrid: groundGrid);
            if (reportImpacts)
                _wfFlight.GroundObstacleImpact(hull, _transform.GetWorldPosition(obstacle));
            _wfDestructible.BreakEntity(obstacle);
            if (!TerminatingOrDeleted(obstacle))
                QueueDel(obstacle);
        }
    }
}
