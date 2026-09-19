#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.PlanetCracker.Chunk;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

[TestFixture]
[TestOf(typeof(WFCrackerSystem))]
public sealed class LifecycleRegressionTest
{
    [Test]
    public async Task ExistingExternalForceAnchorRefusesCrackAndRemainsOwned()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();
        var site = await BuildReadyToCut(pair);

        await server.WaitPost(() =>
        {
            var crackerComp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            var cracker = (site.Cracker, crackerComp);
            Assert.That(crackers.TryTarget(cracker, out _), Is.True);
            entMan.AddComponent<ForceAnchorComponent>(site.Cracker);
            var transform = server.System<SharedTransformSystem>();
            var position = transform.GetWorldPosition(site.Cracker);

            Assert.That(crackers.TryBegin(cracker, out var beginReason), Is.False);
            Assert.That(beginReason, Is.EqualTo("wf-crack-console-refuse-external-lock"));
            Assert.That(transform.GetWorldPosition(site.Cracker), Is.EqualTo(position));

            Assert.That(crackers.EngageLock(cracker, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo("wf-crack-console-refuse-external-lock"));
            Assert.That(crackerComp.Locked, Is.False);
            Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.True);
        });

        await Cleanup(pair, site);
    }

    [Test]
    public async Task PairFormedWhileHullIsIdleIsReconciledOnReturningToOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var site = await BuildCrackerInOrbit(pair);
        var elsewhere = await pair.CreateTestMap();

        await server.WaitPost(() => transform.SetCoordinates(
            site.Cracker, new EntityCoordinates(elsewhere.MapUid, new Vector2(20f, 20f))));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.Idle)));

        await DeployPair(pair, site, 0f, 24f, false);
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.Idle), "The pair event must not promote an off-orbit hull."));

        await server.WaitPost(() => transform.SetCoordinates(
            site.Cracker, new EntityCoordinates(site.Orbit, new Vector2(128f, 128f))));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.AnchorsPlaced),
                "Entering Surveying must reconcile the already-valid owned pair."));

        await Cleanup(pair, site);
    }

    [Test]
    public async Task FailedChunkTransitKeepsOwnershipAndCanRetry()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var chunks = server.System<WFPlanetChunkSystem>();
        var site = await BuildExtracted(pair);
        var chunk = EntityUid.Invalid;
        var elsewhere = await pair.CreateTestMap();

        await server.WaitAssertion(() => chunk = FindChunk(entMan));
        await server.WaitPost(() =>
            transform.SetCoordinates(chunk, new EntityCoordinates(elsewhere.MapUid, new Vector2(20f, 20f))));
        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            var chunkComp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);
            var crackerComp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            Assert.That(chunks.DropChunk((chunk, chunkComp)), Is.False);
            Assert.That(chunkComp.Dropped, Is.False);
            Assert.That(crackerComp.Chunk, Is.EqualTo(entMan.GetNetEntity(chunk)));
            Assert.That(entMan.HasComponent<ForceAnchorComponent>(chunk), Is.True);
            Assert.That(entMan.GetComponent<PhysicsComponent>(chunk).BodyType, Is.EqualTo(BodyType.Static));
        });

        await Cleanup(pair, site);
    }

    [Test]
    public async Task FailedReleaseStaysDisconnectingUntilChunkTransitCanRetry()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var crackers = server.System<WFCrackerSystem>();
        var site = await BuildDisconnecting(pair);
        var chunk = EntityUid.Invalid;
        var elsewhere = await pair.CreateTestMap();

        await server.WaitAssertion(() => chunk = FindChunk(entMan));
        await server.WaitPost(() => transform.SetCoordinates(
            chunk, new EntityCoordinates(elsewhere.MapUid, new Vector2(20f, 20f))));
        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
            crackers.ReleaseNow((site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker))));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var cracker = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            var chunkComp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cracker.State, Is.EqualTo(WFCrackState.Disconnecting));
                Assert.That(cracker.EvacRunning, Is.True);
                Assert.That(chunkComp.Dropped, Is.False);
                Assert.That(cracker.Chunk, Is.EqualTo(entMan.GetNetEntity(chunk)));
            }
        });

        await server.WaitPost(() => transform.SetCoordinates(
            chunk, new EntityCoordinates(site.Orbit, new Vector2(128f, 128f))));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.Released), "A later release attempt did not complete after transit became available."));

        await Cleanup(pair, site);
    }

    private static async Task Cleanup(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            foreach (var uid in new[] { FindChunk(entMan), site.Cracker })
            {
                if (uid.IsValid() && entMan.EntityExists(uid))
                    entMan.DeleteEntity(uid);
            }
        });

        await server.WaitRunTicks(1);
        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }
}
