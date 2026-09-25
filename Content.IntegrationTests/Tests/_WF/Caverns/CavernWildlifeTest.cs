#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server.Parallax;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Surface wildlife dropped into a cavern by an unloaded chunk is removed; wildlife that fell through a hole stays.</summary>
[TestFixture]
[TestOf(typeof(WFCavernSystem))]
public sealed class CavernWildlifeTest
{
    /// <summary>Stands in for an animal; any unanchored mob with z-physics falls the same way.</summary>
    private const string MobProto = "MobHuman";

    /// <summary>The tile the wildlife stands on, well away from the planet centre.</summary>
    private static readonly Vector2i Spot = new(204, 204);

    /// <summary>The patch of ground loaded around it: the spot's chunk and its neighbours.</summary>
    private static readonly Vector2i From = Spot - new Vector2i(ChunkSize, ChunkSize);
    private static readonly Vector2i To = Spot + new Vector2i(ChunkSize, ChunkSize);

    /// <summary>An awake animal whose ground chunk unloads falls into the cavern and is deleted; a mob that isn't wildlife stays.</summary>
    [Test]
    public async Task WildlifeOverUnloadedGroundIsRemoved()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");
        await LoadChunks(pair, world.Ground, From, To);

        var animal = await SpawnMob(pair, world, Spot, wildlife: true);
        var control = await SpawnMob(pair, world, Spot + new Vector2i(1, 0), wildlife: false);
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(animal).MapUid, Is.EqualTo(world.Ground),
                    "Precondition: the animal is not standing on the ground.");
                Assert.That(entMan.GetComponent<TransformComponent>(control).MapUid, Is.EqualTo(world.Ground),
                    "Precondition: the control mob is not standing on the ground.");
            }
        });

        await UnloadChunks(pair, world.Ground, From, To);
        await Wake(pair, animal, control);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(control).MapUid, Is.EqualTo(world.Cavern),
                    "Precondition: a mob over the unloaded chunk did not fall into the cavern.");
                Assert.That(entMan.Deleted(animal) || entMan.IsQueuedForDeletion(animal), Is.True,
                    $"Wildlife that fell into the cavern over unloaded ground lingers on {entMan.ToPrettyString(entMan.GetComponentOrNull<TransformComponent>(animal)?.MapUid)}.");
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>An animal that falls through a pinned hole into the cavern is kept, even with its chunk unloaded.</summary>
    [Test]
    public async Task WildlifeThroughPinnedHoleIsKept()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");
        await LoadChunks(pair, world.Ground, From, To);

        var animal = await SpawnMob(pair, world, Spot, wildlife: true);
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        // Pinned, so the unload keeps its tile and the animal on it; the hole is opened by hand after.
        await server.WaitPost(() =>
            biomes.WfPinTiles((world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground)), new[] { Spot }));
        await UnloadChunks(pair, world.Ground, From, To);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(animal).MapUid, Is.EqualTo(world.Ground),
                "Precondition: the animal on its pinned tile fell when the chunk unloaded."));

        await server.WaitPost(() =>
            maps.SetTile(world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), Spot, Tile.Empty));
        await Wake(pair, animal);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(animal) || entMan.IsQueuedForDeletion(animal), Is.False,
                "Wildlife that fell through a pinned hole was deleted.");
            Assert.That(entMan.GetComponent<TransformComponent>(animal).MapUid, Is.EqualTo(world.Cavern),
                "Precondition: the animal did not fall through the hole into the cavern.");
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns a mob on a ground tile's centre, marked as the ground's wildlife if asked.</summary>
    private static async Task<EntityUid> SpawnMob(TestPair pair, World world, Vector2i tile, bool wildlife)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mob = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity(MobProto, new EntityCoordinates(world.Ground, new Vector2(tile.X + 0.5f, tile.Y + 0.5f)));

            if (wildlife)
                entMan.EnsureComponent<WFPlanetWildlifeComponent>(mob).Ground = world.Ground;
        });

        await server.WaitRunTicks(1);
        return mob;
    }

    /// <summary>Wakes z-physics bodies, as a wandering animal would be; a sleeping one never notices its ground go.</summary>
    private static async Task Wake(TestPair pair, params EntityUid[] mobs)
    {
        var levels = pair.Server.System<CEZLevelsSystem>();

        await pair.Server.WaitPost(() =>
        {
            foreach (var mob in mobs)
            {
                levels.WakeBody(mob);
            }
        });
    }
}
