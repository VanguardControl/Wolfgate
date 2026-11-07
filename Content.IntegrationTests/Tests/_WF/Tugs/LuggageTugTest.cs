#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Tugs;
using Content.Shared._WF.Tugs;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.Tugs;

/// <summary>A rider drives a luggage tug that tows trolleys along its path, and crates strap onto the trolleys.</summary>
[TestFixture]
public sealed class LuggageTugTest
{
    private const float StartX = 4.5f;
    private const float StartY = 20.5f;

    private sealed class Rig
    {
        public required TestMapData Map;
        public EntityUid Tug;
        public EntityUid Driver;
        public readonly List<EntityUid> Trolleys = new();
    }

    /// <summary>An open plating floor with the tug facing south and six trolleys lined up north of it.</summary>
    private static async Task<Rig> Build(TestPair pair)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var rig = new Rig { Map = map };

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            for (var x = -10; x < 20; x++)
            {
                for (var y = -15; y < 30; y++)
                {
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            rig.Tug = entMan.SpawnEntity("WFLuggageTug", new EntityCoordinates(map.Grid.Owner, StartX, StartY));
            rig.Driver = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, StartX, StartY));

            for (var i = 1; i <= 6; i++)
            {
                rig.Trolleys.Add(entMan.SpawnEntity("WFLuggageTrolley", new EntityCoordinates(map.Grid.Owner, StartX, StartY + i)));
            }
        });
        await server.WaitRunTicks(5);

        return rig;
    }

    private static Entity<WFTugComponent> Tug(TestPair pair, Rig rig)
    {
        return (rig.Tug, pair.Server.EntMan.GetComponent<WFTugComponent>(rig.Tug));
    }

    private static Entity<WFTugTrolleyComponent> Trolley(TestPair pair, EntityUid uid)
    {
        return (uid, pair.Server.EntMan.GetComponent<WFTugTrolleyComponent>(uid));
    }

    private static Vector2 Local(TestPair pair, EntityUid uid)
    {
        return pair.Server.EntMan.GetComponent<TransformComponent>(uid).LocalPosition;
    }

    /// <summary>Buckling in makes the rider the operator; unbuckling clears it.</summary>
    [Test]
    public async Task BucklingInTakesTheWheel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var buckle = server.System<SharedBuckleSystem>();
            Assert.That(buckle.TryBuckle(rig.Driver, rig.Driver, rig.Tug), Is.True, "The rider could not buckle in.");
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BuckleComponent>(rig.Driver).BuckledTo, Is.EqualTo(rig.Tug), "The rider fell off.");
            Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Tug).Operator, Is.EqualTo(rig.Driver));
            Assert.That(entMan.HasComponent<GridVehicleOperatorComponent>(rig.Driver), Is.True);
        });

        // Buckling has a short cooldown before you can get up.
        await server.WaitRunTicks(90);
        await server.WaitAssertion(() =>
        {
            var buckle = server.System<SharedBuckleSystem>();
            Assert.That(buckle.TryUnbuckle(rig.Driver, rig.Driver), Is.True, "The rider could not unbuckle.");
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Tug).Operator, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Five trolleys couple in a row, a sixth is refused, and each one slows the tug.</summary>
    [Test]
    public async Task CouplesUpToFiveTrolleys()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var tugs = server.System<WFTugSystem>();
            var mover = entMan.GetComponent<GridVehicleMoverComponent>(rig.Tug);
            var baseSpeed = mover.MaxSpeed;

            for (var i = 0; i < 5; i++)
            {
                Assert.That(tugs.TryCouple(Tug(pair, rig), Trolley(pair, rig.Trolleys[i])), Is.True, $"Trolley {i + 1} did not couple.");
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tugs.TryCouple(Tug(pair, rig), Trolley(pair, rig.Trolleys[5])), Is.False, "A sixth trolley coupled.");
                Assert.That(Tug(pair, rig).Comp.Trolleys, Has.Count.EqualTo(5));
                Assert.That(mover.MaxSpeed, Is.LessThan(baseSpeed), "Towing did not slow the tug.");
            }

            // Uncoupling the third car drops it and the two behind.
            Assert.That(tugs.TryUncouple(Trolley(pair, rig.Trolleys[2])), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Tug(pair, rig).Comp.Trolleys, Is.EqualTo(new[] { rig.Trolleys[0], rig.Trolleys[1] }));
                Assert.That(Trolley(pair, rig.Trolleys[4]).Comp.Tug, Is.Null);

                var released = entMan.GetComponent<PhysicsComponent>(rig.Trolleys[2]);
                Assert.That(released.BodyType, Is.EqualTo(BodyType.Dynamic), "A released trolley stayed kinematic.");
                Assert.That(released.CanCollide, Is.True, "A released trolley stayed non-colliding.");
                Assert.That(Trolley(pair, rig.Trolleys[2]).Comp.Towed, Is.False);

                var towed = entMan.GetComponent<PhysicsComponent>(rig.Trolleys[1]);
                Assert.That(towed.BodyType, Is.EqualTo(BodyType.Kinematic), "A towed trolley is not kinematic.");
                Assert.That(towed.CanCollide, Is.False, "A towed trolley still collides.");
                Assert.That(Trolley(pair, rig.Trolleys[1]).Comp.Towed, Is.True);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Driving pulls the train along the tug's path at its spacing, and leaving the grid drops it.</summary>
    [Test]
    public async Task TrainFollowsTheTug()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var tugs = server.System<WFTugSystem>();
            for (var i = 0; i < 3; i++)
            {
                Assert.That(tugs.TryCouple(Tug(pair, rig), Trolley(pair, rig.Trolleys[i])), Is.True);
            }

            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(rig.Driver, rig.Driver, rig.Tug), Is.True);
        });
        await server.WaitRunTicks(2);

        // The tug faces south, so forward drives it down the grid.
        await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = MoveButtons.Up);
        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            var tug = Local(pair, rig.Tug);
            Assert.That(tug.Y, Is.LessThan(StartY - 3f), "The tug barely moved.");
            Assert.That(entMan.GetComponent<BuckleComponent>(rig.Driver).BuckledTo, Is.EqualTo(rig.Tug), "The rider fell off while driving.");

            for (var i = 0; i < 3; i++)
            {
                var car = Local(pair, rig.Trolleys[i]);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(car.X, Is.EqualTo(tug.X).Within(0.2f), $"Trolley {i + 1} left the path.");
                    Assert.That(car.Y - tug.Y, Is.EqualTo(i + 1f).Within(0.15f), $"Trolley {i + 1} is not at its spacing.");
                }
            }
        });

        await server.WaitPost(() =>
        {
            entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = MoveButtons.None;
            var xform = entMan.System<SharedTransformSystem>();
            xform.SetCoordinates(rig.Tug, new EntityCoordinates(rig.Map.MapUid, new Vector2(100f, 100f)));
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(Tug(pair, rig).Comp.Trolleys, Is.Empty, "The train stayed coupled to a tug that left its grid.");
            Assert.That(Trolley(pair, rig.Trolleys[0]).Comp.Tug, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crate straps onto a trolley, rides with it, and comes off again.</summary>
    [Test]
    public async Task CratesStrapToTrolleys()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var tugs = server.System<WFTugSystem>();
            var trolley = Trolley(pair, rig.Trolleys[0]);
            var crate = entMan.SpawnEntity("CrateGeneric", new EntityCoordinates(rig.Map.Grid.Owner, StartX + 1, StartY + 1));
            var mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Map.Grid.Owner, StartX + 1, StartY + 2));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tugs.TryLoad(trolley, mob), Is.False, "A mob was strapped on as cargo.");
                Assert.That(tugs.TryLoad(trolley, crate), Is.True, "The crate did not strap on.");
                Assert.That(trolley.Comp.Cargo!.ContainedEntity, Is.EqualTo(crate));
                Assert.That(entMan.GetComponent<TransformComponent>(crate).ParentUid, Is.EqualTo(trolley.Owner));
            }

            var drop = new EntityCoordinates(rig.Map.Grid.Owner, StartX + 2, StartY + 1);
            Assert.That(tugs.TryUnload(trolley, drop), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(trolley.Comp.Cargo!.ContainedEntity, Is.Null);
                Assert.That(entMan.GetComponent<TransformComponent>(crate).ParentUid, Is.EqualTo(rig.Map.Grid.Owner));
            }
        });

        await pair.CleanReturnAsync();
    }
}
