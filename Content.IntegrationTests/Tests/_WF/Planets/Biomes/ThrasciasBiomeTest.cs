#nullable enable
using System.Collections.Generic;
using System.Linq;
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
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>Samples the whole Thrascias surface: every tile takes weather, every region occurs, and its scenery stays on its own ground.</summary>
[TestFixture]
public sealed class ThrasciasBiomeTest
{
    private static readonly string[] SnowTiles =
    {
        "FloorSnow", "WFFloorThrasciasSnowRough", "WFFloorThrasciasSnowCalm",
    };

    private static readonly string[] IceTiles =
    {
        "WFFloorThrasciasPackIce", "WFFloorThrasciasRoughIce", "WFFloorThrasciasClearIce",
        "WFFloorThrasciasClearIceSmooth", "WFFloorThrasciasLakeIce",
    };

    private const string Dirt = "WFFloorThrasciasFrozenDirt";
    private const string Crystal = "WFFloorThrasciasCrystalIce";
    private const string Lake = "WFFloorThrasciasLakeIce";

    private static readonly HashSet<string> Pines = new()
    {
        "ShiptestFloraTreePine", "CoyoteFloraTreeMedPine", "DarkPackTreeVampPine", "VoreFloraTreeSif",
    };

    private static readonly HashSet<string> IceRocks = new()
    {
        "ShiptestFloraRockIcy", "ShiptestRockPileIcy", "AuroraFloraRockIce",
    };

    private static readonly HashSet<string> CrystalGrowth = new()
    {
        "ShiptestFloraRockCrystal", "VorePropRockCrystal", "VorePropRockCrystalDust", "AuroraFloraTreeCrystal",
        "BayCrystal", "ChompQuartz",
    };

    private static readonly HashSet<string> Tundra = new()
    {
        "CMGrassTallgrassIce", "PolarisFloraSifFrostbelle", "GoonShrubSnow", "GoonShrubSnow2",
        "GoonStoneSnow", "GoonStoneSnow2", "GoonStoneSnow3", "GoonStoneSnow4", "AuroraFloraRockSnow",
    };

    [Test]
    public async Task ThrasciasRegionsAllOccurOnTheirOwnGround()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WFPlanetSurfacePrototype>("WFSurfaceThrascias");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Thrascias", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = entities.GetComponent<BiomeComponent>(layers[0]);
            var generator = server.System<SharedBiomeSystem>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();

            var allowed = new HashSet<string>(SnowTiles.Concat(IceTiles)) { Dirt, Crystal };
            var tileCount = new Dictionary<string, int>();
            var featureCount = new Dictionary<string, int>();
            var decalCount = new Dictionary<string, int>();
            var plasmaOffCrystal = 0;
            var misplaced = new List<string>();
            var sampled = 0;
            var radius = surface.Radius;
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var pos = new Vector2i(x, y);
                generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile);
                Assert.That(tile.HasValue && !tile.Value.IsEmpty, Is.True, "Natural terrain has a hole.");
                var def = (ContentTileDefinition) tiles[tile!.Value.TypeId];
                sampled++;
                tileCount[def.ID] = tileCount.GetValueOrDefault(def.ID) + 1;
                if (!def.Weather && misplaced.Count < 5)
                    misplaced.Add($"{def.ID} hides weather");
                if (!allowed.Contains(def.ID) && misplaced.Count < 5)
                    misplaced.Add($"{def.ID} is not a Thrascias tile");

                if (generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed,
                        (Entity<MapGridComponent>?) null, out var feature))
                {
                    featureCount[feature] = featureCount.GetValueOrDefault(feature) + 1;
                    if (feature == "FloorLiquidPlasmaEntity" && def.ID != Crystal)
                        plasmaOffCrystal++;
                    var home = feature switch
                    {
                        _ when Pines.Contains(feature) => SnowTiles.Contains(def.ID),
                        _ when IceRocks.Contains(feature) => IceTiles.Contains(def.ID) && def.ID != Lake,
                        "CMRockIce" => IceTiles.Contains(def.ID) && def.ID != Lake,
                        _ when CrystalGrowth.Contains(feature) => def.ID == Crystal,
                        _ when Tundra.Contains(feature) => SnowTiles.Contains(def.ID) || def.ID == Dirt,
                        _ => true,
                    };
                    if (!home && misplaced.Count < 5)
                        misplaced.Add($"{feature} stands on {def.ID}");
                }

                if (generator.TryGetDecals(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var decals))
                    foreach (var (id, _) in decals)
                        decalCount[id] = decalCount.GetValueOrDefault(id) + 1;
            }

            double Share(int count) => 100.0 * count / sampled;
            int Tiles(params string[] ids) => ids.Sum(id => tileCount.GetValueOrDefault(id));
            int Features(IEnumerable<string> ids) => ids.Sum(id => featureCount.GetValueOrDefault(id));

            TestContext.Out.WriteLine($"Thrascias: {sampled} tiles sampled");
            foreach (var (id, count) in tileCount.OrderByDescending(kv => kv.Value))
                TestContext.Out.WriteLine($"  tile {id}: {Share(count):F2}%");
            foreach (var (id, count) in featureCount.OrderByDescending(kv => kv.Value))
                TestContext.Out.WriteLine($"  entity {id}: {Share(count):F3}%");
            foreach (var (id, count) in decalCount.OrderByDescending(kv => kv.Value))
                TestContext.Out.WriteLine($"  decal {id}: {Share(count):F3}%");

            Assert.That(misplaced, Is.Empty, string.Join("; ", misplaced));

            var snow = Tiles(SnowTiles);
            var packIce = Tiles(IceTiles.Where(id => id != Lake).ToArray());
            var lake = Tiles(Lake);
            var crystal = Tiles(Crystal);
            var pines = Features(Pines);
            var iceWalls = featureCount.GetValueOrDefault("CMRockIce");
            var crystalWalls = Features(new[] { "BayCrystal", "ChompQuartz" });
            var solids = pines + iceWalls + crystalWalls + Features(IceRocks) + Features(CrystalGrowth) + Features(Tundra.Where(id => id.Contains("Stone") || id.Contains("Rock")));

            Assert.That(Share(snow), Is.GreaterThan(55), "Snow should stay the common ground.");
            Assert.That(Share(Tiles("WFFloorThrasciasSnowRough", "WFFloorThrasciasSnowCalm", Dirt)), Is.GreaterThan(10), "Tundra variety is missing.");
            Assert.That(Share(Tiles(Dirt)), Is.GreaterThan(0), "No bare frozen dirt.");
            Assert.That(Share(packIce), Is.GreaterThan(2), "Pack ice should be a major region.");
            Assert.That(Share(packIce + lake), Is.LessThan(22), "Ice should not take over the snow.");
            Assert.That(Share(lake), Is.GreaterThan(0.3), "Frozen lakes are missing.");
            foreach (var id in IceTiles)
                Assert.That(tileCount.GetValueOrDefault(id), Is.GreaterThan(0), $"{id} never appears.");
            Assert.That(crystal, Is.GreaterThan(0), "Crystal fields are missing.");
            Assert.That(Share(crystal), Is.LessThan(8), "Crystal fields should be rare.");

            Assert.That(featureCount.GetValueOrDefault("FloorLiquidPlasmaEntity"), Is.GreaterThan(0), "Plasma pools are missing.");
            Assert.That(plasmaOffCrystal, Is.Zero, "Every plasma pool should sit in a crystal field.");
            Assert.That(featureCount.Keys.Any(id => id.Contains("PlanetmapOre")), Is.True, "Ore outcrops are missing.");
            Assert.That(featureCount.Keys.Any(id => id.Contains("Fauna")), Is.True, "Wildlife spawners are missing.");
            Assert.That(featureCount.Keys.Any(id => id.Contains("Water")), Is.False, "Thrascias is dry; its lakes are frozen tiles.");

            Assert.That(Features(Tundra), Is.GreaterThan(0), "No tundra growth.");
            Assert.That(featureCount.GetValueOrDefault("CMGrassTallgrassIce"), Is.GreaterThan(0), "No ice grass.");
            Assert.That(featureCount.GetValueOrDefault("PolarisFloraSifFrostbelle"), Is.GreaterThan(0), "No frostbelles.");
            Assert.That(featureCount.GetValueOrDefault("GoonShrubSnow") + featureCount.GetValueOrDefault("GoonShrubSnow2"), Is.GreaterThan(0), "No snow shrubs.");
            foreach (var rock in IceRocks.Concat(CrystalGrowth))
                Assert.That(featureCount.GetValueOrDefault(rock), Is.GreaterThan(0), $"{rock} never appears.");
            Assert.That(pines, Is.GreaterThan(0), "No boreal pines.");
            Assert.That(Share(pines), Is.LessThan(4), "Pine pockets should be a minority of the surface.");
            foreach (var pine in Pines)
                Assert.That(featureCount.GetValueOrDefault(pine), Is.GreaterThan(0), $"{pine} never appears.");
            Assert.That(iceWalls, Is.GreaterThan(0), "No ice cliffs.");
            Assert.That(Share(iceWalls), Is.LessThan(1.5), "Ice cliffs should be sparse.");
            Assert.That(featureCount.GetValueOrDefault("AuroraFloraTreeCrystal"), Is.GreaterThan(0), "No crystal trees.");
            Assert.That(Features(new[] { "ShiptestFloraRockCrystal", "VorePropRockCrystal", "VorePropRockCrystalDust" }), Is.GreaterThan(0), "No crystal rocks.");
            Assert.That(crystalWalls, Is.GreaterThan(0), "No crystal wall clusters.");
            Assert.That(Share(crystalWalls), Is.LessThan(0.2), "Crystal walls should be a few small clusters.");
            Assert.That(Share(solids), Is.LessThan(8), "Too much solid scenery for the world to stay walkable.");
            Assert.That(decalCount.GetValueOrDefault("AuroraDecalFloorDecalSnowdrift"), Is.GreaterThan(0), "No snowdrifts.");
            Assert.That(decalCount.GetValueOrDefault("AuroraDecalFloorDecalSnowrocks"), Is.GreaterThan(0), "No snow rock decals.");
            Assert.That(decalCount.GetValueOrDefault("CMDecalRocksIceIce1"), Is.GreaterThan(0), "No ice rock decals.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
