#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Caverns;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Each world's cavern noise, sampled around its gate: open enough, connected, ore only in rock, signature present.</summary>
[TestFixture]
[TestOf(typeof(WFCavernSampler))]
public sealed class CavernBiomeTest
{
    /// <summary>Section 4: the share of open tiles each world's 192-tile sample must fall within.</summary>
    private static readonly Dictionary<string, (float Min, float Max)> OpenBands = new()
    {
        { "WFSurfaceAsclepiu", (0.35f, 0.55f) },
        { "WFSurfaceFervidus", (0.30f, 0.45f) },
        { "WFSurfaceMerak", (0.40f, 0.60f) },
        { "WFSurfaceAerumna", (0.25f, 0.40f) },
        { "WFSurfaceThrascias", (0.35f, 0.55f) },
        { "WFSurfaceCarcinoma", (0.35f, 0.55f) },
    };

    /// <summary>
    /// Section 6: what each world's signature feature leaves in the noise. A named template must place the entity by
    /// itself, and every tile where it does must carry it in the cavern too, so another layer can't stand in for it.
    /// </summary>
    private static readonly Dictionary<string, Signature[]> Signatures = new()
    {
        { "WFSurfaceAsclepiu", new[] { new Signature("plunge pools", "FloorWater") } },
        { "WFSurfaceFervidus", new[] { new Signature("lava tubes", null, "FloorLavaEntity", "WFCavernSignatureFervidus") } },
        {
            "WFSurfaceMerak", new[]
            {
                new Signature("hall pillars", "WFCavernFloorSandstone", "WallRockSand"),
                new Signature("fossils", null, "WallRockSandArtifactFragment"),
                new Signature("buried camps", null, "SalvageHumanCorpseSpawner"),
            }
        },
        {
            "WFSurfaceAerumna", new[]
            {
                new Signature("pink geodes", null, "CrystalPink"),
                new Signature("shadow trees", null, "ShadowTree"),
            }
        },
        {
            "WFSurfaceThrascias", new[]
            {
                new Signature("ice galleries", "FloorIce"),
                new Signature("ice columns", null, "WallIce"),
                new Signature("plasma lakes", "FloorIce", "FloorLiquidPlasmaEntity", "WFCavernSignatureThrascias"),
            }
        },
        { "WFSurfaceCarcinoma", new[] { new Signature("blood channels", null, "WFBloodRiver") } },
    };

    /// <summary>Edge of the open-fraction sample.</summary>
    private const int OpenSize = 192;

    /// <summary>Edge of the connectivity sample.</summary>
    private const int ConnectSize = 128;

    /// <summary>The largest walkable region must hold at least this share of the walkable tiles.</summary>
    private const float MinLargestShare = 0.6f;

    /// <summary>Edge of the signature sample, taken every other tile.</summary>
    private const int SignatureSize = 512;

    /// <summary>The share of open tiles in 192² around each gate falls inside its world's band.</summary>
    [Test]
    public async Task OpenFractionInBand()
    {
        var samples = await SampleEveryWorld(OpenSize, 1);

        using (Assert.EnterMultipleScope())
        {
            foreach (var world in samples)
            {
                var sample = world.Sample;
                var open = sample.OpenFraction();
                var (min, max) = OpenBands[world.Surface];
                TestContext.Out.WriteLine($"{world.Surface}: open {open:P1}, largest region {sample.LargestRegionShare():P1}, veins {sample.VeinFraction():P1} of rock.");

                Assert.That(open, Is.InRange(min, max), $"{world.Surface}: {open:P1} of the tiles around the gate are open, outside {min:P0}-{max:P0}.");
            }
        }
    }

    /// <summary>In 128² around each gate, the largest 4-connected walkable region holds most of the walkable tiles; lava and plasma count as barriers.</summary>
    [Test]
    public async Task TunnelsConnect()
    {
        var samples = await SampleEveryWorld(ConnectSize, 1);

        using (Assert.EnterMultipleScope())
        {
            foreach (var world in samples)
            {
                var largest = world.Sample.LargestRegionShare();
                TestContext.Out.WriteLine($"{world.Surface}: largest walkable region {largest:P1} of the walkable tiles.");

                Assert.That(largest, Is.GreaterThanOrEqualTo(MinLargestShare),
                    $"{world.Surface}: the largest walkable region holds only {largest:P1} of the walkable tiles.");
            }
        }
    }

    /// <summary>Every ore vein sampled around each gate stands on the world's rock substrate, and there are veins.</summary>
    [Test]
    public async Task OreOnlyInRock()
    {
        var samples = await SampleEveryWorld(OpenSize, 1);

        using (Assert.EnterMultipleScope())
        {
            foreach (var world in samples)
            {
                var sample = world.Sample;
                var substrate = Floors[world.Surface].Substrate;
                var veins = 0;

                for (var i = 0; i < sample.Count; i++)
                {
                    if (!sample.Vein[i])
                        continue;

                    veins++;
                    Assert.That(sample.Tiles[i], Is.EqualTo(substrate),
                        $"{world.Surface}: {sample.Entities[i]} at {sample.IndexOf(i)} stands on {sample.Tiles[i]}, not the rock substrate.");
                }

                Assert.That(veins, Is.Positive, $"{world.Surface}: no ore vein near the gate.");
            }
        }
    }

    /// <summary>Each world's signature feature turns up in 512² around its gate, placed by its own template where one is named.</summary>
    [Test]
    public async Task SignaturePresent()
    {
        var samples = await SampleEveryWorld(SignatureSize, 2, true);

        using (Assert.EnterMultipleScope())
        {
            foreach (var world in samples)
            {
                var sample = world.Sample;

                foreach (var signature in Signatures[world.Surface])
                {
                    if (signature.Template is not { } template)
                    {
                        var found = Enumerable.Range(0, sample.Count).Count(i => signature.Matches(sample, i));
                        TestContext.Out.WriteLine($"{world.Surface}: {found} samples of {signature.What}.");

                        Assert.That(found, Is.Positive, $"{world.Surface}: no {signature.What} in {SignatureSize}² around the gate.");
                        continue;
                    }

                    var alone = world.Templates[template];
                    var placed = Enumerable.Range(0, alone.Count).Where(i => signature.Matches(alone, i)).ToList();
                    var missing = placed.Where(i => !signature.Matches(sample, i)).ToList();
                    TestContext.Out.WriteLine($"{world.Surface}: {placed.Count} samples of {signature.What} from {template}, {missing.Count} missing in the cavern.");

                    Assert.That(placed, Is.Not.Empty, $"{world.Surface}: {template} places no {signature.What} in {SignatureSize}² around the gate.");
                    Assert.That(missing, Is.Empty,
                        $"{world.Surface}: {missing.Count} of the {placed.Count} tiles where {template} places {signature.What} lack it in the cavern, the first at {(missing.Count > 0 ? sample.IndexOf(missing[0]) : default)}.");
                }
            }
        }
    }

    /// <summary>Builds each world in turn on one pair and samples its cavern noise around the gate, and optionally its signature templates alone.</summary>
    private static async Task<List<WorldSample>> SampleEveryWorld(int size, int step, bool templates = false)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var sampler = server.System<WFCavernSampler>();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var samples = new List<WorldSample>();

        await EnableCaverns(pair);

        foreach (var surfaceId in Surfaces)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var gate = await Gate(pair, world);

                await server.WaitPost(() =>
                {
                    var biome = server.EntMan.GetComponent<BiomeComponent>(world.Cavern);
                    var centre = gate.CentreTile;
                    var alone = new Dictionary<string, WFCavernSample>();

                    if (templates)
                    {
                        foreach (var signature in Signatures[surfaceId])
                        {
                            if (signature.Template is { } template && !alone.ContainsKey(template))
                                alone[template] = sampler.Sample(proto.Index<BiomeTemplatePrototype>(template).Layers, biome.Seed, centre, size, step);
                        }
                    }

                    samples.Add(new WorldSample(surfaceId, sampler.Sample(biome, centre, size, step), alone));
                });
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
        return samples;
    }

    /// <summary>A world's cavern sample and, by template id, its signature templates sampled alone.</summary>
    private sealed record WorldSample(string Surface, WFCavernSample Sample, Dictionary<string, WFCavernSample> Templates);

    /// <summary>A signature: what it is, the tile and entity it leaves (null for any), and the template that places it.</summary>
    private sealed record Signature(string What, string? Tile, string? Entity = null, string? Template = null)
    {
        /// <summary>Whether a sample carries this signature.</summary>
        public bool Matches(WFCavernSample sample, int i)
        {
            return (Tile == null || sample.Tiles[i] == Tile) && (Entity == null || sample.Entities[i] == Entity);
        }
    }
}
