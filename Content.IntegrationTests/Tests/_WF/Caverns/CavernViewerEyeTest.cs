#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._CE.ZLevels.Core;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>The eye cap: nobody on or above the ground loads the cavern, and a cavern viewer keeps the ground above loaded.</summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class CavernViewerEyeTest
{
    /// <summary>The z-level eye prototype UpdateViewer spawns on each map it looks into.</summary>
    private const string EyeProto = "CEZLevelEye";

    /// <summary>Where the viewer stands, well away from the planet centre.</summary>
    private static readonly Vector2 ViewerPos = new(40.5f, 40.5f);

    /// <summary>A ground viewer has no eye on the cavern, and no cavern chunk loads for 60 ticks.</summary>
    [Test]
    public async Task GroundViewerLoadsNoCavern()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        // Terrain under the viewer before it spawns, so it stands on ground from its first tick.
        var tile = new Vector2i((int) ViewerPos.X, (int) ViewerPos.Y);
        await LoadChunks(pair, world.Ground, tile - new Vector2i(ChunkSize, ChunkSize), tile + new Vector2i(ChunkSize, ChunkSize));

        var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, ViewerPos);
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var cavernBiome = entMan.GetComponent<BiomeComponent>(world.Cavern);
            var groundBiome = entMan.GetComponent<BiomeComponent>(world.Ground);
            // Past the hand-loaded patch, so only the viewer's own loader can have loaded it.
            var farChunk = SharedMapSystem.GetChunkIndices(tile + new Vector2i(3 * ChunkSize, 0), ChunkSize) * ChunkSize;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Ground),
                    "Precondition: the viewer left the ground.");
                Assert.That(EyesOn(entMan, world.Cavern), Is.Empty, "A ground viewer has an eye on the cavern below it.");
                Assert.That(EyesOn(entMan, world.Layers[1]), Is.Not.Empty,
                    "Precondition: the ground viewer has no eye on the air layer above it.");
                Assert.That(groundBiome.LoadedChunks, Does.Contain(farChunk),
                    "Precondition: the viewer's own chunk loading never ran.");
                Assert.That(cavernBiome.LoadedChunks, Is.Empty, "A ground viewer loaded cavern chunks.");
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>A cavern viewer has an eye on the ground above it, and the ground chunks over it load.</summary>
    [Test]
    public async Task CavernViewerLoadsGroundAbove()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        var viewer = await PlanetFixture.AttachViewer(pair, world.Cavern, ViewerPos);
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var groundBiome = entMan.GetComponent<BiomeComponent>(world.Ground);
            var tile = new Vector2i((int) ViewerPos.X, (int) ViewerPos.Y);
            var overhead = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Cavern),
                    "Precondition: the viewer left the cavern.");
                Assert.That(EyesOn(entMan, world.Ground), Is.Not.Empty, "A cavern viewer has no eye on the ground above it.");
                Assert.That(groundBiome.LoadedChunks, Does.Contain(overhead),
                    "The ground chunk over a cavern viewer never loaded, so nothing roofs it.");
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>Every z-level eye standing on a map.</summary>
    private static List<EntityUid> EyesOn(IEntityManager entMan, EntityUid map)
    {
        var found = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID == EyeProto && xform.MapUid == map)
                found.Add(uid);
        }

        return found;
    }
}
