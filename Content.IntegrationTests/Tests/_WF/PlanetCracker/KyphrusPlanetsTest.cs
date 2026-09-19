using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

[TestFixture]
public sealed class KyphrusPlanetsTest
{
    [TestCase("Fervidus", "FloorBasalt")]
    [TestCase("Merak", "FloorDesertPlanet")]
    [TestCase("Aerumna", "FloorChromite")]
    [TestCase("Thrascias", "FloorSnow")]
    [TestCase("Carcinoma", "WFFloorFlesh")]
    public async Task NewWorldBuildsAndItsVeinsSurvive(string name, string floor)
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var em = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var layers = new List<EntityUid>();
        var vein = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            var registry = server.System<WFPlanetRegistrySystem>();
            Assert.That(registry.TryGetSurface("Planet" + name, out var surface), Is.True,
                "The sector body must automatically resolve its surface.");
            Assert.That(surface!.BuildAtRoundStart, Is.True);
            Assert.That(surface.CloudLayer, Is.False, "A cloud deck would hide the surface from orbit.");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, name, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(em.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            Assert.That(layers, Has.Count.EqualTo(5));
            var orbit = em.GetComponent<WFOrbitLayerComponent>(layers[^1]);
            Assert.That(orbit.RadarLayers, Is.Not.Empty);
            Assert.That(orbit.RadarSeed, Is.EqualTo(surface.Seed));
            var radar = server.System<WFPlanetRadarSystem>();
            for (var x = -100; x <= 100; x += 25)
            for (var y = -100; y <= 100; y += 25)
                Assert.That(radar.Sample(orbit, new Vector2(x, y)), Is.Not.Null,
                    "The natural surface must not have noise gaps.");

            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var grid = em.GetComponent<MapGridComponent>(layers[0]);
            server.System<SharedMapSystem>().SetTile(layers[0], grid, Vector2i.Zero,
                new Tile(tiles[floor].TileId));
            vein = em.SpawnEntity("WFDeepVein" + name,
                new EntityCoordinates(layers[0], new Vector2(0.5f, 0.5f)));
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(vein), Is.True, "The world's vein rejected its own terrain.");
            var seam = em.GetComponent<WFDeepVeinComponent>(vein);
            var table = proto.Index<WFVeinTablePrototype>("WFVeinTable" + name);
            Assert.That(seam.TotalYield, Is.GreaterThan(0));
            Assert.That(seam.Remaining, Is.EqualTo(seam.TotalYield));
            Assert.That(table.Ores.ContainsKey(seam.Ore), Is.True);
            Assert.That(em.GetComponent<TransformComponent>(vein).Anchored, Is.True);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
