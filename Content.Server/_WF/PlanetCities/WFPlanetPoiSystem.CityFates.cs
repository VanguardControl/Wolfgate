using System.Linq;
using System.Numerics;
using Content.Server.Spawners.Components;
using Content.Shared._WF.PlanetCities;
using Content.Shared.Light.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

/// <summary>What a fixed city has come to this round: overrun by something, taken over by crazies, or left alone.</summary>
public sealed partial class WFPlanetPoiSystem
{
    /// <summary>Tiles a fate's groups keep from the city's spawn points, so nobody joins into a fight.</summary>
    private const float FateSpawnClearance = 16f;

    private void RollCityFate(WFFixedCityPrototype city, Entity<MapGridComponent> ground, HashSet<Vector2i> tiles, HashSet<EntityUid> created)
    {
        if (city.Fates.Count == 0)
            return;

        var roll = _random.NextFloat(city.Fates.Sum(fate => fate.Weight));
        var picked = city.Fates[^1];
        foreach (var fate in city.Fates)
        {
            roll -= fate.Weight;
            if (roll >= 0)
                continue;
            picked = fate;
            break;
        }
        Log.Info($"{city.ID} on {ToPrettyString(ground)} this round: {picked.Name}");
        if (picked.Spawns is not { } spawns)
            return;

        var spawnPoints = new List<Vector2>();
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (points.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == ground.Owner)
                spawnPoints.Add(xform.LocalPosition);
        }

        TryComp<RoofComponent>(ground, out var roof);
        bool Open(Vector2i tile) => tiles.Contains(tile) && !_maps.GetTileRef(ground, ground.Comp, tile).Tile.IsEmpty
                                    && !_maps.GetAnchoredEntitiesEnumerator(ground, ground.Comp, tile).MoveNext(out _);
        var candidates = tiles.Where(tile => Open(tile)
                && spawnPoints.All(point => Vector2.Distance(point, (Vector2) tile) > FateSpawnClearance)
                && (picked.Gathering.Count == 0 || roof == null || !_roof.IsRooved((ground, ground.Comp, roof), tile)))
            .ToList();
        if (candidates.Count == 0)
            return;

        var groups = _random.Next(picked.MinGroups, picked.MaxGroups + 1);
        for (var i = 0; i < groups; i++)
        {
            var centre = _random.Pick(candidates);
            foreach (var id in picked.Gathering)
                created.Add(Spawn(id, new EntityCoordinates(ground, (Vector2) centre + new Vector2(0.5f))));

            var size = _random.Next(picked.MinGroupSize, picked.MaxGroupSize + 1);
            for (var member = 0; member < size; member++)
            {
                var near = Enumerable.Range(0, 12)
                    .Select(_ => centre + new Vector2i(_random.Next(-3, 4), _random.Next(-3, 4)))
                    .FirstOrDefault(tile => tile != centre && Open(tile), centre);
                foreach (var id in _entityTables.GetSpawns(spawns))
                    created.Add(Spawn(id, new EntityCoordinates(ground, (Vector2) near + new Vector2(0.5f))));
            }
        }
    }
}
