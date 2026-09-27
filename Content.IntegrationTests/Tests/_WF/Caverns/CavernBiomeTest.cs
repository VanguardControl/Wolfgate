#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Caverns;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Maths;
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

    /// <summary>Section 6: what each world's signature feature leaves in the noise, as (what, tile or null, entity or null).</summary>
    private static readonly Dictionary<string, (string What, string? Tile, string? Entity)[]> Signatures = new()
    {
        { "WFSurfaceAsclepiu", new (string, string?, string?)[] { ("plunge pools", "FloorWater", null) } },
        { "WFSurfaceFervidus", new (string, string?, string?)[] { ("lava", null, "FloorLavaEntity") } },
        {
            "WFSurfaceMerak", new (string, string?, string?)[]
            {
                ("hall pillars", "WFCavernFloorSandstone", "WallRockSand"),
                ("fossils", null, "WallRockSandArtifactFragment"),
            }
        },
        {
            "WFSurfaceAerumna", new (string, string?, string?)[]
            {
                ("pink geodes", null, "CrystalPink"),
                ("shadow trees", null, "ShadowTree"),
            }
        },
        {
            "WFSurfaceThrascias", new (string, string?, string?)[]
            {
                ("ice galleries", "FloorIce", null),
                ("ice columns", null, "WallIce"),
            }
        },
        { "WFSurfaceCarcinoma", new (string, string?, string?)[] { ("blood channels", null, "WFBloodRiver") } },
    };

    /// <summary>Edge of the open-fraction sample.</summary>
    private const int OpenSize = 192;

    /// <summary>Edge of the connectivity sample.</summary>
    private const int ConnectSize = 128;

    /// <summary>The largest open region must hold at least this share of the open tiles.</summary>
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
            foreach (var (surfaceId, sample) in samples)
            {
                var open = sample.OpenFraction();
                var (min, max) = OpenBands[surfaceId];
                TestContext.Out.WriteLine($"{surfaceId}: open {open:P1}, largest region {sample.LargestRegionShare():P1}, veins {sample.VeinFraction():P1} of rock.");

                Assert.That(open, Is.InRange(min, max), $"{surfaceId}: {open:P1} of the tiles around the gate are open, outside {min:P0}-{max:P0}.");
            }
        }
    }

    /// <summary>In 128² around each gate, the largest 4-connected open region holds most of the open tiles.</summary>
    [Test]
    public async Task TunnelsConnect()
    {
        var samples = await SampleEveryWorld(ConnectSize, 1);

        using (Assert.EnterMultipleScope())
        {
            foreach (var (surfaceId, sample) in samples)
            {
                var largest = sample.LargestRegionShare();
                TestContext.Out.WriteLine($"{surfaceId}: largest region {largest:P1} of the open tiles.");

                Assert.That(largest, Is.GreaterThanOrEqualTo(MinLargestShare),
                    $"{surfaceId}: the largest open region holds only {largest:P1} of the open tiles.");
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
            foreach (var (surfaceId, sample) in samples)
            {
                var substrate = Floors[surfaceId].Substrate;
                var veins = 0;

                for (var i = 0; i < sample.Count; i++)
                {
                    if (!sample.Vein[i])
                        continue;

                    veins++;
                    Assert.That(sample.Tiles[i], Is.EqualTo(substrate),
                        $"{surfaceId}: {sample.Entities[i]} at {sample.IndexOf(i)} stands on {sample.Tiles[i]}, not the rock substrate.");
                }

                Assert.That(veins, Is.Positive, $"{surfaceId}: no ore vein near the gate.");
            }
        }
    }

    /// <summary>Each world's signature feature turns up in 512² around its gate.</summary>
    [Test]
    public async Task SignaturePresent()
    {
        var samples = await SampleEveryWorld(SignatureSize, 2);

        using (Assert.EnterMultipleScope())
        {
            foreach (var (surfaceId, sample) in samples)
            {
                foreach (var (what, tile, entity) in Signatures[surfaceId])
                {
                    var found = Enumerable.Range(0, sample.Count).Count(i =>
                        (tile == null || sample.Tiles[i] == tile) && (entity == null || sample.Entities[i] == entity));
                    TestContext.Out.WriteLine($"{surfaceId}: {found} samples of {what}.");

                    Assert.That(found, Is.Positive, $"{surfaceId}: no {what} in {SignatureSize}² around the gate.");
                }
            }
        }
    }

    /// <summary>Builds each world in turn on one pair and samples its cavern noise around the gate.</summary>
    private static async Task<List<(string Surface, WFCavernSample Sample)>> SampleEveryWorld(int size, int step)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var sampler = server.System<WFCavernSampler>();
        var samples = new List<(string, WFCavernSample)>();

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
                    samples.Add((surfaceId, sampler.Sample(biome, GateCentre(gate), size, step)));
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

    /// <summary>The tile at the middle of a gate's hole.</summary>
    private static Vector2i GateCentre(WFCavernMouth gate)
    {
        return gate.Origin + new Vector2i(gate.Size / 2, gate.Size / 2);
    }
}
