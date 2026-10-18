using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets.Bounds;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

/// <summary>Big creatures let loose on a world's open ground once all of it exists.</summary>
public sealed partial class WFPlanetPoiSystem
{
    /// <summary>Tiles a roamer starts beyond the landing zone's edge.</summary>
    private const float RoamerClearance = 24f;

    private void OnPreloaded(ref WFPlanetPreloadedEvent args)
    {
        var ground = args.Ground;
        if (!TryComp<WFPlanetPoiComponent>(ground, out var record) || record.PendingRoamers.Count == 0
            || !TryComp<BiomeComponent>(ground, out var biome) || !TryComp<MapGridComponent>(ground, out var grid))
            return;

        var inner = record.RoamerInner;
        var outer = record.RoamerRadius;
        var roamers = record.PendingRoamers.ToList();
        record.PendingRoamers.Clear();
        if (outer <= inner)
            return;

        foreach (var roamer in roamers)
        {
            var count = _random.Next(roamer.MinCount, roamer.MaxCount + 1);
            for (var i = 0; i < count; i++)
            {
                for (var attempt = 0; attempt < 64; attempt++)
                {
                    var spot = record.RoamerCentre + _random.NextAngle().ToVec() * MathF.Sqrt(_random.NextFloat(inner * inner, outer * outer));
                    var tile = spot.Floored();
                    // Open ground: no site, road or anything standing there.
                    if (_biome.WfIsPinned((ground, biome), tile) || _maps.GetTileRef(ground, grid, tile).Tile.IsEmpty
                        || _maps.GetAnchoredEntitiesEnumerator(ground, grid, tile).MoveNext(out _))
                        continue;

                    Spawn(roamer.Id, new EntityCoordinates(ground, (Vector2) tile + new Vector2(0.5f)));
                    break;
                }
            }
        }
    }
}
