#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Caverns;
using Content.Server.Parallax;
using Content.Shared._WF.Caverns;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Every world gets a gate at build: a pinned hole with shades, a solid lip, a rock-free pad and a climb point.</summary>
[TestFixture]
[TestOf(typeof(WFCavernMouthSystem))]
public sealed class CavernMouthTest
{
    /// <summary>Section 4.8: what each world's shafts report about the air below.</summary>
    private static readonly Dictionary<string, WFCavernAir> ExpectedAir = new()
    {
        { "WFSurfaceAsclepiu", WFCavernAir.Breathable },
        { "WFSurfaceFervidus", WFCavernAir.Scalding },
        { "WFSurfaceMerak", WFCavernAir.Breathable },
        { "WFSurfaceAerumna", WFCavernAir.Toxic },
        { "WFSurfaceThrascias", WFCavernAir.Freezing },
        { "WFSurfaceCarcinoma", WFCavernAir.Foul },
    };

    /// <summary>Section 3.5: each world's landing tile fall multiplier.</summary>
    private static readonly Dictionary<string, float> ExpectedLanding = new()
    {
        { "WFSurfaceAsclepiu", 0f },
        { "WFSurfaceFervidus", 0.75f },
        { "WFSurfaceMerak", 0.5f },
        { "WFSurfaceAerumna", 1.5f },
        { "WFSurfaceThrascias", 0.25f },
        { "WFSurfaceCarcinoma", 0.4f },
    };

    /// <summary>Section 3.6: each world's climb-up time in seconds.</summary>
    private static readonly Dictionary<string, float> ExpectedClimb = new()
    {
        { "WFSurfaceAsclepiu", 4f },
        { "WFSurfaceFervidus", 4f },
        { "WFSurfaceMerak", 4.6f },
        { "WFSurfaceAerumna", 10f },
        { "WFSurfaceThrascias", 5f },
        { "WFSurfaceCarcinoma", 4f },
    };

    /// <summary>Each world, built and torn down in turn, has a fully fitted gate as soon as it is built.</summary>
    [Test]
    public async Task GateExists()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await EnableCaverns(pair);

        foreach (var surfaceId in Surfaces)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var cavern = CavernOf(pair, surfaceId);
                var gate = await Gate(pair, world);

                await AssertGateFitted(pair, world, cavern, gate, surfaceId);

                // A cavern viewer's load brings rock around the pad, never onto it.
                var spec = cavern.Mouths;
                await LoadChunks(pair, world.Cavern, gate.Origin - new Vector2i(spec.PadRadius, spec.PadRadius),
                    gate.Origin + new Vector2i(spec.HoleSize + spec.PadRadius, spec.HoleSize + spec.PadRadius));

                await server.WaitAssertion(() => AssertPadClear(pair, world, cavern, gate, surfaceId));
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Unloading and reloading both maps' chunks leaves the gate's tiles and entities exactly as they were.</summary>
    [Test]
    public async Task GateSurvivesUnloadReload()
    {
        const string surfaceId = "WFSurfaceFervidus";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var cavern = CavernOf(pair, surfaceId);
            var gate = await Gate(pair, world);
            var spec = cavern.Mouths;
            var from = gate.Origin - new Vector2i(spec.PadRadius + 1, spec.PadRadius + 1);
            var to = gate.Origin + new Vector2i(spec.HoleSize + spec.PadRadius + 1, spec.HoleSize + spec.PadRadius + 1);

            await LoadChunks(pair, world.Ground, from, to);
            await LoadChunks(pair, world.Cavern, from, to);

            Snapshot? before = null;
            await server.WaitPost(() => before = Snap(pair, world, gate, spec));

            Assert.That(before!.Rim, Is.Not.Empty, "Precondition: the Fervidus gate has no rim decor to keep track of.");

            await UnloadChunks(pair, world.Ground, from, to);
            await UnloadChunks(pair, world.Cavern, from, to);

            await server.WaitAssertion(() =>
            {
                var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                Assert.That(ChunkOrigins(from, to).Any(chunk => biomes.WfIsChunkLoaded(groundBiome, chunk)), Is.False,
                    "Precondition: a ground chunk under the gate is still loaded.");
                Snap(pair, world, gate, spec).AssertSame(before, "after the unload");
            });

            await LoadChunks(pair, world.Ground, from, to);
            await LoadChunks(pair, world.Cavern, from, to);

            await server.WaitAssertion(() =>
            {
                Snap(pair, world, gate, spec).AssertSame(before, "after the reload");
                AssertPadClear(pair, world, cavern, gate, surfaceId);
            });

            await AssertGateFitted(pair, world, cavern, gate, surfaceId);
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The gate's hole, lip, pad, shades and climb point are as section 3.3 stamps them.</summary>
    private static async Task AssertGateFitted(TestPair pair, World world, WFCavernPrototype cavern, WFCavernMouth gate, string surfaceId)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var spec = cavern.Mouths;

        await server.WaitAssertion(() =>
        {
            var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
            var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
            var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
            var levelBiome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
            var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(gate.Kind, Is.EqualTo(WFCavernMouthKind.Gate), $"{surfaceId}: the gate has the wrong kind.");
                Assert.That(gate.Size, Is.EqualTo(spec.HoleSize), $"{surfaceId}: the gate hole has the wrong size.");
                Assert.That(ground.Mouths.Count(mouth => mouth.Kind == WFCavernMouthKind.Gate), Is.EqualTo(1),
                    $"{surfaceId}: the ground should have exactly one gate.");
                Assert.That(gate.ClimbTile, Is.EqualTo(WFCavernMouthSystem.ClimbTile(gate.Origin, gate.Size, spec.ClimbSide)),
                    $"{surfaceId}: the climb tile is not the lip tile on the climb side.");

                foreach (var index in WFCavernMouthSystem.Hole(gate.Origin, gate.Size))
                {
                    Assert.That(biomes.WfIsPinned(groundBiome, index), Is.True, $"{surfaceId}: hole tile {index} is not pinned.");
                    Assert.That(TileAt(maps, world.Ground, groundGrid, index).IsEmpty, Is.True,
                        $"{surfaceId}: hole tile {index} is not empty.");
                    Assert.That(ground.Shades.TryGetValue(index, out var shade), Is.True, $"{surfaceId}: hole tile {index} has no shade.");

                    var xform = entMan.GetComponent<TransformComponent>(shade);
                    Assert.That(xform.MapUid, Is.EqualTo(world.Ground), $"{surfaceId}: the shade over {index} is not on the ground.");
                    Assert.That(xform.Anchored, Is.False, $"{surfaceId}: the shade over {index} is anchored.");
                    Assert.That(maps.TileIndicesFor(world.Ground, groundGrid, xform.Coordinates), Is.EqualTo(index),
                        $"{surfaceId}: the shade over {index} sits on another tile.");

                    var shaft = entMan.GetComponent<WFCavernShaftComponent>(shade);
                    Assert.That(shaft.Cavern?.Id, Is.EqualTo(cavern.ID), $"{surfaceId}: the shade names the wrong cavern.");
                    Assert.That(shaft.Air, Is.EqualTo(ExpectedAir[surfaceId]), $"{surfaceId}: the shade reports the wrong air.");
                    Assert.That(shaft.LandingMultiplier, Is.EqualTo(ExpectedLanding[surfaceId]).Within(0.001f),
                        $"{surfaceId}: the shade reports the wrong landing.");

                    var landing = TileAt(maps, world.Cavern, levelGrid, index);
                    Assert.That(tileDefs[landing.TypeId].ID, Is.EqualTo(spec.LandingTile.Id),
                        $"{surfaceId}: cavern tile {index} under the hole is not the landing tile.");
                }

                foreach (var index in WFCavernMouthSystem.Ring(gate.Origin, gate.Size))
                {
                    Assert.That(biomes.WfIsPinned(groundBiome, index), Is.True, $"{surfaceId}: lip tile {index} is not pinned.");
                    Assert.That(TileAt(maps, world.Ground, groundGrid, index).IsEmpty, Is.False,
                        $"{surfaceId}: lip tile {index} is empty.");
                }

                foreach (var index in WFCavernMouthSystem.Pad(gate.Origin, gate.Size, spec.PadRadius))
                {
                    Assert.That(biomes.WfIsPinned(levelBiome, index), Is.True, $"{surfaceId}: pad tile {index} is not pinned.");
                    Assert.That(TileAt(maps, world.Cavern, levelGrid, index).IsEmpty, Is.False,
                        $"{surfaceId}: pad tile {index} is empty.");
                }

                Assert.That(ground.ClimbPoints.TryGetValue(gate.ClimbTile, out var climb), Is.True,
                    $"{surfaceId}: the gate has no climb point.");

                var climbXform = entMan.GetComponent<TransformComponent>(climb);
                Assert.That(climbXform.MapUid, Is.EqualTo(world.Cavern), $"{surfaceId}: the climb point is not in the cavern.");
                Assert.That(climbXform.Anchored, Is.True, $"{surfaceId}: the climb point is not anchored.");
                Assert.That(maps.TileIndicesFor(world.Cavern, levelGrid, climbXform.Coordinates), Is.EqualTo(gate.ClimbTile),
                    $"{surfaceId}: the climb point is not under the climb tile.");
                Assert.That(entMan.GetComponent<WFCavernClimbComponent>(climb).Delay, Is.EqualTo(ExpectedClimb[surfaceId]).Within(0.01f),
                    $"{surfaceId}: the climb point has the wrong climb time.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(climb).EntityPrototype?.ID, Is.EqualTo(spec.ClimbPoint.Id),
                    $"{surfaceId}: the climb point is the wrong entity.");
            }
        });
    }

    /// <summary>Fails if anything but the climb point is anchored on the pad.</summary>
    private static void AssertPadClear(TestPair pair, World world, WFCavernPrototype cavern, WFCavernMouth gate, string surfaceId)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();
        var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
        var levelBiome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
        var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
        var climb = ground.ClimbPoints[gate.ClimbTile];

        Assert.That(pair.Server.System<BiomeSystem>().WfIsChunkLoaded(levelBiome, gate.Origin), Is.True,
            $"Precondition: {surfaceId}'s cavern chunk under the gate never loaded.");

        using (Assert.EnterMultipleScope())
        {
            foreach (var index in WFCavernMouthSystem.Pad(gate.Origin, gate.Size, cavern.Mouths.PadRadius))
            {
                foreach (var anchored in maps.GetAnchoredEntities(world.Cavern, levelGrid, index))
                {
                    if (anchored == climb)
                        continue;

                    Assert.Fail($"{surfaceId}: {entMan.ToPrettyString(anchored)} stands on pad tile {index}.");
                }
            }
        }
    }

    /// <summary>A map's tile at an index, empty where there is none.</summary>
    private static Tile TileAt(SharedMapSystem maps, EntityUid map, MapGridComponent grid, Vector2i index)
    {
        return maps.TryGetTileRef(map, grid, index, out var tile) ? tile.Tile : Tile.Empty;
    }

    /// <summary>Takes down the gate's tiles on both maps and the entities stamped with it.</summary>
    private static Snapshot Snap(TestPair pair, World world, WFCavernMouth gate, WFCavernMouthSpec spec)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();
        var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
        var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
        var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
        var snapshot = new Snapshot();

        foreach (var index in WFCavernMouthSystem.Footprint(gate.Origin, gate.Size))
        {
            snapshot.Ground[index] = TileAt(maps, world.Ground, groundGrid, index);

            foreach (var anchored in maps.GetAnchoredEntities(world.Ground, groundGrid, index))
            {
                snapshot.Rim.Add(anchored);
            }
        }

        foreach (var index in WFCavernMouthSystem.Pad(gate.Origin, gate.Size, spec.PadRadius))
        {
            snapshot.Pad[index] = TileAt(maps, world.Cavern, levelGrid, index);
        }

        foreach (var uid in ground.Shades.Values.Concat(ground.ClimbPoints.Values).Concat(snapshot.Rim))
        {
            snapshot.Entities[uid] = entMan.EntityExists(uid)
                ? entMan.GetComponent<TransformComponent>(uid).Coordinates
                : EntityCoordinates.Invalid;
        }

        return snapshot;
    }

    /// <summary>The gate's tiles and stamped entities at one moment.</summary>
    private sealed class Snapshot
    {
        public readonly Dictionary<Vector2i, Tile> Ground = new();
        public readonly Dictionary<Vector2i, Tile> Pad = new();
        public readonly List<EntityUid> Rim = new();
        public readonly Dictionary<EntityUid, EntityCoordinates> Entities = new();

        /// <summary>Fails on any tile or entity that differs from an earlier snapshot.</summary>
        public void AssertSame(Snapshot? before, string when)
        {
            Assert.That(before, Is.Not.Null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Ground, Is.EquivalentTo(before!.Ground), $"The gate's ground tiles changed {when}.");
                Assert.That(Pad, Is.EquivalentTo(before.Pad), $"The gate's pad tiles changed {when}.");
                Assert.That(Rim, Is.EquivalentTo(before.Rim), $"The gate's rim decor changed {when}.");
                Assert.That(Entities, Is.EquivalentTo(before.Entities), $"The gate's shades, climb point or rim moved {when}.");
            }
        }
    }
}
