using System.Numerics;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Chunk;

/// <summary>
/// The gangway: a one-tile lattice catwalk the rig lays on the hull from the berth marker out to the hanging chunk,
/// and lifts again when the chunk drops. Orbit stays deadly off the catwalk; this is the one way across.
/// </summary>
public sealed partial class WFPlanetChunkSystem
{
    /// <summary>The catwalk tile; the grate over space the game already ships.</summary>
    public const string GangwayTile = "Lattice";

    /// <summary>Furthest the catwalk is walked out from the marker before the disc is declared unreachable.</summary>
    private const float GangwayMaxLength = 96f;

    /// <summary>Tile writes for one gangway, pooled.</summary>
    private readonly List<(Vector2i Index, Tile Tile)> _gangwayTiles = new();

    /// <summary>
    /// Lays the catwalk along the marker's facing: from the marker's own tile, over any tile the hull does not already
    /// have, to one tile inside the disc. Refused whole if another grid sits on the line; the hull keeps its shape.
    /// </summary>
    private void LayGangway(
        Entity<WFPlanetCrackerComponent> cracker,
        Entity<WFPlanetChunkComponent> chunk,
        Vector2 hang,
        float radius,
        IReadOnlySet<EntityUid> ignored)
    {
        var hull = cracker.Owner;

        if (cracker.Comp.Berth is not { } netBerth || !TryGetEntity(netBerth, out var marker))
            return;

        if (!TryComp<MapGridComponent>(hull, out var hullGrid))
            return;

        var (markerPos, markerRot) = _transform.GetWorldPositionRotation(marker.Value);
        var facing = markerRot.ToWorldVec();
        var mapId = Transform(hull).MapID;
        var tile = new Tile(_tileDefs[GangwayTile].TileId);

        _gangwayTiles.Clear();
        var last = Vector2i.Zero;
        var any = false;
        var reached = false;

        // Half-tile steps so a diagonal line never skips a tile.
        for (var step = 0f; step <= GangwayMaxLength; step += 0.5f)
        {
            var point = markerPos + facing * step;

            if ((point - hang).Length() <= radius - 1f)
            {
                reached = true;
                break;
            }

            var index = _map.WorldToTile(hull, hullGrid, point);

            if (any && index == last)
                continue;

            any = true;
            last = index;

            if (_map.TryGetTileRef(hull, hullGrid, index, out var existing) && !existing.Tile.IsEmpty)
                continue;

            // Another hull on the line: a docked ship or a stranger. Nothing is laid through it.
            var centre = _map.GridTileToWorld(hull, hullGrid, index).Position;
            _found.Clear();
            _mapManager.FindGridsIntersecting(mapId, Box2.CenteredAround(centre, Vector2.One), ref _found, approx: false, includeMap: false);

            foreach (var found in _found)
            {
                if (found.Owner == hull || found.Owner == chunk.Owner || ignored.Contains(found.Owner))
                    continue;

                Log.Warning($"{ToPrettyString(found.Owner)} sits on {ToPrettyString(hull)}'s gangway line; no gangway was laid to the chunk.");
                _gangwayTiles.Clear();
                return;
            }

            _gangwayTiles.Add((index, tile));
        }

        if (!reached)
        {
            Log.Warning($"{ToPrettyString(hull)}'s berth marker line never reaches its chunk; no gangway was laid.");
            _gangwayTiles.Clear();
            return;
        }

        if (_gangwayTiles.Count == 0)
            return;

        _map.SetTiles(hull, hullGrid, _gangwayTiles);

        foreach (var (index, _) in _gangwayTiles)
        {
            chunk.Comp.GangwayTiles.Add(index);
        }

        chunk.Comp.GangwayHull = GetNetEntity(hull);
        Dirty(chunk);
    }

    /// <summary>Lifts the catwalk: every recorded tile still carrying the grate goes back to space.</summary>
    public void LiftGangway(Entity<WFPlanetChunkComponent> chunk)
    {
        if (chunk.Comp.GangwayTiles.Count == 0)
            return;

        if (TryGetEntity(chunk.Comp.GangwayHull, out var hull) && TryComp<MapGridComponent>(hull, out var hullGrid))
        {
            var tileId = _tileDefs[GangwayTile].TileId;
            _gangwayTiles.Clear();

            foreach (var index in chunk.Comp.GangwayTiles)
            {
                if (_map.TryGetTileRef(hull.Value, hullGrid, index, out var existing) && existing.Tile.TypeId == tileId)
                    _gangwayTiles.Add((index, Tile.Empty));
            }

            _map.SetTiles(hull.Value, hullGrid, _gangwayTiles);
        }

        chunk.Comp.GangwayTiles.Clear();
        chunk.Comp.GangwayHull = null;
        Dirty(chunk);
    }
}
