#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._DV.Planet;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Markers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A planet's biome marker layers must find tiles for every vein in a marker chunk. A mask its biome never lays
/// places nothing, costs a scan of the whole chunk per layer and logs a warning for every vein.
/// </summary>
[TestFixture]
[TestOf(typeof(PlanetPrototype))]
public sealed class PlanetOreMarkerTest
{
    private const int Seed = 20260914;
    private static readonly Entity<MapGridComponent>? NoGrid = null;

    [Test]
    public async Task EveryMarkerLayerHasRoomForItsVeins()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var biome = server.System<SharedBiomeSystem>();

            foreach (var planet in server.ProtoMan.EnumeratePrototypes<PlanetPrototype>().OrderBy(p => p.ID))
            {
                if (planet.BiomeMarkerLayers.Count == 0)
                    continue;

                var layers = server.ProtoMan.Index(planet.Biome).Layers;
                var entities = new Dictionary<Vector2i, string?>();

                foreach (var layerId in planet.BiomeMarkerLayers)
                {
                    var marker = server.ProtoMan.Index(layerId);
                    // The first marker chunk, sized as BiomeSystem.BuildMarkerChunks sizes it.
                    var buffer = (int) (marker.Radius / 2f);
                    var bounds = new Box2i(new Vector2i(buffer, buffer),
                        new Vector2i(marker.Size - buffer, marker.Size - buffer));
                    var veins = Math.Min((int) (bounds.Area / (marker.Radius * marker.Radius)), marker.MaxCount);
                    var needed = veins * marker.MaxGroupSize;
                    var found = 0;

                    for (var x = bounds.Left; x < bounds.Right; x++)
                    {
                        for (var y = bounds.Bottom; y < bounds.Top; y++)
                        {
                            var index = new Vector2i(x, y);
                            if (!entities.TryGetValue(index, out var entity))
                            {
                                entity = biome.TryGetBiomeTile(index, layers, Seed, NoGrid, out var tile)
                                         && biome.TryGetEntity(index, layers, tile.Value, Seed, NoGrid, out var ent)
                                    ? ent
                                    : null;
                                entities[index] = entity;
                            }

                            if (Fits(marker, entity))
                                found++;
                        }
                    }

                    Assert.That(found, Is.GreaterThanOrEqualTo(needed),
                        $"{planet.ID}'s {layerId} needs {needed} tiles for {veins} veins in a marker chunk of {planet.Biome} and found {found}");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Whether a marker may take a tile holding this biome entity, as BiomeSystem.GetMarkerNodes decides.</summary>
    private static bool Fits(BiomeMarkerLayerPrototype marker, string? entity)
    {
        if (marker.EntityMask.Count > 0 && (entity == null || !marker.EntityMask.ContainsKey(entity)))
            return false;

        return entity == null || marker.Prototype == null;
    }
}
