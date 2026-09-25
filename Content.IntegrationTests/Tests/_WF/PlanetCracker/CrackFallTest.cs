#nullable enable
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server.Gravity;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>The cracker's fall: lock release, centrifuge lift cleared, and the push into transit.</summary>
[TestFixture]
[TestOf(typeof(WFCrackerSystem))]
public sealed class CrackFallTest
{
    /// <summary>The fall releases the lock and leaves the body Dynamic before entering transit.</summary>
    [Test]
    public async Task FallReleasesTheLockBeforeEnteringTransit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.True,
                    "Precondition: the cutting hull is force-anchored.");
                Assert.That(entMan.GetComponent<PhysicsComponent>(site.Cracker).BodyType,
                    Is.EqualTo(BodyType.Static), "Precondition: the locked hull is a static body.");
            }
        });

        var push = await PushFall(pair, site);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(push.BodyType, Is.Not.EqualTo(BodyType.Static),
                    "The hull was still static in the instant the push returned, so nothing would have moved.");
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.False,
                    "The fall left the force anchor on.");
                Assert.That(entMan.HasComponent<PreventGridAnchorChangesComponent>(site.Cracker), Is.False,
                    "The fall left the anchor-change block on, which pins the hull static for the whole drop.");
                Assert.That(entMan.GetComponent<PhysicsComponent>(site.Cracker).BodyType,
                    Is.Not.EqualTo(BodyType.Static), "The falling hull is still a static body.");
                Assert.That(comp.Locked, Is.False, "The hull still thinks it is locked.");
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Falling), "The hull is not falling.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The centrifuge stops counting as lift the moment the push happens.</summary>
    [Test]
    public async Task FallClearsTheCentrifugeLift()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GravityGeneratorComponent>(site.Centrifuge).GravityActive, Is.True,
                "Precondition: the charged centrifuge is carrying the hull.");
            Assert.That(zLevels.TryGetGravgenLoad(site.Cracker, out _, out var capacity), Is.True,
                "Precondition: the hull reports a gravgen load.");
            Assert.That(capacity, Is.GreaterThan(0f), "Precondition: the hull has lift to lose.");
        });

        await PushFall(pair, site);

        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.TryGetGravgenLoad(site.Cracker, out _, out var capacity), Is.True,
                "The hull stopped reporting a gravgen load at all.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<GravityGeneratorComponent>(site.Centrifuge).GravityActive, Is.False,
                    "The centrifuge is still counting as lift, so the hull would hover instead of drop.");
                Assert.That(capacity, Is.EqualTo(0f).Within(0.01f),
                    "The hull still reports pooled lift capacity after the push.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Fall puts the hull on a transit map with a faller, still descending two seconds later.</summary>
    [Test]
    public async Task FallPushesIntoTransit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<WFOrbitLayerComponent>(
                    entMan.GetComponent<TransformComponent>(site.Cracker).MapUid), Is.True,
                "Precondition: the cutting hull is parked on the orbit layer."));

        var push = await PushFall(pair, site);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(push.OnTransitMap, Is.True,
                "The hull is not on a transit map, so the push never happened.");
            Assert.That(push.LeftOrbit, Is.True, "The hull never left the orbit layer.");
            Assert.That(push.HasFaller, Is.True, "The push never made the hull a faller.");
            Assert.That(push.CrashVelocity, Is.GreaterThan(0f),
                "The hull has no crash velocity threshold for its landing to be measured against.");
        }

        // Without the lift-cache reset and fall seed, the hull would settle straight back onto the orbit layer.
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(site.Cracker);
            var mapUid = xform.MapUid;
            var onOrbit = entMan.HasComponent<WFOrbitLayerComponent>(mapUid);
            var onTransit = entMan.HasComponent<CEZTransitMapComponent>(mapUid);
            var progress = entMan.TryGetComponent(site.Cracker, out CEZPhysicsComponent? zPhys) ? zPhys.LocalPosition : -1f;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(onOrbit, Is.False, "The hull settled back onto the orbit layer after the push.");
                Assert.That(onTransit, Is.True, "Two seconds after the push the hull is neither in transit nor on the orbit layer.");
                Assert.That(progress, Is.LessThan(0.9f), $"The hull is not descending (progress {progress}).");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The cracker starts its fall at progress 1.0 exactly, which the chunk offsets from.</summary>
    [Test]
    public async Task FallUsesADistinctStartProgress()
    {
        await using var pair = await PoolManager.GetServerClient();

        var site = await BuildReadyToCut(pair);
        await BeginCut(pair, site);

        var push = await PushFall(pair, site);

        Assert.That(push.Progress, Is.EqualTo(1f).Within(0.001f),
            "The hull did not enter transit at the top of its level, so F5's 0.98 would not be distinct from it.");

        await Cleanup(pair, site);
    }

    /// <summary>Pushes the hull off its layer via the grace expiry path and reads the result in one callback.</summary>
    private static async Task<PushResult> PushFall(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();
        var result = new PushResult();

        await server.WaitPost(() =>
        {
            crackers.Fall((site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker)));

            var mapUid = entMan.GetComponent<TransformComponent>(site.Cracker).MapUid;

            result.OnTransitMap = entMan.HasComponent<CEZTransitMapComponent>(mapUid);
            result.LeftOrbit = !entMan.HasComponent<WFOrbitLayerComponent>(mapUid);
            result.HasFaller = entMan.TryGetComponent(site.Cracker, out CEZGridFallerComponent? faller);
            result.CrashVelocity = faller?.GridCrashVelocity ?? 0f;
            result.Progress = entMan.TryGetComponent(site.Cracker, out CEZPhysicsComponent? zPhys)
                ? zPhys.LocalPosition
                : -1f;
            result.BodyType = entMan.TryGetComponent(site.Cracker, out PhysicsComponent? body)
                ? body.BodyType
                : BodyType.Static;
        });

        await server.WaitRunTicks(1);
        return result;
    }

    /// <summary>The hull is mid-transit, so it goes before the stack it was falling into.</summary>
    private static async Task Cleanup(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;

        await server.WaitPost(() => server.EntMan.DeleteEntity(site.Cracker));
        await server.WaitRunTicks(1);

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>What the push looked like in the instant it happened, before a single tick could react to it.</summary>
    private sealed class PushResult
    {
        /// <summary>Whether the hull's map was a transit map.</summary>
        public bool OnTransitMap;

        /// <summary>Whether the hull's map was no longer the planet's orbit layer.</summary>
        public bool LeftOrbit;

        /// <summary>Whether the hull carried a faller.</summary>
        public bool HasFaller;

        /// <summary>The faller's crash threshold, or zero when there was no faller.</summary>
        public float CrashVelocity;

        /// <summary>Altitude within the transit level, 1 at the top and 0 at the bottom.</summary>
        public float Progress;

        /// <summary>The hull's body type, which must be Dynamic before the transit call.</summary>
        public BodyType BodyType;
    }
}
