#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.PlanetCracker.Chunk;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// What happens to a cut disc once nothing is holding it: the watchdog's three abandonment cases, the pause its grace
/// has to survive, the push itself and the offset that keeps it out of the hull's way on the way down.
/// Everything about a push is read in the same server callback that makes it, for the reason CrackFallTest gives: the
/// very next frame is currently a defect rather than a contract.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetChunkSystem))]
public sealed class ChunkDropTest
{
    /// <summary>
    /// Design D24's first case. A hull that stops existing is not holding anything, and the disc it was holding has to
    /// go down rather than hang in a berth that is no longer there.
    /// </summary>
    [Test]
    public async Task WatchdogDropsTheChunkWhenTheCrackerIsDeleted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var events = server.System<WFAnchorTestEventSystem>();

        await server.WaitPost(() => events.Clear());

        var site = await BuildExtracted(pair);
        var chunk = await ArmWatchdog(pair);

        await server.WaitPost(() => entMan.DeleteEntity(site.Cracker));

        // Two sweeps of the 1 Hz watchdog, which is what the design allows it.
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);
            var map = entMan.GetComponent<TransformComponent>(chunk).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Dropped, Is.True, "The orphaned chunk is still hanging in a berth that no longer exists.");
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(map), Is.True, "The dropped chunk never reached a transit map.");
                Assert.That(events.ChunksDropped, Has.Count.EqualTo(1), "The drop hook did not fire exactly once.");
                Assert.That(events.ChunksDropped[0].Chunk, Is.EqualTo(chunk), "The drop hook named another chunk.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The hull is still alive but has left the layer, which is the same abandonment by another route.</summary>
    [Test]
    public async Task WatchdogDropsTheChunkWhenTheCrackerLeavesOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildExtracted(pair);
        var chunk = await ArmWatchdog(pair);
        var elsewhere = await pair.CreateTestMap();

        await server.WaitPost(() => transform.SetCoordinates(site.Cracker, new EntityCoordinates(elsewhere.MapUid, new Vector2(64f, 64f))));
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Dropped, Is.True, "The chunk stayed in a berth its hull had flown away from.");
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(entMan.GetComponent<TransformComponent>(chunk).MapUid), Is.True,
                    "The dropped chunk never reached a transit map.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>
    /// The case design D24 exists for and the reason the test is an IDENTITY test: every planet has an orbit layer and
    /// the orbit layer is an FTL destination, so a hull that jumps to ANOTHER planet's orbit passes any kind test while
    /// abandoning its chunk just as completely.
    /// </summary>
    [Test]
    public async Task WatchdogDropsTheChunkWhenTheCrackerMovesToAnotherPlanetsOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildExtracted(pair);
        var chunk = await ArmWatchdog(pair);

        var second = await BuildOwnedStack(pair);
        var otherOrbit = second.Layers[^1];

        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<WFOrbitLayerComponent>(otherOrbit), Is.True,
                "Precondition: the second stack's top layer is an orbit layer too, so a kind test would pass."));

        await server.WaitPost(() => transform.SetCoordinates(site.Cracker, new EntityCoordinates(otherOrbit, new Vector2(128f, 128f))));
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetChunkComponent>(chunk).Dropped, Is.True,
                "The chunk stayed put while its hull sat in a different planet's orbit."));

        await Cleanup(pair, site);
        await Teardown(pair, second.Layers);
    }

    /// <summary>
    /// The AutoPausedField proof. ExtractedAt is paused with its map, so a berth that sat paused for longer than the
    /// whole grace window must still be inside it the moment the map comes back; a plain TimeSpan would burn the lot
    /// and drop the disc out from under a perfectly healthy hull on the first sweep after the unpause.
    /// </summary>
    [Test]
    public async Task TheWatchdogGraceSurvivesAPause()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        var site = await BuildExtracted(pair);
        var chunk = EntityUid.Invalid;
        var before = TimeSpan.Zero;
        var grace = TimeSpan.Zero;

        await server.WaitAssertion(() =>
        {
            chunk = FindChunk(entMan);
            Assert.That(chunk, Is.Not.EqualTo(EntityUid.Invalid), "Precondition: a disc was cut.");

            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);
            before = comp.ExtractedAt;
            grace = comp.WatchdogGrace;

            Assert.That(grace, Is.GreaterThan(TimeSpan.Zero), "Precondition: the watchdog has a grace window to shift.");
        });

        // Paused for comfortably longer than the whole grace, with the sweep still running the entire time.
        await server.WaitPost(() => maps.SetPaused(new Entity<MapComponent?>(site.Orbit, null), true));
        await server.WaitRunTicks(pair.SecondsToTicks((float)grace.TotalSeconds + 4f));
        await server.WaitPost(() => maps.SetPaused(new Entity<MapComponent?>(site.Orbit, null), false));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Dropped, Is.False, "The chunk dropped while its own map was paused.");
                Assert.That(comp.ExtractedAt, Is.GreaterThan(before + grace),
                    "The extraction time did not shift with the pause, so the whole grace window expired while nothing was running.");
            }
        });

        // And the grace is genuinely still running: an abandonment right now is not acted on until it expires.
        await server.WaitPost(() => entMan.DeleteEntity(site.Cracker));
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetChunkComponent>(chunk).Dropped, Is.False,
                "The watchdog acted inside a grace window the pause should have pushed forward."));

        await server.WaitRunTicks(pair.SecondsToTicks((float)grace.TotalSeconds + 3f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetChunkComponent>(chunk).Dropped, Is.True,
                "The grace never expired at all, so the watchdog would never drop anything."));

        await Cleanup(pair, site);
    }

    /// <summary>
    /// The push itself, read in the instant it happens, then its aftermath. Without the fall seed the hover branch
    /// sees progress above the touchdown threshold and a velocity inside the exit band and puts the chunk straight
    /// back on the layer it came from, which presents as "the drop did nothing" a frame later.
    /// </summary>
    [Test]
    public async Task DropChunkPushesIntoTransitAndKeepsDescending()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var chunks = server.System<WFPlanetChunkSystem>();

        var site = await BuildExtracted(pair);
        var chunk = EntityUid.Invalid;

        var onTransit = false;
        var hasFaller = false;
        var velocity = 0f;
        var progress = -1f;
        var bodyType = BodyType.Static;
        var stillPinned = true;

        await server.WaitPost(() =>
        {
            chunk = FindChunk(entMan);

            chunks.DropChunk((chunk, entMan.GetComponent<WFPlanetChunkComponent>(chunk)));

            var map = entMan.GetComponent<TransformComponent>(chunk).MapUid;

            onTransit = entMan.HasComponent<CEZTransitMapComponent>(map);
            hasFaller = entMan.TryGetComponent(chunk, out CEZGridFallerComponent? faller);
            velocity = faller?.Velocity ?? 0f;
            progress = entMan.TryGetComponent(chunk, out CEZPhysicsComponent? zPhys) ? zPhys.LocalPosition : -1f;
            bodyType = entMan.TryGetComponent(chunk, out PhysicsComponent? body) ? body.BodyType : BodyType.Static;
            // PreventGridAnchorChanges deliberately stays on: it is what keeps CE's un-forced Enable from unfixing
            // the chunk's rotation mid-push. The pin that matters is the force-anchor and the static body.
            stillPinned = entMan.HasComponent<ForceAnchorComponent>(chunk);
        });

        await server.WaitRunTicks(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(onTransit, Is.True, "The chunk is not on a transit map, so the push never happened.");
            Assert.That(hasFaller, Is.True, "The push never made the chunk a faller.");
            Assert.That(velocity, Is.GreaterThan(0.1f), "The chunk was seeded inside the transit exit band and will pop straight back up.");
            Assert.That(bodyType, Is.Not.EqualTo(BodyType.Static), "The chunk was still static in the instant the push returned.");
            Assert.That(stillPinned, Is.False, "The push left the chunk's anchoring lock on, which pins it static for the whole drop.");
        }

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var map = entMan.GetComponent<TransformComponent>(chunk).MapUid;
            var now = entMan.TryGetComponent(chunk, out CEZPhysicsComponent? zPhys) ? zPhys.LocalPosition : -1f;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(map), Is.True,
                    "A second later the chunk has settled back out of transit instead of falling.");
                Assert.That(now, Is.LessThan(progress), $"The chunk is not descending (was {progress}, now {now}).");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>
    /// Two grids at identical progress give prevDelta * curDelta == 0, which is not greater than zero, so the transit
    /// collision check AABB-tests them every tick and explodes them both on contact. The chunk therefore joins the
    /// hull's fall one notch below it, and both values are captured in the SAME callback that makes the push because
    /// the faller advances on every tick afterwards.
    /// </summary>
    [Test]
    public async Task TheChunkJoinsTheHullsFallAtADistinctProgress()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();

        var site = await BuildExtracted(pair);
        var chunk = EntityUid.Invalid;

        var hullProgress = -1f;
        var chunkProgress = -1f;
        var dropped = false;

        await server.WaitPost(() =>
        {
            chunk = FindChunk(entMan);

            crackers.Fall((site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker)));

            hullProgress = entMan.TryGetComponent(site.Cracker, out CEZPhysicsComponent? hullZ) ? hullZ.LocalPosition : -1f;
            chunkProgress = entMan.TryGetComponent(chunk, out CEZPhysicsComponent? chunkZ) ? chunkZ.LocalPosition : -1f;
            dropped = entMan.GetComponent<WFPlanetChunkComponent>(chunk).Dropped;
        });

        await server.WaitRunTicks(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dropped, Is.True, "The hull's fall did not take its chunk with it.");
            Assert.That(hullProgress, Is.EqualTo(1f).Within(0.01f), "The hull did not enter transit at the top of its level.");
            Assert.That(chunkProgress, Is.EqualTo(0.98f).Within(0.01f), "The chunk did not enter transit just below the hull.");
            Assert.That(hullProgress, Is.GreaterThan(chunkProgress),
                "The hull and its chunk entered transit at the same progress, which is what the order-swap guard cannot survive.");
        }

        await Cleanup(pair, site);
    }

    /// <summary>A hull that never cut anything has no chunk to take with it, and that is not an error.</summary>
    [Test]
    public async Task AFallingEventWithNoChunkIsIgnored()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();
        var events = server.System<WFAnchorTestEventSystem>();

        await server.WaitPost(() => events.Clear());

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        await server.WaitPost(() =>
            crackers.Fall((site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker))));

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(FindChunk(entMan), Is.EqualTo(EntityUid.Invalid), "A hull that never extracted produced a chunk on its way down.");
                Assert.That(events.ChunksDropped, Is.Empty, "A hull with no chunk still raised the drop hook.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).Chunk, Is.Null,
                    "A hull that never extracted carries a chunk back-link.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>
    /// Finds the extracted chunk and takes the watchdog's grace off it, so a three second wait really is two sweeps
    /// rather than two sweeps plus the design's five second window.
    /// </summary>
    private static async Task<EntityUid> ArmWatchdog(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var chunk = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            chunk = FindChunk(entMan);

            Assert.That(chunk, Is.Not.EqualTo(EntityUid.Invalid), "Precondition: a disc was cut to abandon.");

            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);

            Assert.That(comp.Dropped, Is.False, "Precondition: the fresh chunk is hanging rather than falling.");

            comp.WatchdogGrace = TimeSpan.Zero;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetChunkComponent>(chunk).Dropped, Is.False,
                "The watchdog dropped a chunk whose hull was sitting right where it left it."));

        return chunk;
    }

    /// <summary>Anything mid-transit goes before the stack it was falling into.</summary>
    private static async Task Cleanup(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            foreach (var uid in new List<EntityUid> { FindChunk(entMan), site.Cracker })
            {
                if (uid != EntityUid.Invalid && entMan.EntityExists(uid))
                    entMan.DeleteEntity(uid);
            }
        });

        await server.WaitRunTicks(1);

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }
}
