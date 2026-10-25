using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Drydock;
using Content.Server._WF.AsteroidMining;
using Content.Server._WF.DroneRings;
using Content.Server.Gatherable;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.AsteroidMining;

/// <summary>
/// Tests asteroid grids, cracking into fragments, boulders and ore, worldgen's swap of tile asteroids for clusters,
/// and the mining laser's pulses, heat window and overcharge.
/// </summary>
[TestFixture]
public sealed class AsteroidMiningTest
{
    private const string Gun = "WFWeaponTurretMiningBeam";
    private static readonly Vector2 ShipPos = new(200f, 0f);

    /// <summary>Spawns an asteroid grid at a world position and returns its rock.</summary>
    private static EntityUid SpawnAsteroid(TestPair pair, MapId map, string proto, Vector2 position)
    {
        var em = pair.Server.EntMan;
        var mapUid = pair.Server.System<SharedMapSystem>().GetMap(map);
        var grid = em.SpawnEntity(proto, new EntityCoordinates(mapUid, position));
        return em.GetComponent<WFAsteroidGridComponent>(grid).RockUid!.Value;
    }

    /// <summary>Rocks on their own asteroid grids, leaving out boulders.</summary>
    private static List<WFAsteroidComponent> GridRocks(IEntityManager em)
    {
        var rocks = new List<WFAsteroidComponent>();
        var query = em.EntityQueryEnumerator<WFAsteroidComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid is { } grid && em.TryGetComponent(grid, out WFAsteroidGridComponent? builder) &&
                builder.RockUid == uid)
                rocks.Add(comp);
        }

        return rocks;
    }

    private static List<EntityUid> Boulders(IEntityManager em)
    {
        var boulders = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<WFAsteroidComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == "WFAsteroidBoulder")
                boulders.Add(uid);
        }

        return boulders;
    }

    private static int OreItems(IEntityManager em)
    {
        var tags = em.System<TagSystem>();
        var count = 0;
        var query = em.EntityQueryEnumerator<TagComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (tags.HasTag(uid, "Ore"))
                count++;
        }

        return count;
    }

    [Test]
    public async Task CleanCrackSplitsIntoFragmentsAndOre()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var asteroids = server.System<WFAsteroidSystem>();

        EntityUid rock = default, grid = default, passenger = default;
        var mass = 0f;
        await server.WaitAssertion(() =>
        {
            rock = SpawnAsteroid(pair, map.MapId, "WFAsteroidLarge", ShipPos);
            asteroids.Roll(rock, "WFAsteroidRock");
            var comp = em.GetComponent<WFAsteroidComponent>(rock);
            var xform = em.GetComponent<TransformComponent>(rock);
            grid = xform.GridUid!.Value;
            mass = comp.Mass;
            Assert.Multiple(() =>
            {
                Assert.That(xform.Anchored, Is.True, "The rock should be anchored at the grid's centre.");
                Assert.That(server.System<SharedMapSystem>().GetAllTiles(grid, em.GetComponent<MapGridComponent>(grid)).Count(),
                    Is.GreaterThan(50), "A radius 5 rock should sit on a disc of tiles.");
                Assert.That(mass, Is.InRange(comp.MinMass, comp.MaxMass), "The rolled mass should be in the rock's range.");
                Assert.That(comp.Ores, Is.Not.Empty, "The rock should hold at least one ore.");
                Assert.That(comp.Ores.Sum(o => o.Fraction), Is.LessThan(1f), "Part of the rock should be inert.");
            });

            passenger = em.SpawnEntity("Crowbar", new EntityCoordinates(grid, 4.5f, 0.5f));
            asteroids.QueueCrack(rock, WFAsteroidCrack.Clean);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var fragments = GridRocks(em);
            Assert.That(em.Deleted(passenger), Is.False, "Things left on a cracked asteroid should stay behind in space.");
            var boulders = Boulders(em);
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(grid), Is.True, "Cracking should remove the rock's grid.");
                Assert.That(fragments, Has.Count.EqualTo(2), "A large rock should split into two fragments.");
                Assert.That(fragments.Sum(f => f.Mass), Is.EqualTo(mass * 0.5f).Within(0.01f),
                    "Fragments should carry the fragment share of the mass.");
                Assert.That(fragments.All(f => f.Ores.Count > 0), Is.True, "Fragments should keep the parent's ores.");
                Assert.That(boulders, Is.Not.Empty, "Part of the dropped ore should come out as boulders.");
                Assert.That(OreItems(em), Is.GreaterThan(0), "The rest of the mass should drop as ore.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WorldgenSwapsOreAsteroidsButKeepsScrap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var basalt = new WFDebrisPlacingEvent("NFAsteroidBasaltDebrisMassive", new MapCoordinates(ShipPos, map.MapId));
            server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref basalt);
            var scrap = new WFDebrisPlacingEvent("NFAsteroidScrapDebrisMassive", new MapCoordinates(ShipPos, map.MapId));
            server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref scrap);
            Assert.Multiple(() =>
            {
                Assert.That(basalt.Proto, Is.EqualTo("WFAsteroidClusterBasalt"), "A basalt tile asteroid should become a basalt cluster.");
                Assert.That(scrap.Proto, Is.EqualTo("NFAsteroidScrapDebrisMassive"), "Scrap asteroids should stay as tiles.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClusterFillsItsBudgetThenRemovesItself()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid marker = default;
        float budget = 0f;
        await server.WaitAssertion(() =>
        {
            var mapUid = server.System<SharedMapSystem>().GetMap(map.MapId);
            marker = em.SpawnEntity("WFAsteroidClusterRock", new EntityCoordinates(mapUid, ShipPos));
            budget = em.GetComponent<WFAsteroidClusterComponent>(marker).Mass;
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var rocks = GridRocks(em);
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(marker), Is.True, "The marker should remove itself once it has spawned its rocks.");
                Assert.That(rocks, Has.Count.GreaterThan(1), "The cluster should spawn several rocks.");
                Assert.That(rocks.All(r => r.CompositionId?.Id == "WFAsteroidRock" && r.Mass > 0f), Is.True,
                    "Every rock should roll from the cluster's composition.");
                Assert.That(rocks.Sum(r => r.Mass), Is.InRange(budget * 0.7f, budget * 1.05f),
                    "The rocks should add up to about the cluster's budget.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BoulderBreaksIntoOreWithoutTouchingItsShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var asteroids = server.System<WFAsteroidSystem>();

        EntityUid ship = default, mined = default, beamed = default;
        await server.WaitAssertion(() =>
        {
            ship = DrydockTest.CreateShip(pair, map.MapId, ShipPos);
            DrydockTest.SetFloor(pair, ship, 5);
            mined = em.SpawnEntity("WFAsteroidBoulder", new EntityCoordinates(ship, 1.5f, 1.5f));
            beamed = em.SpawnEntity("WFAsteroidBoulder", new EntityCoordinates(ship, 3.5f, 3.5f));
            asteroids.Roll(mined, "WFAsteroidRock");
            asteroids.Roll(beamed, "WFAsteroidRock");
            Assert.That(em.GetComponent<TransformComponent>(mined).GridUid, Is.EqualTo(ship), "The boulder should be aboard.");

            server.System<GatherableSystem>().Gather(mined);
            asteroids.QueueCrack(beamed, WFAsteroidCrack.Clean);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(mined), Is.True, "Gathering should break the boulder.");
                Assert.That(em.Deleted(beamed), Is.True, "Cracking should break the boulder.");
                Assert.That(em.Deleted(ship), Is.False, "Breaking a boulder aboard must never delete the ship's grid.");
                Assert.That(OreItems(em), Is.GreaterThan(0), "The boulders should drop their ore.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Sets up a ship with a mining laser and a small, fully predictable rock 17 m north of it.</summary>
    private static (EntityUid Gun, EntityUid Rock) SetUpBeam(TestPair pair, MapId map)
    {
        var em = pair.Server.EntMan;
        var ship = DrydockTest.CreateShip(pair, map, ShipPos);
        DrydockTest.SetFloor(pair, ship, 5);
        var gun = DrydockTest.SpawnAnchored(pair, ship, Gun, new Vector2i(2, 2));
        var rock = SpawnAsteroid(pair, map, "WFAsteroidSmall", ShipPos + new Vector2(2f, 17f));
        pair.Server.System<WFAsteroidSystem>().Roll(rock, "WFAsteroidRock");
        var comp = em.GetComponent<WFAsteroidComponent>(rock);
        comp.Resistance = 0f;
        comp.Instability = 0f;
        comp.WindowCenter = 0.55f;
        comp.WindowHalfWidth = 0.12f;
        return (gun, rock);
    }

    /// <summary>Holds the gun's fire on the rock, one pulse a tick, until the rock is gone or time runs out.</summary>
    private static async Task HoldFire(TestPair pair, EntityUid gun, EntityUid rock, float seconds)
    {
        var em = pair.Server.EntMan;
        var beams = pair.Server.System<WFMiningBeamSystem>();
        var ticks = (int) (seconds * pair.Server.ResolveDependency<IGameTiming>().TickRate);
        for (var i = 0; i < ticks && !em.Deleted(rock); i++)
        {
            await pair.Server.WaitPost(() =>
            {
                var beam = em.GetComponent<WFMiningBeamEmitterComponent>(gun);
                if (!em.Deleted(rock) && pair.Server.ResolveDependency<IGameTiming>().CurTime >= beam.CooldownUntil)
                    beams.Pulse(gun, beam, rock, 17f);
            });
            await pair.Server.WaitRunTicks(1);
        }
    }

    [Test]
    public async Task PulseHeatsTheRockItHitsInsteadOfDamagingIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (gun, rock) = SetUpBeam(pair, map.MapId);
            var pulse = em.SpawnEntity("WFMiningBeamPulse", MapCoordinates.Nullspace);
            var hits = new HashSet<EntityUid> { rock };
            var fired = new HitscanRaycastFiredEvent
            {
                FromCoordinates = em.GetComponent<TransformComponent>(gun).Coordinates,
                ShotDirection = new Vector2(0f, 1f),
                HitEntities = hits,
                Gun = gun,
                DistanceTried = 17f,
            };
            em.EventBus.RaiseLocalEvent(pulse, ref fired);

            var beam = em.GetComponent<WFMiningBeamEmitterComponent>(gun);
            Assert.Multiple(() =>
            {
                Assert.That(hits, Does.Not.Contain(rock), "The rock should leave the hit list, so it isn't damaged.");
                Assert.That(beam.Active, Is.True, "A pulse should light the beam.");
                Assert.That(beam.Target, Is.EqualTo(rock), "The beam should hold on the rock it hit.");
                Assert.That(em.GetComponent<DamageableComponent>(rock).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                    "The rock should take no damage from the pulse.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeamInsideWindowCracksRock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid gun = default, rock = default;
        await server.WaitAssertion(() =>
        {
            (gun, rock) = SetUpBeam(pair, map.MapId);
            var beam = em.GetComponent<WFMiningBeamEmitterComponent>(gun);
            // Settles the heat at the window's centre.
            beam.Power = 0.55f / beam.Strength;
        });

        await HoldFire(pair, gun, rock, 40f);
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(rock), Is.True, "Heat held in the window should crack the rock.");
                Assert.That(OreItems(em), Is.GreaterThan(0), "A small rock should crack straight into ore.");
                Assert.That(em.GetComponent<WFMiningBeamEmitterComponent>(gun).Active, Is.False,
                    "The beam should go out once the gun stops firing.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OverchargeBlowsRockAndOverloadsGun()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var timing = server.ResolveDependency<IGameTiming>();

        EntityUid gun = default, rock = default;
        await server.WaitAssertion(() =>
        {
            (gun, rock) = SetUpBeam(pair, map.MapId);
            // Full power on a rock with no resistance settles above the overcharge line.
            em.GetComponent<WFMiningBeamEmitterComponent>(gun).Power = 1f;
        });

        await HoldFire(pair, gun, rock, 40f);

        await server.WaitAssertion(() =>
        {
            var beam = em.GetComponent<WFMiningBeamEmitterComponent>(gun);
            var attempt = new AttemptShootEvent(gun, null);
            em.EventBus.RaiseLocalEvent(gun, ref attempt);
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(rock), Is.True, "Holding the rock over the line should blow it up.");
                Assert.That(beam.Active, Is.False, "The blast should put the beam out.");
                Assert.That(beam.CooldownUntil, Is.GreaterThan(timing.CurTime), "The gun should be cooling down.");
                Assert.That(attempt.Cancelled, Is.True, "The gun shouldn't fire while it cools down.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
