#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests._WG.Drydock;
using Content.Server._WG.Unstuck;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Unstuck;

/// <summary>
/// Unstick me moves a body out of a wall to the nearest clear floor, and does nothing for one that isn't stuck.
/// </summary>
[TestFixture]
[TestOf(typeof(WGUnstuckSystem))]
public sealed class UnstuckTest
{
    private const string MobProto = "MobHuman";
    private const string WallProto = "WallSolid";

    private static readonly Vector2i WallTile = new(2, 2);

    /// <summary>A body inside a wall, nearer its top edge, lands on the floor just above it.</summary>
    [Test]
    public async Task StuckInWallMovesToNearestFloor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            DrydockTest.SpawnAnchored(pair, grid, WallProto, WallTile);
            var mob = em.SpawnEntity(MobProto, new EntityCoordinates(grid, WallTile.X + 0.5f, WallTile.Y + 0.8f));

            var unstuck = server.System<WGUnstuckSystem>();
            Assert.That(unstuck.TryUnstuck(mob, out var message), Is.True, message);

            var xform = em.GetComponent<TransformComponent>(mob);
            var tile = server.System<SharedMapSystem>()
                .TileIndicesFor(grid, em.GetComponent<MapGridComponent>(grid), xform.Coordinates);
            Assert.That(tile, Is.EqualTo(WallTile + new Vector2i(0, 1)), "The body didn't land on the nearest floor.");

            Assert.That(unstuck.TryUnstuck(mob, out message), Is.False, "A freed body still counted as stuck.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A body on open floor, or standing against a wall, is left where it is.</summary>
    [Test]
    public async Task FreeBodyIsNotMoved()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            DrydockTest.SpawnAnchored(pair, grid, WallProto, WallTile);
            var unstuck = server.System<WGUnstuckSystem>();

            foreach (var spot in new[] { new Vector2(0.5f, 0.5f), new Vector2(WallTile.X + 0.5f, WallTile.Y + 1.4f) })
            {
                var start = new EntityCoordinates(grid, spot);
                var mob = em.SpawnEntity(MobProto, start);
                Assert.That(unstuck.TryUnstuck(mob, out _), Is.False, $"A body at {spot} counted as stuck.");
                Assert.That(em.GetComponent<TransformComponent>(mob).Coordinates, Is.EqualTo(start), $"A body at {spot} was moved.");
                em.DeleteEntity(mob);
            }
        });

        await pair.CleanReturnAsync();
    }
}
