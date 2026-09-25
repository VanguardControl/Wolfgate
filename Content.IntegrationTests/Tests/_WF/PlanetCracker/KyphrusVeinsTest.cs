using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Shared._DV.Planet;
using Content.Shared._WF.PlanetCracker;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Markers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>Every Kyphrus world's crack site: its vein survives its own terrain and its layers follow the planet's own.</summary>
[TestFixture]
public sealed class KyphrusVeinsTest
{
    [TestCase("PlanetFervidus", "Fervidus", "FloorBasalt")]
    [TestCase("PlanetMerak", "Merak", "FloorDesertPlanet")]
    [TestCase("PlanetAerumna", "Aerumna", "FloorChromite")]
    [TestCase("PlanetThrascias", "Thrascias", "FloorSnow")]
    [TestCase("WFPlanetCarcinoma", "Carcinoma", "WFFloorFlesh")]
    public async Task EachWorldsVeinSurvivesItsOwnTerrain(string planet, string name, string floor)
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
            Assert.That(server.System<WFPlanetRegistrySystem>().TryGetSurface(planet, out var surface), Is.True,
                "The sector body must automatically resolve its surface.");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface!, Vector2.Zero, name, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(em.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);

            AssertSiteLayers(proto, em, surface!, layers[0], "WFVeinTable" + name);

            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var grid = em.GetComponent<MapGridComponent>(layers[0]);
            server.System<SharedMapSystem>().SetTile(layers[0], grid, Vector2i.Zero, new Tile(tiles[floor].TileId));
            vein = em.SpawnEntity("WFDeepVein" + name, new EntityCoordinates(layers[0], new Vector2(0.5f, 0.5f)));
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(vein), Is.True, "The world's vein rejected its own terrain.");
            var seam = em.GetComponent<WFDeepVeinComponent>(vein);
            var table = proto.Index<WFVeinTablePrototype>("WFVeinTable" + name);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(seam.TotalYield, Is.GreaterThan(0));
                Assert.That(seam.Remaining, Is.EqualTo(seam.TotalYield));
                Assert.That(seam.Ore is { } ore && table.Ores.ContainsKey(ore), Is.True);
                Assert.That(em.GetComponent<TransformComponent>(vein).Anchored, Is.True);
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Asclepiu's deep veins land after its ore layers, the position they had inside its planet prototype.</summary>
    [Test]
    public async Task AsclepiuDeepVeinsFollowItsOreLayers()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        var layers = await BuildStandalone(pair);

        await server.WaitAssertion(() =>
        {
            AssertSiteLayers(proto, server.EntMan, proto.Index<WFPlanetSurfacePrototype>(SurfaceProto), layers[0],
                "WFVeinTableAsclepiu");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Exactly one site names the surface, with the expected table, and the ground carries its layers last.</summary>
    private static void AssertSiteLayers(IPrototypeManager proto,
        IEntityManager em,
        WFPlanetSurfacePrototype surface,
        EntityUid ground,
        string table)
    {
        var sites = proto.EnumeratePrototypes<WFCrackSitePrototype>().Where(s => s.Surface == surface.ID).ToList();
        Assert.That(sites, Has.Count.EqualTo(1), $"{surface.ID} needs exactly one crack site.");

        var site = sites[0];
        var expected = new List<ProtoId<BiomeMarkerLayerPrototype>>(proto.Index<PlanetPrototype>(surface.Ground).BiomeMarkerLayers);
        expected.AddRange(site.MarkerLayers);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(site.Veins?.Id, Is.EqualTo(table), $"{site.ID} rolls from the wrong vein table.");
            Assert.That(site.MarkerLayers, Is.Not.Empty, $"{site.ID} adds no deep-vein layer, so the world has no veins.");
            Assert.That(MarkerLayers(em.GetComponent<BiomeComponent>(ground)), Is.EqualTo(expected),
                "The ground's marker layers are not the planet's own followed by the site's, so placement would re-roll.");
        }
    }
}
