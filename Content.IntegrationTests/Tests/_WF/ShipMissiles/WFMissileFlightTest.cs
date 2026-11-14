using System.Collections.Generic;
using System.Numerics;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._WF.ShipMissiles;
using Content.Server.Shuttles.Components;
using Content.Shared.Tiles;
using Content.Shared.Cargo.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.ShipMissiles;

/// <summary>A ship missile launches, acquires a painted hull and steers at it on its diverters.</summary>
[TestFixture]
public sealed class WFMissileFlightTest
{
    /// <summary>The missiles this module flies.</summary>>
    private static readonly string[] Flown =
    {
        "MissileProjectile50mmHE",
        "ShipMissileASM150",
        "ShipMissileASM250",
        "ShipMissileASM250HEAT",
        "ShipMissileASM250HV",
    };
    /// <summary>
    /// The carriers must stay neutral: a carrier that still flies itself would fight the flight system, and one
    /// without a seeker cone would stop flares working.
    /// </summary>
    [Test]
    public async Task CarriersOnlySeek()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var offset = 0f;
            foreach (var id in Flown)
            {
                offset += 200f;
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.MapUid, new Vector2(offset, 2000f)));

                var seeker = em.GetComponent<TargetSeekingComponent>(uid);
                var flight = em.GetComponent<WFMissileFlightComponent>(uid);

                Assert.Multiple(() =>
                {
                    Assert.That(seeker.Acceleration, Is.Zero, $"{id} must not thrust itself.");
                    Assert.That(seeker.LaunchSpeed, Is.Zero, $"{id} must not launch itself.");
                    Assert.That(seeker.TurnRate?.Theta ?? 0f, Is.Zero, $"{id} must not aim itself.");
                    Assert.That(seeker.TrackingAlgorithm, Is.EqualTo(TrackingMethod.Direct),
                        $"{id} must not run an intercept solver that divides by its zero thrust.");
                    Assert.That(seeker.ScanArc, Is.GreaterThan(0f), $"{id} must keep a cone for flare decoys.");
                    Assert.That(seeker.DetectionRange, Is.GreaterThan(0f), $"{id} must keep a ring for flare decoys.");
                    Assert.That(flight.MaxSpeed, Is.EqualTo(seeker.MaxSpeed),
                        $"{id} carrier and flight must agree on the speed cap.");
                });
            }
        });

        await pair.CleanReturnAsync();
    }
    /// <summary>
    /// Fired down +x with the hull 600 m out and 100 m off the line of fire, the missile must lock, turn onto it and
    /// never exceed its speed cap.
    /// </summary>
    [Test]
    public async Task LaunchesAndLeads()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var xform = em.System<SharedTransformSystem>();

        EntityUid missile = default;

        await pair.Server.WaitAssertion(() =>
        {
            // A hull carrying a seeker target, off the missile's line of fire but inside its cone.
            var painted = em.SpawnEntity(null, map.GridCoords);
            xform.AnchorEntity(painted);
            em.AddComponent<TargetSeekingTargetComponent>(painted);

            missile = em.SpawnEntity("ShipMissileASM150",
                new EntityCoordinates(map.MapUid, new Vector2(-600f, -100f)));
            xform.SetWorldRotation(missile, Angle.FromDegrees(90));
        });

        await pair.RunTicksSync(20);

        await pair.Server.WaitAssertion(() =>
        {
            var flight = em.GetComponent<WFMissileFlightComponent>(missile);
            var seeker = em.GetComponent<TargetSeekingComponent>(missile);
            var body = em.GetComponent<PhysicsComponent>(missile);

            Assert.Multiple(() =>
            {
                Assert.That(flight.Launched, Is.True, "The launch impulse goes in on the first tick.");
                Assert.That(seeker.CurrentTarget, Is.EqualTo(map.Grid.Owner),
                    "The carrier must have acquired the painted hull.");
                Assert.That(flight.Seeker, Is.EqualTo(WFMissileSeekerState.Tracking), "A parked hull is not a notch.");
                Assert.That(flight.Divert, Is.GreaterThan(0f).And.LessThanOrEqualTo(flight.DivertBudget),
                    "A gentle lead must not drain the diverters.");
                Assert.That(body.LinearVelocity.X, Is.GreaterThan(0f), "The motor must push down the nose.");
                Assert.That(body.LinearVelocity.Y, Is.GreaterThan(0f), "Steering turns onto the off-axis hull.");
                Assert.That(body.LinearVelocity.Length(), Is.LessThanOrEqualTo(flight.MaxSpeed + 0.5f),
                    "The speed cap must hold.");
            });
        });

        await pair.CleanReturnAsync();
    }
    /// <summary>
    /// Handed 45 degrees off its nose, the missile still locks during the launch transient and takes the bearing: the
    /// ASM-50's own 7.5 degree scan arc would never see that hull.
    /// </summary>
    [Test]
    public async Task SoftLaunchTakesTheBearing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var xform = em.System<SharedTransformSystem>();

        EntityUid missile = default;

        await pair.Server.WaitAssertion(() =>
        {
            // The painted hull sits on the grid at its origin; the missile is 600 m out, 45 degrees off the bearing.
            var painted = em.SpawnEntity(null, map.GridCoords);
            xform.AnchorEntity(painted);
            em.AddComponent<TargetSeekingTargetComponent>(painted);

            missile = em.SpawnEntity("MissileProjectile50mmHE",
                new EntityCoordinates(map.MapUid, new Vector2(-424f, -424f)));
            xform.SetWorldRotation(missile, Angle.FromDegrees(90)); // +x: a quarter turn in world angles
        });

        await pair.RunTicksSync(12);

        await pair.Server.WaitAssertion(() =>
        {
            var flight = em.GetComponent<WFMissileFlightComponent>(missile);
            var seeker = em.GetComponent<TargetSeekingComponent>(missile);
            var body = em.GetComponent<PhysicsComponent>(missile);

            Assert.Multiple(() =>
            {
                Assert.That(seeker.CurrentTarget, Is.EqualTo(map.Grid.Owner),
                    "The launch cone must lock what the scan arc alone would miss.");
                Assert.That(flight.LaunchTimer, Is.GreaterThan(0f), "The launch transient is still running.");
                Assert.That(flight.Divert, Is.EqualTo(flight.DivertBudget), "Off-rail stabilisation is free.");
                Assert.That(body.LinearVelocity.X, Is.GreaterThan(0f), "The launch impulse runs down the nose.");
                Assert.That(body.LinearVelocity.Y, Is.GreaterThan(0f), "Steering turns onto the off-axis hull.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Fired at a large 5x5 hull with a small 1x1 decoy in between, the smart missile takes the far (matching) one
    /// and the dumb one takes the nearer decoy. Descriptor size-match rejects the decoy.
    /// </summary>
    [Test]
    [TestCase("ShipMissileASM150", false)]        // dumb: takes the near decoy
    [TestCase("ShipMissileASM150Smart", true)]    // smart: takes the far hull
    public async Task DescriptorSeekingPicksTheFiredAtTarget(string missileProto, bool expectFar)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var xform = em.System<SharedTransformSystem>();
        var maps = em.System<SharedMapSystem>();

        EntityUid missile = default, far = default, near = default;
        await pair.Server.WaitAssertion(() =>
        {
            // Far hull: a large 5x5 ship. Near decoy: a small 1x1 ship. Both marked.
            far = MakeGrid(em, maps, map.MapId, new Vector2(0f, 0f), 5);
            em.EnsureComponent<TargetSeekingTargetComponent>(far);

            near = MakeGrid(em, maps, map.MapId, new Vector2(-300f, 0f), 1);
            em.EnsureComponent<TargetSeekingTargetComponent>(near);

            missile = em.SpawnEntity(missileProto, new EntityCoordinates(map.MapUid, new Vector2(-600f, 0f)));
            xform.SetWorldRotation(missile, Angle.FromDegrees(90)); // +x

            // Designated is what "the player clicked": the far hull.
            var ec = xform.GetMapCoordinates(far); em.GetComponent<WFMissileFlightComponent>(missile).Designated = new MapCoordinates(ec.Position, ec.MapId);
        });

        await pair.Server.WaitRunTicks(20);

        await pair.Server.WaitAssertion(() =>
        {
            var target = em.GetComponent<TargetSeekingComponent>(missile).CurrentTarget;
            var expectUid = expectFar ? far : near;
            Assert.That(target, Is.EqualTo(expectUid));
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid MakeGrid(IEntityManager em, SharedMapSystem maps, MapId mapId, Vector2 pos, int size)
    {
        var grid = maps.CreateGridEntity(mapId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < size; x++)
            for (var y = 0; y < size; y++)
                tiles.Add((new Vector2i(x, y), new Tile(1)));
        maps.SetTiles(grid.Owner, grid.Comp, tiles);
        em.System<SharedPhysicsSystem>().SetBodyType(grid.Owner, BodyType.Dynamic);
        em.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, pos);
        em.EnsureComponent<ShuttleComponent>(grid.Owner);
        return grid.Owner;
    }
}
