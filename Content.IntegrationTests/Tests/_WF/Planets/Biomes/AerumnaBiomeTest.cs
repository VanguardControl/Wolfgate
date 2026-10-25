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

/// <summary>Samples Aerumna's whole surface: only weather tiles, every region present, scenery sparse enough to walk through.</summary>
[TestFixture]
[TestOf(typeof(SharedBiomeSystem))]
public sealed class AerumnaBiomeTest
{
    private const string Surface = "WFSurfaceAerumna";

    private static readonly HashSet<string> GroundTiles = new()
    {
        "FloorChromite",
        "WFFloorAerumnaClay",
        "WFFloorAerumnaClayRock",
        "WFFloorAerumnaShroud",
        "WFFloorAerumnaCrystalBed",
    };

    private static readonly HashSet<string> GiantMushrooms = new()
    {
        "NovaTreeMushroomReverse", "NovaMushroomBlueReverse", "NovaMushroomGreenReverse",
        "AuroraFloraTreeMushroom", "NovaStumpMushroomReverse",
    };

    private static readonly HashSet<string> Glowing = new()
    {
        "ShiptestFluffGlowshroom", "NovaFloraAshLightshroom", "NovaAshLightshroomBlue", "NovaAshLightshroomGreen",
        "NovaFloraBiolumi", "NovaFloraBiolumiFlower", "NovaFloraBiolumiLamp", "NovaFloraBiolumiMine",
    };

    private static readonly HashSet<string> Boulders = new()
    {
        "CoyoteFloraChompBoulder", "CoyoteFloraChompLargeBoulder", "CoyoteFloraChompTallBoulder",
        "DarkPackFloraRockDarkpack", "DarkPackFloraRockDarkpackBig",
        "GoonStone2", "GoonStone3", "GoonStone4", "GoonStone5", "GoonStone6", "GoonStone7", "GoonStone8",
    };

    private static readonly HashSet<string> Spires = new()
    {
        "ShiptestFloraRockCrystal", "VorePropRockCrystal", "VorePropRockCrystalDust", "AuroraFloraRockSpire",
    };

    private static readonly HashSet<string> Water = new() { "MonoFloorWaterEntity" };

    /// <summary>Each region is there and in proportion, and nothing outside the planet's tile set or off weather tiles appears.</summary>
    [Test]
    public async Task RegionsAreAllPresentAndOnWeatherTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            var surface = proto.Index<WFPlanetSurfacePrototype>(Surface);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Aerumna", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = entities.GetComponent<BiomeComponent>(layers[0]);
            var generator = server.System<SharedBiomeSystem>();
            var radius = surface.Radius;

            var tileCounts = new Dictionary<string, int>();
            var featureCounts = new Dictionary<string, int>();
            var sampled = 0;
            var open = 0;
            var giants = 0;
            var glowing = 0;
            var boulders = 0;
            var spires = 0;
            var pink = 0;
            var roots = 0;
            var vines = 0;
            var shadow = 0;
            var water = 0;
            var ore = 0;
            var fungusDecals = 0;
            var offWeather = new HashSet<string>();
            var offSet = new HashSet<string>();
            Entity<MapGridComponent>? noGrid = null;

            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var pos = new Vector2i(x, y);
                Assert.That(generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, noGrid, out var tile), Is.True);
                var def = (ContentTileDefinition) tiles[tile!.Value.TypeId];
                sampled++;
                tileCounts[def.ID] = tileCounts.GetValueOrDefault(def.ID) + 1;
                if (!def.Weather)
                    offWeather.Add(def.ID);
                if (!GroundTiles.Contains(def.ID))
                    offSet.Add(def.ID);

                generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed, noGrid, out var feature);
                if (feature == null)
                    open++;
                else
                    featureCounts[feature] = featureCounts.GetValueOrDefault(feature) + 1;
                if (feature != null && GiantMushrooms.Contains(feature)) giants++;
                if (feature != null && Glowing.Contains(feature)) glowing++;
                if (feature != null && Boulders.Contains(feature)) boulders++;
                if (feature != null && Spires.Contains(feature)) spires++;
                if (feature == "CrystalPink") pink++;
                if (feature == "GoonSetpiecesRootwall") roots++;
                if (feature == "AuroraAssunzioneBushLucevine") vines++;
                if (feature == "ShadowTree") shadow++;
                if (feature != null && Water.Contains(feature)) water++;
                if (feature == "MonoPlanetmapOreChromite") ore++;
                if (def.ID == "WFFloorAerumnaShroud"
                    && generator.TryGetDecals(pos, biome.Layers, biome.Seed, noGrid, out var decals)
                    && decals.Any(decal => decal.ID == "AuroraDecalFloorDecalFungus"))
                    fungusDecals++;
            }

            double Share(int n) => (double) n / sampled;
            string Pct(int n) => $"{100.0 * n / sampled:F2}%";
            TestContext.Out.WriteLine($"Aerumna: {sampled} tiles sampled, open {Pct(open)}");
            foreach (var (id, n) in tileCounts.OrderByDescending(kv => kv.Value))
                TestContext.Out.WriteLine($"  tile {id}: {Pct(n)}");
            TestContext.Out.WriteLine($"  giant mushrooms {Pct(giants)}, glowing plants {Pct(glowing)}, boulders {Pct(boulders)}, "
                + $"crystal spires {Pct(spires)}, pink crystal {Pct(pink)}, root walls {Pct(roots)}, luce-vine {Pct(vines)}, "
                + $"shadow trees {Pct(shadow)}, water {Pct(water)}, ore {Pct(ore)}, fungus decals {Pct(fungusDecals)}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(offWeather, Is.Empty, "Rain would not draw on these tiles.");
                Assert.That(offSet, Is.Empty, "Tiles outside Aerumna's ground set.");

                // Major regions: at least ~2% of the surface each.
                Assert.That(Share(tileCounts.GetValueOrDefault("WFFloorAerumnaShroud")), Is.GreaterThan(0.02), "No fungal forest ground.");
                Assert.That(Share(tileCounts.GetValueOrDefault("WFFloorAerumnaClay")), Is.GreaterThan(0.02), "No clay flats.");
                Assert.That(Share(tileCounts.GetValueOrDefault("FloorChromite")), Is.GreaterThan(0.2), "Aerumna should still read as chromite.");
                Assert.That(giants, Is.GreaterThan(0), "No giant mushrooms.");
                Assert.That(glowing, Is.GreaterThan(0), "No glowshrooms or bioluminescent plants.");
                Assert.That(boulders, Is.GreaterThan(0), "No boulder fields.");
                Assert.That(shadow, Is.GreaterThan(0), "The shadow grove is gone.");
                Assert.That(water, Is.GreaterThan(0), "The pools are gone.");
                Assert.That(ore, Is.GreaterThan(0), "The chromite ore is gone.");
                Assert.That(fungusDecals, Is.GreaterThan(0), "No fungus decals.");

                // Rare regions: present, but only just.
                Assert.That(tileCounts.GetValueOrDefault("WFFloorAerumnaClayRock"), Is.GreaterThan(0), "No bare rock patches.");
                Assert.That(tileCounts.GetValueOrDefault("WFFloorAerumnaCrystalBed"), Is.GreaterThan(0), "No crystal spire region.");
                Assert.That(spires, Is.GreaterThan(0), "No crystal spires.");
                Assert.That(pink, Is.GreaterThan(0), "No pink crystal.");
                Assert.That(roots, Is.GreaterThan(0), "No overgrown ruins.");
                Assert.That(vines, Is.GreaterThan(0), "No luce-vine.");
                Assert.That(Share(tileCounts.GetValueOrDefault("WFFloorAerumnaCrystalBed")), Is.LessThan(0.1), "Crystal spires are meant to be rare.");
                Assert.That(Share(roots), Is.LessThan(0.005), "Root walls must stay very sparse.");

                // Walkability and the ecology budget.
                Assert.That(Share(open), Is.GreaterThan(0.6), "Most of the surface should be open.");
                Assert.That(Share(giants + boulders + spires + roots + shadow), Is.LessThan(0.12), "Too much solid scenery to walk through.");
            }
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
