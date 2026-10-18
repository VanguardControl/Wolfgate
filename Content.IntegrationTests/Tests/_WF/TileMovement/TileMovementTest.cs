#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Shared._WF.TileMovement;
using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.TileMovement;

/// <summary>A tile walker steps between tile centres, stops short of walls and settles when the key is let go.</summary>
[TestFixture]
public sealed class TileMovementTest
{
    private const int WallX = 8;

    /// <summary>A plating room with a wall column at <see cref="WallX"/> and a tile walker at (2.5, 2.5).</summary>
    private static async Task<EntityUid> Build(TestPair pair, bool tileWalker = true)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var mob = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            entMan.EnsureComponent<GravityComponent>(map.Grid).Enabled = true;

            for (var x = -2; x < 12; x++)
            {
                for (var y = -2; y < 8; y++)
                {
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            for (var y = -2; y < 8; y++)
            {
                entMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, WallX + 0.5f, y + 0.5f));
            }

            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 2.5f, 2.5f));
            if (tileWalker)
                entMan.AddComponent<WFTileMovementComponent>(mob);
        });
        await server.WaitRunTicks(5);

        return mob;
    }

    private static async Task Hold(TestPair pair, EntityUid mob, MoveButtons buttons, int ticks)
    {
        await pair.Server.WaitPost(() =>
            pair.Server.EntMan.GetComponent<InputMoverComponent>(mob).HeldMoveButtons = buttons);
        await pair.Server.WaitRunTicks(ticks);
    }

    private static Vector2 Position(TestPair pair, EntityUid mob)
    {
        return pair.Server.EntMan.GetComponent<TransformComponent>(mob).LocalPosition;
    }

    private static void AssertCentred(Vector2 position)
    {
        var fracX = position.X - MathF.Floor(position.X);
        var fracY = position.Y - MathF.Floor(position.Y);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fracX, Is.EqualTo(0.5f).Within(0.02f), $"Not on a tile centre: {position}");
            Assert.That(fracY, Is.EqualTo(0.5f).Within(0.02f), $"Not on a tile centre: {position}");
        }
    }

    /// <summary>Letting go mid-walk finishes the step on a tile centre in the same row.</summary>
    [Test]
    public async Task ReleasingFinishesOnATileCentre()
    {
        await using var pair = await PoolManager.GetServerClient();
        var mob = await Build(pair);

        await Hold(pair, mob, MoveButtons.Right, 23);
        await Hold(pair, mob, MoveButtons.None, 60);

        await pair.Server.WaitAssertion(() =>
        {
            var position = Position(pair, mob);
            AssertCentred(position);
            Assert.That(position.X, Is.GreaterThan(3f), "The mob did not move.");
            Assert.That(position.Y, Is.EqualTo(2.5f).Within(0.02f), "The mob drifted off its row.");
            Assert.That(pair.Server.EntMan.GetComponent<WFTileMovementComponent>(mob).Moving, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Walking into a wall stops on the tile in front of it.</summary>
    [Test]
    public async Task WallStopsOnTheTileBefore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var mob = await Build(pair);

        await Hold(pair, mob, MoveButtons.Right, 240);

        await pair.Server.WaitAssertion(() =>
        {
            var position = Position(pair, mob);
            AssertCentred(position);
            Assert.That(position.X, Is.EqualTo(WallX - 0.5f).Within(0.02f));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A diagonal key pair steps diagonally and ends centred.</summary>
    [Test]
    public async Task DiagonalStepsEndCentred()
    {
        await using var pair = await PoolManager.GetServerClient();
        var mob = await Build(pair);

        await Hold(pair, mob, MoveButtons.Up | MoveButtons.Right, 20);
        await Hold(pair, mob, MoveButtons.None, 60);

        await pair.Server.WaitAssertion(() =>
        {
            var position = Position(pair, mob);
            AssertCentred(position);
            Assert.That(position.X - 2.5f, Is.EqualTo(position.Y - 2.5f).Within(0.02f), $"Not a diagonal path: {position}");
            Assert.That(position.X, Is.GreaterThan(3f), "The mob did not move.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A tap late in one tick and released early in the next still takes a full-speed step.</summary>
    [Test]
    public async Task TapFinishesAtFullSpeed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var mob = await Build(pair);
        var server = pair.Server;
        var mover = server.System<Content.Server.Physics.Controllers.MoverController>();

        await server.WaitPost(() =>
            mover.SetVelocityDirection((mob, server.EntMan.GetComponent<InputMoverComponent>(mob)), Direction.East, 60000, true));
        await server.WaitRunTicks(1);
        await server.WaitPost(() =>
            mover.SetVelocityDirection((mob, server.EntMan.GetComponent<InputMoverComponent>(mob)), Direction.East, 2000, false));
        // A walking step takes under half a second; the subtick-scaled speed took several.
        await server.WaitRunTicks(40);

        await server.WaitAssertion(() =>
        {
            var position = Position(pair, mob);
            AssertCentred(position);
            Assert.That(position.X, Is.EqualTo(3.5f).Within(0.02f));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Wall-mounted lights and cameras along the way don't stop a step.</summary>
    [Test]
    public async Task WallmountsDontBlock([Values("Poweredlight", "EmergencyLight", "SurveillanceCameraGeneral")] string mount)
    {
        await using var pair = await PoolManager.GetServerClient();
        var mob = await Build(pair);

        await pair.Server.WaitPost(() =>
        {
            var grid = pair.Server.EntMan.GetComponent<TransformComponent>(mob).ParentUid;
            for (var x = 3; x < WallX; x++)
            {
                var mounted = pair.Server.EntMan.SpawnEntity(mount, new EntityCoordinates(grid, x + 0.5f, 2.5f));
                pair.Server.System<SharedTransformSystem>().SetLocalRotation(mounted, Angle.FromDegrees(90 * x));
            }
        });

        await Hold(pair, mob, MoveButtons.Right, 240);

        await pair.Server.WaitAssertion(() =>
        {
            var position = Position(pair, mob);
            AssertCentred(position);
            Assert.That(position.X, Is.EqualTo(WallX - 0.5f).Within(0.02f));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Without the component the mob still moves freely.</summary>
    [Test]
    public async Task FreeMovementUnchangedWithoutTrait()
    {
        await using var pair = await PoolManager.GetServerClient();
        var mob = await Build(pair, tileWalker: false);

        await Hold(pair, mob, MoveButtons.Right, 30);

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(Position(pair, mob).X, Is.GreaterThan(3f), "The mob did not move.");
            Assert.That(pair.Server.EntMan.HasComponent<WFTileMovementComponent>(mob), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
