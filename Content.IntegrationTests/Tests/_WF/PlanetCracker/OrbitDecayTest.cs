#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Server._WF.ShipPa;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Flight;
using Content.Shared._WF.ShipPa;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// F11, orbit decay: an orbit layer holds up only what is holding itself up. A running linear thruster, a force
/// anchor or a dock onto something that has one keeps a grid parked; anything else - a dead ship, a shot-off hull,
/// debris - gets a minute of warning and then falls through the F10 descent like a hull that chose it.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOrbitDecaySystem))]
public sealed class OrbitDecayTest
{
    /// <summary>A selectable ship code, used as the code the warning has to borrow and hand back.</summary>
    private const string PriorCode = "ShipCodeYellow";

    /// <summary>Seconds the countdown is shortened to; the sweep is 1 Hz, so anything faster is not observable.</summary>
    private const float TestGrace = 1f;

    /// <summary>
    /// Seconds a grid that should be stamped is given to be stamped in: the sweep gives a hull ten seconds of settle
    /// from the moment it first appears on the layer, and then wants two sweeps running before it stamps anything.
    /// </summary>
    private const float StampWait = 13f;

    /// <summary>
    /// A hull with one powered landing thruster keeps station. Its lift ratio is only 0.40 - far short of flying -
    /// which is the point: holding an orbit is not the same question as holding an altitude.
    /// </summary>
    [Test]
    public async Task PoweredThrusterHoldsOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMap);
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 1);

        // Three seconds is three sweeps; a grid that was going to be stamped is stamped on the first.
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "A hull with a running thruster is decaying anyway.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The hull left the orbit layer without being told to.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull that has only just arrived is not stamped, even though the sweep reads no running thruster on it. An
    /// FTL hop lands with the shuttle's thrusters disabled and they come back on their own power event, so the sweeps
    /// straight after an arrival are reading the hop rather than the ship - which is how a vessel a crew had only
    /// just boarded went down the atmosphere with no warning anyone could act on.
    /// </summary>
    [Test]
    public async Task FreshArrivalWithPoweredThrustersIsNotStamped()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMap);
        await MapInitHull(pair, hull);
        var thrusters = await AddLandingThrusters(pair, hull, 1);

        // The arrival gap: powered thrusters that are off until their own next update.
        await SetThrusters(pair, hull, false);
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(thrusters, Is.Not.Empty, "Precondition: the hull was given a landing thruster.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "A hull was stamped as decaying inside its arrival settle.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The hull left the orbit layer without being told to.");
            }
        });

        await SetThrusters(pair, hull, true);
        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "The hull is decaying with its thrusters back and running.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The hull fell out of orbit after its thrusters came back.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The same hull with every thruster switched off is decaying: it takes the warning code, keeps its parking for
    /// the grace, and then goes down the F10 descent as a lift-lost hull rather than being deleted or stranded.
    /// </summary>
    [Test]
    public async Task LostStationKeepingDecaysAndFalls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMap);
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 1);

        await SetThrusters(pair, hull, false);
        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.True,
                    "A hull with no station-keeping is not decaying.");
                Assert.That(entMan.GetComponent<ShipAlertComponent>(hull).Code.Id,
                    Is.EqualTo(WFOrbitDecaySystem.AlertOrbitDecay),
                    "The decay warning never reached the ship's situation code.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The hull fell before its grace was up.");
            }
        });

        await Hasten(pair, hull, TestGrace);
        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.Not.EqualTo(orbit),
                    "The grace ran out and the hull is still parked in orbit.");
                Assert.That(entMan.HasComponent<WFLiftLostComponent>(hull), Is.True,
                    "The hull left orbit without entering lift lost, so no GPWS sequence runs on the way down.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "The countdown is still on a hull that has already left orbit.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Power back inside the grace cancels the whole thing, and the ship gets the code it was flying under back -
    /// silently, the way F10 hands a landed hull its own code back.
    /// </summary>
    [Test]
    public async Task ThrustBackInsideGraceCancels()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var alerts = server.System<ShipAlertSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMap);
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 1);

        await server.WaitPost(() => alerts.SetCode(hull, PriorCode));

        await SetThrusters(pair, hull, false);
        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ShipAlertComponent>(hull).Code.Id,
                Is.EqualTo(WFOrbitDecaySystem.AlertOrbitDecay),
                "Precondition: the warning took the ship's code over.");
        });

        await SetThrusters(pair, hull, true);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "The countdown survived the thrusters coming back.");
                Assert.That(entMan.GetComponent<ShipAlertComponent>(hull).Code.Id, Is.EqualTo(PriorCode),
                    "The ship was not handed its own situation code back.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "The hull fell anyway.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Debris: a bare grid with nobody aboard, no console and no thrusters. It comes down on its own and stays down -
    /// orbit decay puts wrecks on the ground, it never cleans them up.
    /// </summary>
    [Test]
    public async Task ThrusterlessDebrisFallsAndStaysAWreck()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        // Ground for it to land on: the biome only generates around a player, and there is nobody here at all.
        await LayTiles(pair, ground, new Vector2i(-16, -16), new Vector2i(16, 16));

        var debris = await BuildDebris(pair, orbitMap, 3);

        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(debris), Is.True,
                "A thrusterless fragment is holding an orbit for free.");
        });

        await Hasten(pair, debris, TestGrace);

        // Four gaps of free fall, sampled rather than guessed at; the whole stack is about fifteen seconds deep.
        for (var i = 0; i < 200 && !await OnGround(pair, debris, ground); i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(debris).MapUid, Is.EqualTo(ground),
                "The debris never reached the ground layer.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(30f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.EntityExists(debris), Is.True, "The wreck was cleaned up; wrecks are forever.");
                Assert.That(entMan.GetComponent<TransformComponent>(debris).MapUid, Is.EqualTo(ground),
                    "The wreck did not stay where it landed.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A dead hull welded to a live one rides its station-keeping: the rule pools over the docked set, so a tender
    /// under tow does not drag itself out of orbit.
    /// </summary>
    [Test]
    public async Task DockedToStationKeepingHoldsOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var docking = server.System<DockingSystem>();
        var shuttles = server.System<ShuttleSystem>();
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildCracker(pair, orbitMap);
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 1);

        var transport = await BuildTransport(pair, orbitMap, new Vector2(400f, 0f));
        await MapInitHull(pair, transport);
        await SetThrusters(pair, transport, false);

        // Park the transport's own port where the hull's is, then weld the two together at rest (CrackConsoleTest).
        await server.WaitPost(() =>
        {
            var hullDock = FindDock(entMan, hull);
            var transportDock = FindDock(entMan, transport);

            Assert.That(hullDock, Is.Not.EqualTo(EntityUid.Invalid), "The cracker hull has no docking port.");
            Assert.That(transportDock, Is.Not.EqualTo(EntityUid.Invalid), "The transport has no docking port.");

            var wanted = transform.GetWorldPosition(hullDock) - transform.GetWorldPosition(transportDock);
            transform.SetWorldPosition(transport, transform.GetWorldPosition(transport) + wanted);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitPost(() =>
        {
            var hullDock = FindDock(entMan, hull);
            var transportDock = FindDock(entMan, transport);

            docking.Dock(
                (hullDock, entMan.GetComponent<DockingComponent>(hullDock)),
                (transportDock, entMan.GetComponent<DockingComponent>(transportDock)));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var docked = new HashSet<EntityUid>();
            shuttles.GetAllDockedShuttles(hull, docked);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(docked, Does.Contain(transport), "Precondition: the transport is docked to the hull.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(transport), Is.False,
                    "A thrusterless grid docked to a hull that is keeping station is decaying anyway.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(hull), Is.False,
                    "The hull that is keeping station is decaying.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The two grids the sweep must never touch: a force-anchored grid, which is held by something other than its
    /// own engines, and a chunk in a berth, whose fall F7 owns from end to end.
    /// </summary>
    [Test]
    public async Task ForceAnchoredAndChunkNeverDecay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var anchored = await BuildDebris(pair, orbitMap, 3, new Vector2(30f, 0f));
        var chunk = await BuildDebris(pair, orbitMap, 3, new Vector2(60f, 0f));

        await server.WaitPost(() =>
        {
            entMan.AddComponent<ForceAnchorComponent>(anchored);

            // F7's own watchdog drops an orphan chunk five seconds after extraction, which would take the grid off
            // the layer before the decay sweep had anything to say about it.
            var comp = entMan.AddComponent<WFPlanetChunkComponent>(chunk);
            comp.ExtractedAt = timing.CurTime;
            comp.WatchdogGrace = TimeSpan.FromMinutes(5);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(anchored), Is.False,
                    "A force-anchored grid is decaying.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(chunk), Is.False,
                    "A chunk in the berth is decaying.");
                Assert.That(entMan.GetComponent<TransformComponent>(anchored).MapUid, Is.EqualTo(orbit),
                    "The force-anchored grid left the orbit layer.");
                Assert.That(entMan.GetComponent<TransformComponent>(chunk).MapUid, Is.EqualTo(orbit),
                    "The chunk left the orbit layer.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Switches every thruster on a grid on or off through ThrusterSystem, which is what a power cut or a hit ends up
    /// doing. ThrusterComponent is [Access(typeof(ThrusterSystem))], so Enabled cannot be written from here at all;
    /// IsOn is the flag the station-keeping rule reads anyway.
    /// </summary>
    private static async Task SetThrusters(TestPair pair, EntityUid grid, bool enabled)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var thrusters = server.System<ThrusterSystem>();

        await server.WaitPost(() =>
        {
            foreach (var uid in Children(entMan, grid))
            {
                if (!entMan.TryGetComponent<ThrusterComponent>(uid, out var thruster))
                    continue;

                if (!enabled)
                    thrusters.DisableThruster(uid, thruster);
                else if (thrusters.CanEnable(uid, thruster))
                    thrusters.EnableThruster(uid, thruster);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
    }

    /// <summary>
    /// Shortens a countdown that is already running. Grace is a component field precisely so a test does not sit
    /// through the minute the crew gets; the deadline is re-stamped with it because the sweep stamps once, as the
    /// warning goes out.
    /// </summary>
    private static async Task Hasten(TestPair pair, EntityUid grid, float seconds)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitPost(() =>
        {
            var decay = entMan.GetComponent<WFOrbitDecayComponent>(grid);
            decay.Grace = TimeSpan.FromSeconds(seconds);
            decay.DecayAt = timing.CurTime + decay.Grace;
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>The map id of a layer, which is what a grid is built on.</summary>
    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapId = MapId.Nullspace;

        await server.WaitPost(() => mapId = entMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }

    /// <summary>True once a grid has arrived on the ground layer, however it got there.</summary>
    private static async Task<bool> OnGround(TestPair pair, EntityUid grid, EntityUid ground)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var landed = false;

        await server.WaitPost(() => landed = entMan.GetComponent<TransformComponent>(grid).MapUid == ground);
        return landed;
    }

    /// <summary>The first docking port resting on a grid, or Invalid.</summary>
    private static EntityUid FindDock(IEntityManager entMan, EntityUid grid)
    {
        foreach (var uid in Children(entMan, grid))
        {
            if (entMan.HasComponent<DockingComponent>(uid))
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>
    /// Every z-layer map is itself a grid. None of the flight sweeps may mistake one for a hull: the orbit map is
    /// not an orbiter to drop, and an air layer is not a hull in flight that plays wind to everyone on it.
    /// </summary>
    [Test]
    public async Task LayerMapsAreNeverStampedAsHulls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);

        await server.WaitRunTicks(pair.SecondsToTicks(StampWait));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var layer in layers)
                {
                    Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(layer), Is.False,
                        $"Layer map {entMan.ToPrettyString(layer)} was stamped as a decaying orbiter.");
                    Assert.That(entMan.HasComponent<Content.Server._WF.PlanetCracker.Flight.WFFlightAmbienceComponent>(layer), Is.False,
                        $"Layer map {entMan.ToPrettyString(layer)} was treated as a hull in flight.");
                    Assert.That(entMan.HasComponent<Content.Server._WF.PlanetCracker.Flight.WFPlanetDragComponent>(layer), Is.False,
                        $"Layer map {entMan.ToPrettyString(layer)} was given planet drag.");
                }
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
