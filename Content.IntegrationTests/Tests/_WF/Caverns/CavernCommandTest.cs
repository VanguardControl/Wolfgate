#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Server.Parallax;
using Content.Shared._WF.Administration;
using Content.Shared.Parallax.Biomes;
using Robust.Client.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>The wfcavern admin command: list, tp, mouths and open.</summary>
[TestFixture]
[TestOf(typeof(WFCavernCommand))]
public sealed class CavernCommandTest
{
    /// <summary>How far east of the gate the admin carves a mouth by hand.</summary>
    private static readonly Vector2i OpenOffset = new(16, 0);

    /// <summary>list shows every world, tp lands on the gate pad, mouths lists the gate and open carves a mouth.</summary>
    [Test]
    public async Task ListTpMouthsOpen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var biomes = server.System<BiomeSystem>();
        var console = pair.Client.ResolveDependency<IClientConsoleHost>();
        var output = new List<string>();

        EventHandler<AddStringArgs> capture = (_, args) =>
        {
            // Local lines are the client's own echo; the server's replies arrive as acks.
            if (!args.Local)
                output.Add(args.Text.ToString());
        };

        await EnableCaverns(pair);

        var worlds = new List<World>();
        await pair.Client.WaitPost(() => console.AddString += capture);

        try
        {
            foreach (var surfaceId in Surfaces)
            {
                worlds.Add(await BuildWorld(pair, surfaceId));
            }

            var list = await Run(pair, output, "list");
            Assert.That(list.Count(line => line.Contains("cavern:")), Is.EqualTo(Surfaces.Length),
                $"list should print one row per world:\n{string.Join('\n', list)}");
            Assert.That(list.Where(line => line.Contains("cavern:")).All(line => line.Contains("mouths: 1")), Is.True,
                $"Every freshly built world should have exactly its gate:\n{string.Join('\n', list)}");

            var world = worlds[0];
            var gate = await Gate(pair, world);
            var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(ClimbTile(gate)));

            var tp = await Run(pair, output, "tp Asclepiu");
            await server.WaitAssertion(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(viewer);
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);

                Assert.That(xform.MapUid, Is.EqualTo(world.Cavern), $"tp did not move the caller into the cavern:\n{string.Join('\n', tp)}");
                Assert.That(maps.TileIndicesFor(world.Cavern, grid, xform.Coordinates), Is.EqualTo(ClimbTile(gate)),
                    "tp did not land the caller on the gate pad beside the climb point.");
            });

            var mouths = await Run(pair, output, "mouths Asclepiu");
            Assert.That(mouths.Any(line => line.Contains("Gate") && line.Contains(gate.Origin.ToString())), Is.True,
                $"mouths does not list the gate at {gate.Origin}:\n{string.Join('\n', mouths)}");

            await Run(pair, output, "tp Asclepiu mouth");
            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Ground),
                    "tp ... mouth did not put the caller on the ground beside the gate."));

            // The ground around the gate is loaded by now, so the carve takes the loaded-chunk path. Outcrop walls come
            // from self-deleting spawners the biome no longer tracks, so open treats them as built: pick a clear patch.
            await pair.RunTicksSync(10);
            var spot = gate.Origin + OpenOffset;
            await server.WaitPost(() => spot = FindClearPatch(pair, world, gate.Origin + OpenOffset, gate.Size));
            await server.WaitPost(() =>
                server.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(world.Ground, TileCentre(spot))));
            await pair.RunTicksSync(2);

            var open = await Run(pair, output, "open");
            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));

                Assert.That(biomes.WfIsChunkLoaded(groundBiome, spot), Is.True,
                    "Precondition: the ground under the carve was not loaded.");
                Assert.That(ground.Mouths.Any(mouth => mouth.Kind == WFCavernMouthKind.Admin && mouth.Origin == spot), Is.True,
                    $"open made no admin mouth at {spot}:\n{string.Join('\n', open)}");

                using (Assert.EnterMultipleScope())
                {
                    foreach (var index in WFCavernMouthSystem.Hole(spot, gate.Size))
                    {
                        Assert.That(maps.TryGetTileRef(world.Ground, grid, index, out var tile) && !tile.Tile.IsEmpty, Is.False,
                            $"open left hole tile {index} solid.");
                        Assert.That(biomes.WfIsPinned(groundBiome, index), Is.True, $"open left hole tile {index} unpinned.");
                        Assert.That(ground.Shades.ContainsKey(index), Is.True, $"open left hole tile {index} without a shade.");
                    }

                    foreach (var index in WFCavernMouthSystem.Footprint(spot, gate.Size))
                    {
                        Assert.That(maps.GetAnchoredEntities(world.Ground, grid, index), Is.Empty,
                            $"open left the biome's entities on footprint tile {index}.");
                    }
                }
            });

            var after = await Run(pair, output, "mouths Asclepiu");
            Assert.That(after.Count(line => line.Contains("Admin")), Is.EqualTo(1),
                $"mouths does not list the carved mouth:\n{string.Join('\n', after)}");
        }
        finally
        {
            await pair.Client.WaitPost(() => console.AddString -= capture);

            foreach (var world in worlds)
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The first footprint of solid ground where everything anchored is the biome's own, scanning east from a tile.</summary>
    private static Vector2i FindClearPatch(TestPair pair, World world, Vector2i from, int size)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();
        var biomes = pair.Server.System<BiomeSystem>();
        var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
        var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));

        for (var dx = 0; dx < 16; dx++)
        for (var dy = -4; dy <= 4; dy++)
        {
            var origin = from + new Vector2i(dx, dy);
            var clear = WFCavernMouthSystem.Footprint(origin, size).All(index =>
                maps.TryGetTileRef(world.Ground, grid, index, out var tile) && !tile.Tile.IsEmpty
                && maps.GetAnchoredEntities(world.Ground, grid, index).All(uid => biomes.WfIsBiomeSpawned(biome, uid, index)));

            if (clear)
                return origin;
        }

        Assert.Fail($"Precondition: no clear patch of ground east of {from} to carve a mouth in.");
        return from;
    }

    /// <summary>Runs one wfcavern subcommand as the test player and returns what the server wrote back.</summary>
    private static async Task<List<string>> Run(TestPair pair, List<string> output, string args)
    {
        await pair.Client.WaitPost(output.Clear);
        await pair.Server.WaitPost(() =>
            pair.Server.ConsoleHost.ExecuteCommand(pair.Player, $"{WolfgateAdminCommands.Cavern} {args}"));
        await pair.RunTicksSync(5);

        var lines = new List<string>();
        await pair.Client.WaitPost(() => lines.AddRange(output));
        return lines;
    }
}
