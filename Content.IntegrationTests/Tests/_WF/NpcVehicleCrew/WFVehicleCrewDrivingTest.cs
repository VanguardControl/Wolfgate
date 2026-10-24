#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.NpcVehicleCrew;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcVehicleCrew;

/// <summary>
/// An NPC driver keeps a real vehicle on the road: round a bend in a road-wide corridor to the far end, without circling,
/// and round a wall blocking the way.
/// </summary>
[TestFixture]
[TestOf(typeof(WFVehicleCrewSystem))]
public sealed class WFVehicleCrewDrivingTest
{
    private const string Crew = "WFVehicleCrewCivilianTruck";

    /// <summary>A road-wide corridor: east along y = 0, then north up x = 47.</summary>
    private static bool Road(Vector2i tile) =>
        tile.X is >= -4 and <= 50 && tile.Y is >= -3 and <= 3 || tile.X is >= 44 and <= 50 && tile.Y is >= -3 and <= 44;

    private static async Task<(EntityUid Vehicle, MapCoordinates Goal)> Rig(TestPair pair, Func<Vector2i, bool> floor, Vector2 goal)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid vehicle = default;
        MapCoordinates target = default;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false);
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            // Floor first: a grid left with no tiles at all is deleted.
            var laid = new List<(Vector2i, Tile)>();
            var cleared = new List<(Vector2i, Tile)>();
            for (var x = -8; x <= 54; x++)
            for (var y = -8; y <= 48; y++)
            {
                var tile = new Vector2i(x, y);
                if (floor(tile))
                    laid.Add((tile, new Tile(plating)));
                else
                    cleared.Add((tile, Tile.Empty));
            }
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, laid);
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, cleared);

            Assert.That(server.System<WFVehicleCrewSystem>().TrySpawn(Crew, new EntityCoordinates(map.Grid.Owner, 2.5f, 0.5f),
                out vehicle, WFVehicleBehaviour.Hold), Is.True);
            target = server.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(map.Grid.Owner, goal));
            server.System<WFVehicleCrewSystem>().SetHome(vehicle, target);
        });
        return (vehicle, target);
    }

    /// <summary>Drives until the vehicle is at the goal or the time is up; returns how far it turned in all.</summary>
    private static async Task<float> DriveTo(TestPair pair, EntityUid vehicle, MapCoordinates goal, float seconds)
    {
        var server = pair.Server;
        var xforms = server.System<SharedTransformSystem>();
        var turned = 0f;
        Angle? last = null;
        for (var waited = 0f; waited < seconds; waited += 0.25f)
        {
            var arrived = false;
            await server.WaitPost(() =>
            {
                var rotation = xforms.GetWorldRotation(vehicle);
                if (last is { } previous)
                    turned += (float) Math.Abs(Angle.ShortestDistance(previous, rotation).Degrees);
                last = rotation;
                arrived = Vector2.Distance(xforms.GetWorldPosition(vehicle), goal.Position) < 2.5f;
            });
            if (arrived)
                break;
            await pair.RunSeconds(0.25f);
        }
        return turned;
    }

    [Test]
    public async Task DrivesRoundABendWithoutCircling()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var (vehicle, goal) = await Rig(pair, Road, new Vector2(47.5f, 40.5f));
        var turned = await DriveTo(pair, vehicle, goal, 60f);

        await server.WaitAssertion(() =>
        {
            var distance = Vector2.Distance(server.System<SharedTransformSystem>().GetWorldPosition(vehicle), goal.Position);
            Assert.That(distance, Is.LessThan(2.5f), $"The vehicle didn't get round the bend: {distance:0.0} tiles short.");
            // One bend is a quarter turn; a little weaving on top is fine, laps of the corridor aren't.
            Assert.That(turned, Is.LessThan(300f), $"The vehicle turned {turned:0} degrees on a road with one bend.");
        });
        await pair.Server.WaitPost(() => server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, CCVars.NPCPauseWhenNoPlayersInRange.DefaultValue));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DrivesRoundAWallInTheWay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        // Open ground with a wall across the middle of the way.
        var (vehicle, goal) = await Rig(pair, tile => tile.X is >= -6 and <= 50 && tile.Y is >= -20 and <= 20, new Vector2(44.5f, 0.5f));
        await server.WaitPost(() =>
        {
            var grid = server.EntMan.GetComponent<TransformComponent>(vehicle).GridUid!.Value;
            for (var y = -6; y <= 6; y++)
                server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 22.5f, y + 0.5f));
        });
        await DriveTo(pair, vehicle, goal, 60f);

        await server.WaitAssertion(() =>
        {
            var distance = Vector2.Distance(server.System<SharedTransformSystem>().GetWorldPosition(vehicle), goal.Position);
            Assert.That(distance, Is.LessThan(2.5f), $"The vehicle didn't get round the wall: {distance:0.0} tiles short.");
        });
        await pair.Server.WaitPost(() => server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, CCVars.NPCPauseWhenNoPlayersInRange.DefaultValue));
        await pair.CleanReturnAsync();
    }
}
