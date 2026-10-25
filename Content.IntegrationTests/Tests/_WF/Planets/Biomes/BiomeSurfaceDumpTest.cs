#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

[TestFixture]
public sealed class BiomeSurfaceDumpTest
{
    [TestCase("WFSurfaceAsclepiu")]
    [TestCase("WFSurfaceMerak")]
    [TestCase("WFSurfaceFervidus")]
    public async Task Dump(string surfaceId)
    {
        var dir = Environment.GetEnvironmentVariable("WF_BIOME_DUMP");
        if (string.IsNullOrEmpty(dir))
            return;
        await using var pair = await PoolManager.GetServerClient();
        await PlanetFixture.EnableFeature(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var protos = server.ResolveDependency<IPrototypeManager>();
            var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
            var surface = protos.Index<WFPlanetSurfacePrototype>(surfaceId);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, surfaceId, null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = entities.GetComponent<BiomeComponent>(layers[0]);
            var biomes = server.System<SharedBiomeSystem>();
            var radius = surface.Radius;
            var palette = new List<string>();
            var paletteIndex = new Dictionary<string, int>();
            var ents = new List<string>();
            var entIndex = new Dictionary<string, int>();
            Directory.CreateDirectory(dir);
            using var bin = new BinaryWriter(File.Create(Path.Combine(dir, surfaceId + ".bin")));
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                var index = new Vector2i(x, y);
                if (x * x + y * y > radius * radius || !biomes.TryGetBiomeTile(index, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile))
                {
                    bin.Write((ushort) 65535);
                    bin.Write((byte) 0);
                    bin.Write((ushort) 0);
                    continue;
                }
                var id = ((ContentTileDefinition) tileDefs[tile.Value.TypeId]).ID;
                if (!paletteIndex.TryGetValue(id, out var ti))
                {
                    ti = palette.Count;
                    palette.Add(id);
                    paletteIndex[id] = ti;
                }
                var ei = 0;
                if (biomes.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, (Entity<MapGridComponent>?) null, out var entity))
                {
                    if (!entIndex.TryGetValue(entity, out ei))
                    {
                        ents.Add(entity);
                        ei = ents.Count;
                        entIndex[entity] = ei;
                    }
                }
                bin.Write((ushort) ti);
                bin.Write(tile.Value.Variant);
                bin.Write((ushort) ei);
            }
            File.WriteAllLines(Path.Combine(dir, surfaceId + ".tiles.txt"), palette);
            File.WriteAllLines(Path.Combine(dir, surfaceId + ".ents.txt"), ents);
            File.WriteAllText(Path.Combine(dir, surfaceId + ".radius.txt"), radius.ToString());
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
