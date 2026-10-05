#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server.Atmos.Components;
using Content.Server.Decals;
using Content.Server.Parallax;
using Content.Shared._WF.CCVar;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// Planet terrain nobody is near unloads after its idle time and comes back as it was, while the ground under a hull,
/// beside something built, or loaded by hand stays.
/// </summary>
[TestFixture]
[TestOf(typeof(BiomeSystem))]
public sealed class TerrainUnloadTest
{
    private const string Surface = "WFSurfaceMerak";

    /// <summary>Seconds of idle the tests give a chunk, in place of the default three minutes.</summary>
    private const float Idle = 1f;

    /// <summary>Long enough for the idle time, a scan, and every chunk a viewer loads to be unloaded in turn.</summary>
    private const float Settle = 40f;

    /// <summary>Where the viewer starts, and where it goes: far enough that no chunk is near both.</summary>
    private static readonly Vector2i Home = new(200, 40);
    private static readonly Vector2i Away = new(600, 40);

    /// <summary>
    /// The ground a viewer walks away from unloads, the ground it walks to loads, and back home the same terrain is
    /// there again. With the switch off nothing unloads.
    /// </summary>
    [Test]
    public async Task GroundNobodyIsNearUnloadsAndComesBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();

        await Setup(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(Home));
            await pair.RunTicksSync(pair.SecondsToTicks(2f));

            var homeChunk = ChunkOf(Home);
            var first = await Count(pair, world.Ground, Home);
            Assert.That(first.Loaded, Is.True, "Precondition: the ground under the viewer did not load.");
            Assert.That(first.Tiles, Is.GreaterThan(0));

            // The switch off: walking away unloads nothing.
            await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.TerrainUnload, false));
            await Move(pair, viewer, world.Ground, Away);
            await pair.RunTicksSync(pair.SecondsToTicks(Settle));
            Assert.That((await Count(pair, world.Ground, Home)).Loaded, Is.True, "Terrain unloaded with the switch off.");

            await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.TerrainUnload, true));
            await pair.RunTicksSync(pair.SecondsToTicks(Settle));

            var gone = await Count(pair, world.Ground, Home);
            var there = await Count(pair, world.Ground, Away);

            Assert.Multiple(() =>
            {
                Assert.That(gone.Loaded, Is.False, "The ground the viewer left is still loaded.");
                Assert.That(gone.Tiles, Is.LessThan(first.Tiles), "The ground the viewer left kept all its tiles.");
                Assert.That(there.Loaded, Is.True, "The ground under the viewer is not loaded.");
            });

            await server.WaitAssertion(() =>
            {
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                var ring = ChunkOf(Away) + new Vector2i((4 + BiomeSystem.WfKeepRing) * ChunkSize, 0);
                var past = ring + new Vector2i(2 * ChunkSize, 0);

                Assert.That(biomes.WfIsChunkLoaded(biome, ChunkOf(Away)), Is.True);
                Assert.That(biomes.WfIsChunkLoaded(biome, past), Is.False, "A chunk well past the kept ring is loaded.");
            });

            await Move(pair, viewer, world.Ground, Home);
            await pair.RunTicksSync(pair.SecondsToTicks(2f));

            var back = await Count(pair, world.Ground, Home);
            Assert.Multiple(() =>
            {
                Assert.That(back.Loaded, Is.True, "The ground did not load again when the viewer came back.");
                Assert.That(back.Tiles, Is.EqualTo(first.Tiles), "The ground came back with different tiles.");
                Assert.That(back.Entities, Is.EqualTo(first.Entities), "The ground came back with different things on it.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ghost loads no ground, but the ground it is watching stays; what it leaves behind goes.
    /// </summary>
    [Test]
    public async Task AGhostKeepsTheGroundItWatchesAndLoadsNone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();

        await Setup(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var body = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(Home));
            await pair.RunTicksSync(pair.SecondsToTicks(2f));

            var ghost = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(Home), "MobObserver");
            await server.WaitPost(() => entMan.DeleteEntity(body));
            await pair.RunTicksSync(pair.SecondsToTicks(Settle));

            await server.WaitAssertion(() =>
            {
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                Assert.That(biomes.WfIsChunkLoaded(biome, Home), Is.True, "The ground a ghost is watching unloaded.");
            });

            await Move(pair, ghost, world.Ground, Away);
            await pair.RunTicksSync(pair.SecondsToTicks(Settle));

            await server.WaitAssertion(() =>
            {
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));

                Assert.Multiple(() =>
                {
                    Assert.That(biomes.WfIsChunkLoaded(biome, Home), Is.False, "The ground a ghost left stayed loaded.");
                    Assert.That(biomes.WfIsChunkLoaded(biome, Away), Is.False, "A ghost loaded ground.");
                });
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A cavern chunk's untouched rock goes when it unloads and the same rock is back when it loads. A wall someone
    /// has damaged, one with something fixed to its tile, one on a pinned tile and one with a mark drawn on its tile
    /// stay through both, with nothing grown on top of them, and the mark stays too.
    /// </summary>
    [Test]
    public async Task UntouchedRockUnloadsAndTouchedRockStays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var biomes = server.System<BiomeSystem>();
        var decals = server.System<DecalSystem>();

        await Setup(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var from = Home - new Vector2i(8, 8);
            var to = Home + new Vector2i(15, 15);
            await LoadChunks(pair, world.Cavern, from, to);
            await server.WaitRunTicks(pair.SecondsToTicks(3f));

            var loaded = 0;
            var damaged = EntityUid.Invalid;
            var mounted = EntityUid.Invalid;
            var pinned = EntityUid.Invalid;
            var marked = EntityUid.Invalid;
            var damagedTile = Vector2i.Zero;
            var mountedTile = Vector2i.Zero;
            var pinnedTile = Vector2i.Zero;
            var markedTile = Vector2i.Zero;
            var mark = 0u;

            await server.WaitPost(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var rocks = Rocks(entMan, maps, world.Cavern, grid, from, to);
                loaded = rocks.Count;

                if (rocks.Count < 8)
                    return;

                (damaged, damagedTile) = rocks[0];
                (mounted, mountedTile) = rocks[^1];
                (pinned, pinnedTile) = rocks[rocks.Count / 2];
                (marked, markedTile) = rocks[rocks.Count / 4];

                biomes.WfPinTiles((world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern)), new[] { pinnedTile });
                Assert.That(decals.TryAddDecal("Dirt", new EntityCoordinates(world.Cavern, markedTile), out mark), Is.True,
                    "Precondition: the mark could not be drawn.");
                Assert.That(Marks(decals, world.Cavern, markedTile), Does.Contain(mark), "Precondition: the drawn mark is not on its tile.");

                var chip = new DamageSpecifier();
                chip.DamageDict.Add("Blunt", 1);
                server.System<DamageableSystem>().TryChangeDamage(damaged, chip, true);
                entMan.SpawnEntity("CableApcExtension", maps.GridTileToLocal(world.Cavern, grid, mountedTile));
            });

            Assert.That(loaded, Is.GreaterThan(100), "Precondition: the cavern here has too little rock to tell.");
            await server.WaitRunTicks(5);
            await UnloadChunks(pair, world.Cavern, from, to);

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var left = Rocks(entMan, maps, world.Cavern, grid, from, to);

                Assert.Multiple(() =>
                {
                    Assert.That(left.Count, Is.LessThan(loaded / 4), $"Unloading kept {left.Count} of {loaded} rock walls.");
                    Assert.That(entMan.Deleted(damaged), Is.False, "A damaged wall was unloaded.");
                    Assert.That(entMan.Deleted(mounted), Is.False, "A wall with a cable on its tile was unloaded.");
                    Assert.That(entMan.Deleted(pinned), Is.False, "A wall on a pinned tile was unloaded, and would never grow back.");
                    Assert.That(entMan.Deleted(marked), Is.False, "A wall on a tile with a mark drawn on it was unloaded.");
                    Assert.That(Marks(decals, world.Cavern, markedTile), Does.Contain(mark), "A drawn mark went with its chunk.");
                    Assert.That(maps.GetTileRef(world.Cavern, grid, damagedTile).Tile.IsEmpty, Is.False, "The floor under a kept wall went.");
                });
            });

            await LoadChunks(pair, world.Cavern, from, to);

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);

                Assert.Multiple(() =>
                {
                    Assert.That(Rocks(entMan, maps, world.Cavern, grid, from, to).Count, Is.EqualTo(loaded),
                        "The cavern came back with a different number of rock walls.");
                    Assert.That(Rocks(entMan, maps, world.Cavern, grid, damagedTile, damagedTile).Count, Is.EqualTo(1),
                        "A second wall grew on a kept one.");
                    Assert.That(Rocks(entMan, maps, world.Cavern, grid, mountedTile, mountedTile).Count, Is.EqualTo(1),
                        "A second wall grew on a kept one.");
                    Assert.That(Rocks(entMan, maps, world.Cavern, grid, pinnedTile, pinnedTile).Count, Is.EqualTo(1),
                        "A pinned tile's wall is gone, or has a second on it.");
                    Assert.That(Marks(decals, world.Cavern, markedTile), Does.Contain(mark), "A drawn mark did not last the reload.");
                    Assert.That(biomes.WfIsBiomeSpawned((world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern)), damaged, damagedTile),
                        Is.True, "A kept wall no longer reads as the biome's own.");
                });
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// With the viewer gone, the ground under a parked hull stays, the ground round a floor someone laid stays, and a
    /// chunk loaded by hand stays; the ground between them goes.
    /// </summary>
    [Test]
    public async Task HullsBuildsAndHeldChunksKeepTheirGround()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        var hullAt = Home + new Vector2i(-20, 0);
        var builtAt = Home + new Vector2i(20, 0);
        var heldAt = Home + new Vector2i(0, 20);
        var plain = Home + new Vector2i(0, -24);

        await Setup(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(Home));
            await pair.RunTicksSync(pair.SecondsToTicks(2f));

            var hull = await PlanetFixture.BuildDebris(pair, await MapIdOf(pair, world.Ground), 3, new Vector2(hullAt.X, hullAt.Y));
            await server.WaitPost(() =>
            {
                server.System<SharedPhysicsSystem>().SetBodyType(hull, BodyType.Static);
                server.System<SharedTransformSystem>().SetLocalPosition(hull, new Vector2(hullAt.X, hullAt.Y));

                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                maps.SetTile(world.Ground, grid, builtAt, new Tile(tileDefs["Plating"].TileId));
            });
            await LoadChunks(pair, world.Ground, heldAt, heldAt);
            await pair.RunTicksSync(pair.SecondsToTicks(2f));

            await server.WaitAssertion(() =>
            {
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                var simulated = entMan.GetComponent<GridAtmosphereComponent>(world.Ground).Tiles;
                Assert.That(simulated.ContainsKey(builtAt), Is.True,
                    "Precondition: the laid floor is not simulated by the ground's atmosphere.");

                foreach (var spot in new[] { hullAt, builtAt, heldAt, plain })
                {
                    Assert.That(biomes.WfIsChunkLoaded(biome, spot), Is.True, $"Precondition: the ground at {spot} is not loaded.");
                }
            });

            await Move(pair, viewer, world.Ground, Away);
            await pair.RunTicksSync(pair.SecondsToTicks(Settle));

            await server.WaitAssertion(() =>
            {
                var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);

                Assert.Multiple(() =>
                {
                    Assert.That(biomes.WfIsChunkLoaded(biome, plain), Is.False, "Plain ground nobody is near stayed loaded.");
                    Assert.That(biomes.WfIsChunkLoaded(biome, hullAt), Is.True, "The ground under a parked hull unloaded.");
                    Assert.That(maps.GetTileRef(world.Ground, grid, hullAt + new Vector2i(-2, 1)).Tile.IsEmpty, Is.False,
                        "The ground beside a parked hull unloaded.");
                    Assert.That(biomes.WfIsChunkLoaded(biome, builtAt), Is.True, "The ground round a laid floor unloaded.");
                    Assert.That(maps.GetTileRef(world.Ground, grid, builtAt + new Vector2i(1, 0)).Tile.IsEmpty, Is.False,
                        "The natural ground beside a laid floor unloaded.");
                    Assert.That(biomes.WfIsChunkLoaded(biome, heldAt), Is.True, "A chunk loaded by hand unloaded.");
                });
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Turns planets and caverns on, stops lazy mouth claims, and shortens the idle time.</summary>
    private static async Task Setup(TestPair pair)
    {
        await EnableCaverns(pair);
        await DisableClaims(pair);
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.TerrainUnloadIdle, Idle));
    }

    /// <summary>The origin of the chunk holding a tile.</summary>
    private static Vector2i ChunkOf(Vector2i tile)
    {
        return SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
    }

    /// <summary>Puts the viewer on a ground tile.</summary>
    private static async Task Move(TestPair pair, EntityUid viewer, EntityUid map, Vector2i tile)
    {
        await pair.Server.WaitPost(() =>
            pair.Server.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(map, TileCentre(tile))));
        await pair.RunTicksSync(2);
    }

    /// <summary>Whether a tile's chunk is loaded, and the tiles and anchored entities in the 24 tiles square round it.</summary>
    private static async Task<(bool Loaded, int Tiles, int Entities)> Count(TestPair pair, EntityUid map, Vector2i centre)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var result = (false, 0, 0);

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(map);
            var biome = (map, entMan.GetComponent<BiomeComponent>(map));
            var tiles = 0;
            var entities = 0;

            for (var x = -12; x < 12; x++)
            for (var y = -12; y < 12; y++)
            {
                var index = centre + new Vector2i(x, y);

                if (maps.TryGetTileRef(map, grid, index, out var tile) && !tile.Tile.IsEmpty)
                    tiles++;

                entities += maps.GetAnchoredEntities(map, grid, index).Count();
            }

            result = (server.System<BiomeSystem>().WfIsChunkLoaded(biome, centre), tiles, entities);
        });

        return result;
    }

    /// <summary>The ids of the decals on a tile of a map.</summary>
    private static List<uint> Marks(DecalSystem decals, EntityUid map, Vector2i tile)
    {
        return decals.GetDecalsIntersecting(map, new Box2(tile - new Vector2(0.01f, 0.01f), tile + new Vector2(0.99f, 0.99f)))
            .Select(d => d.Index)
            .ToList();
    }

    /// <summary>Every anchored wall in a tile rectangle of a map, corners included, with its tile.</summary>
    private static List<(EntityUid Uid, Vector2i Tile)> Rocks(IEntityManager entMan, SharedMapSystem maps, EntityUid map, MapGridComponent grid, Vector2i from, Vector2i to)
    {
        var found = new List<(EntityUid, Vector2i)>();

        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            var index = new Vector2i(x, y);

            foreach (var uid in maps.GetAnchoredEntities(map, grid, index))
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID.StartsWith("WallRock") == true)
                    found.Add((uid, index));
            }
        }

        return found;
    }
}
