#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.PlanetCracker.Cracker;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._NF.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Cracker.BUI;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Repairable;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;
using ServerCrackerSystem = Content.Server._WF.PlanetCracker.Cracker.WFCrackerSystem;
using ClientCrackerSystem = Content.Client._WF.PlanetCracker.Cracker.WFCrackerSystem;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// The crack console end to end: the geometry that has to resolve identically on both sides, the berth offset measured
/// across two maps, every targeting and begin-crack precondition, the snap and its refusals, and the state object the
/// window draws - read straight off the server's own builder rather than through a client window.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCrackConsoleWindow))]
public sealed class CrackConsoleTest
{
    /// <summary>Structural damage the projector's modifier set cannot soak, so the numbers land where they are aimed.</summary>
    private const string Blunt = "Blunt";

    /// <summary>Damage that clears the projector's 250 Breakage trigger.</summary>
    private const float ProjectorBreakingDamage = 280f;

    /// <summary>
    /// The abstract shared geometry base must be resolvable in both assemblies, and give the same answer in both.
    /// ReflectionManager skips abstract types and EntitySystemManager maps a base type only from a concrete subclass, so
    /// this passes only while a concrete WFCrackerSystem exists on the server AND on the client. Without the client one
    /// the throw is an UnregisteredTypeException inside Draw, which no other headless gate reaches.
    /// </summary>
    [Test]
    public async Task SharedGeometryResolvesOnBothSides()
    {
        // Connected, because a pooled client that is not in game has no entity systems registered at all.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;

        var serverGeometry = server.ResolveDependency<IEntityManager>().System<SharedWFCrackerSystem>();
        var clientGeometry = client.ResolveDependency<IEntityManager>().System<SharedWFCrackerSystem>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverGeometry, Is.Not.Null, "The server never registered the shared cracker geometry.");
            Assert.That(serverGeometry, Is.InstanceOf<ServerCrackerSystem>(),
                "The server's concrete cracker system is what registers the shared base.");
            Assert.That(clientGeometry, Is.Not.Null, "The client never registered the shared cracker geometry.");
            Assert.That(clientGeometry, Is.InstanceOf<ClientCrackerSystem>(),
                "The client's one-line concrete subclass is missing, so the abstract base is unresolvable.");
        }

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);
        await pair.RunTicksSync(10);

        var serverRect = default(Box2Rotated);

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<WFPlanetCrackerComponent>(cracker);
            Assert.That(serverGeometry.TryGetBerthRect((cracker, comp), out serverRect), Is.True,
                "The server could not derive the berth rectangle.");
        });

        await client.WaitAssertion(() =>
        {
            var uid = pair.ToClientUid(cracker);

            Assert.That(client.EntMan.TryGetComponent(uid, out WFPlanetCrackerComponent? comp), Is.True,
                "The hull never reached the client.");
            Assert.That(clientGeometry.TryGetBerthRect((uid, comp!), out var clientRect), Is.True,
                "The client could not derive the berth rectangle off the same hull.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(clientRect.Center.X, Is.EqualTo(serverRect.Center.X).Within(0.01f),
                    "The two sides disagree about where the berth is.");
                Assert.That(clientRect.Center.Y, Is.EqualTo(serverRect.Center.Y).Within(0.01f),
                    "The two sides disagree about where the berth is.");
                Assert.That(clientRect.Box.Width, Is.EqualTo(serverRect.Box.Width).Within(0.01f),
                    "The two sides disagree about how wide the berth is.");
                Assert.That(clientRect.Box.Height, Is.EqualTo(serverRect.Box.Height).Within(0.01f),
                    "The two sides disagree about how tall the berth is.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Draw smoke for the four diagrams and the window. A real DrawingHandleScreen cannot be obtained in a headless
    /// pair - ClydeHeadless.RenderNow is an empty body and RenderNow itself lives on the internal IClydeInternal, so
    /// no content assembly can reach a handle - therefore this drives every non-draw path instead: construction,
    /// dependency injection, state assignment, FrameUpdate and a Measure/Arrange pass, and asserts no throw. Each
    /// control's [Dependency] fields are checked through WFDiagramControl.DependenciesInjected, which is the specific
    /// failure the missing IoCManager.InjectDependencies call would cause.
    /// </summary>
    [Test]
    public async Task DiagramControlsSurviveAStateAndAFrame()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var state = SyntheticState();

        await pair.Client.WaitPost(() =>
        {
            var site = new WFSiteDiagram();
            var dial = new WFCentrifugeDial();
            var projectors = new WFProjectorPanel();
            var timeline = new WFCrackTimeline();
            var window = new WFCrackConsoleWindow();

            var controls = new WFDiagramControl[] { site, dial, projectors, timeline };

            using (Assert.EnterMultipleScope())
            {
                foreach (var control in controls)
                {
                    Assert.That(control.DependenciesInjected, Is.True,
                        $"{control.GetType().Name} never had IoCManager.InjectDependencies called on it.");
                }
            }

            site.SetState(state);
            timeline.SetState(state);
            projectors.SetState(state);
            dial.SetReadout(state.Spin, state.AtFull, state.Load, state.Capacity);

            Assert.DoesNotThrow(() => window.UpdateState(state),
                "The window should take a full console state without throwing.");

            var frame = new FrameEventArgs(1f / 60f);

            Assert.DoesNotThrow(() =>
            {
                foreach (var control in controls)
                {
                    control.FrameUpdateSmoke(frame);
                    control.Measure(new Vector2(400f, 300f));
                    control.Arrange(new UIBox2(0f, 0f, 400f, 300f));
                    control.FrameUpdateSmoke(frame);
                }

                window.Measure(new Vector2(640f, 560f));
                window.Arrange(new UIBox2(0f, 0f, 640f, 560f));
            }, "Laying out and updating the diagrams should not throw.");

            Assert.That(dial.Spin, Is.EqualTo(state.Spin).Within(0.0001f),
                "The dial should hold the spin it was fed.");

            window.Dispose();

            foreach (var control in controls)
            {
                control.Dispose();
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The centrifuge's own machine window, driven headlessly through every readout shape it has to survive: running,
    /// switched off, and overloaded past its rated capacity. A real DrawingHandleScreen cannot be obtained in a pooled
    /// pair, so this drives construction, both state paths and a Measure/Arrange pass instead.
    /// The dial is measured on its own in the same breath. Control's MeasureOverride returns zero for a leaf, so a
    /// custom-drawn control with no MeasureOverride of its own is invisible in any parent that does not pin it with a
    /// MinSize - which is exactly what this window's bottom slot is.
    /// </summary>
    [Test]
    public async Task CentrifugeWindowAndDialSurviveEveryReadout()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });

        await pair.Client.WaitPost(() =>
        {
            var window = new WFCentrifugeWindow();
            var size = window.SetSize;

            Assert.DoesNotThrow(() =>
            {
                Push(window, size, true, PowerChargePowerStatus.Charging, 45, 0.62f, false, 900f, 2000f);
                Push(window, size, false, PowerChargePowerStatus.Off, -1, 0f, false, 0f, 0f);
                Push(window, size, true, PowerChargePowerStatus.FullyCharged, -1, 1f, true, 2100f, 2000f);

                // No rated capacity at all: the load bar has to divide by nothing without throwing.
                Push(window, size, true, PowerChargePowerStatus.Discharging, 5, 0.4f, false, 300f, 0f);
            }, "The centrifuge window should take every state and readout shape without throwing.");

            var unbounded = new Vector2(float.PositiveInfinity, float.PositiveInfinity);

            var dial = new WFCentrifugeDial();
            var bare = new WFCentrifugeDial { ShowReadouts = false };

            dial.Measure(unbounded);
            bare.Measure(unbounded);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dial.DependenciesInjected, Is.True,
                    "WFCentrifugeDial never had IoCManager.InjectDependencies called on it.");
                Assert.That(dial.DesiredSize.X, Is.GreaterThan(0f),
                    "The dial measures as zero wide, so it vanishes in any parent that does not pin its size.");
                Assert.That(dial.DesiredSize.Y, Is.GreaterThan(0f),
                    "The dial measures as zero tall, so it vanishes in any parent that does not pin its size.");
                Assert.That(bare.DesiredSize.Y, Is.LessThan(dial.DesiredSize.Y),
                    "A dial with its readouts switched off still asks for room to draw them.");
            }

            window.Dispose();
            dial.Dispose();
            bare.Dispose();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>One power state plus one spin and load reading, then a layout pass at the window's own size.</summary>
    private static void Push(
        WFCentrifugeWindow window,
        Vector2 size,
        bool on,
        PowerChargePowerStatus status,
        short eta,
        float spin,
        bool atFull,
        float load,
        float capacity)
    {
        window.UpdateState(new PowerChargeState(on, 128, status, 1000, 2000, eta));
        window.UpdateReadout(spin, atFull, load, capacity);
        window.Measure(size);
        window.Arrange(new UIBox2(Vector2.Zero, size));
    }

    /// <summary>
    /// The berth carries the orbit map's id and the cut circle the ground map's, so the offset between them can only be
    /// a raw XY subtraction. MapCoordinates.InRange is asserted false in the same breath to pin why: it refuses
    /// differing MapIds before doing any distance maths at all.
    /// </summary>
    [Test]
    public async Task BerthOffsetIsMeasuredAcrossMaps()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, new Vector2(3f, -4f));

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            Assert.That(crackers.TryGetOwnedPair(cracker, out var a, out var b, true), Is.True,
                "Precondition: the hull owns a locked pair.");
            Assert.That(crackers.TryGetBerthOffset(cracker, a.Owner, b.Owner, out var offset), Is.True,
                "The berth offset could not be measured.");
            Assert.That(crackers.TryGetBerthCentre(cracker, out var berth), Is.True,
                "Precondition: the berth centre resolves.");
            Assert.That(crackers.TryGetCircle(a.Owner, b.Owner, out var circle, out _), Is.True,
                "Precondition: the pair has a cut circle.");

            var circleCoords = new MapCoordinates(circle, entMan.GetComponent<TransformComponent>(a.Owner).MapID);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(offset.X, Is.EqualTo(3f).Within(0.01f), "The offset is not the raw X delta.");
                Assert.That(offset.Y, Is.EqualTo(-4f).Within(0.01f), "The offset is not the raw Y delta.");
                Assert.That(berth.MapId, Is.Not.EqualTo(circleCoords.MapId),
                    "Precondition: the berth and the circle are on different z-layer maps.");
                Assert.That(berth.InRange(circleCoords, 1000f), Is.False,
                    "MapCoordinates.InRange returned true across two maps; the offset maths must not depend on it.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Twelve tiles of offset is refused and writes no target; six is accepted and writes both anchors.</summary>
    [Test]
    public async Task TargetingRefusesOutsideTolerance()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, new Vector2(12f, 0f));

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            Assert.That(cracker.Item2.State, Is.EqualTo(WFCrackState.AnchorsLocked),
                "Precondition: a drilled pair puts the hull at anchors-locked.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(crackers.TryTarget(cracker, out var reason), Is.False,
                    "Targeting was accepted twelve tiles out of alignment.");
                Assert.That(reason, Is.EqualTo("wf-crack-console-refuse-alignment"),
                    "The refusal did not name the alignment.");
                Assert.That(cracker.Item2.AnchorA, Is.Null, "A refused target still wrote the first anchor.");
                Assert.That(cracker.Item2.AnchorB, Is.Null, "A refused target still wrote the second anchor.");
                Assert.That(crackers.ComputeBlockers(cracker) & WFCrackBlocker.NotAligned,
                    Is.EqualTo(WFCrackBlocker.NotAligned), "The blocker list does not report the misalignment.");
            }
        });

        await AlignHull(pair, site, new Vector2(6f, 0f));

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            Assert.That(crackers.TryTarget(cracker, out _), Is.True,
                "Targeting was refused six tiles out, inside the eight-tile tolerance.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cracker.Item2.AnchorA, Is.Not.Null, "An accepted target wrote no first anchor.");
                Assert.That(cracker.Item2.AnchorB, Is.Not.Null, "An accepted target wrote no second anchor.");
                Assert.That(crackers.ComputeBlockers(cracker) & WFCrackBlocker.NotAligned,
                    Is.EqualTo(WFCrackBlocker.None), "The blocker list still reports a misalignment.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The whole begin path with every precondition met: the hull slides the last six tiles so its berth centre lands on
    /// the cut circle, then force-anchors itself static. Snap BEFORE lock, or a static body makes its own CE grid network
    /// static-anchored and the sync system pins the move straight back.
    /// </summary>
    [Test]
    public async Task BeginCrackSnapsTheHullOntoTheCircle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, new Vector2(6f, 0f));
        await Energise(pair, site.Cracker);

        var before = Vector2.Zero;
        var expected = Vector2.Zero;

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            Assert.That(entMan.GetComponent<WFCentrifugeComponent>(site.Centrifuge).AtFull, Is.True,
                "Precondition: the centrifuge is at full spin.");
            Assert.That(crackers.TryTarget(cracker, out _), Is.True, "Precondition: the pair targets.");

            before = transform.GetWorldPosition(site.Cracker);

            Assert.That(crackers.TryGetTargetedPair(cracker, out var a, out var b), Is.True,
                "Precondition: the targeted pair resolves.");
            Assert.That(crackers.TryGetBerthOffset(cracker, a.Owner, b.Owner, out expected), Is.True,
                "Precondition: the berth offset resolves.");
            Assert.That(crackers.ComputeBlockers(cracker), Is.EqualTo(WFCrackBlocker.None),
                "Precondition: nothing is blocking the begin.");
        });

        var begun = false;
        string? refusal = null;

        await server.WaitPost(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));
            begun = crackers.TryBegin(cracker, out refusal);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            var cracker = (site.Cracker, comp);
            var after = transform.GetWorldPosition(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(begun, Is.True, $"The begin was refused: {refusal}");
                Assert.That(after.X - before.X, Is.EqualTo(expected.X).Within(0.01f),
                    "The hull did not move by exactly the berth offset in X.");
                Assert.That(after.Y - before.Y, Is.EqualTo(expected.Y).Within(0.01f),
                    "The hull did not move by exactly the berth offset in Y.");

                Assert.That(crackers.TryGetTargetedPair(cracker, out var a, out var b), Is.True,
                    "The hull lost its target across the snap.");
                Assert.That(crackers.TryGetBerthOffset(cracker, a.Owner, b.Owner, out var residual), Is.True,
                    "The berth offset stopped resolving after the snap.");
                Assert.That(residual.Length(), Is.LessThan(0.01f),
                    "The berth centre did not land on the cut circle centre.");

                Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.True,
                    "The hull was never force-anchored.");
                Assert.That(entMan.HasComponent<PreventGridAnchorChangesComponent>(site.Cracker), Is.True,
                    "ForceAnchorSystem's map-init path never ran, so the anchor changes are not blocked.");
                Assert.That(entMan.GetComponent<PhysicsComponent>(site.Cracker).BodyType, Is.EqualTo(BodyType.Static),
                    "The locked hull is not a static body.");
                Assert.That(comp.Locked, Is.True, "The hull does not know it applied its own lock.");
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracking), "The cut did not start.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A docked transport has to arrive with the hull. The dock joint is a soft weld, so a grid left where it was is
    /// dragged across the map over about a second instead of being placed - which is the bug the reapply-and-redock step
    /// exists to avoid.
    /// </summary>
    [Test]
    public async Task SnapCarriesTheDockedTransport()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();
        var docking = server.System<DockingSystem>();
        var shuttles = server.System<ShuttleSystem>();
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, new Vector2(6f, 0f));
        await Energise(pair, site.Cracker);

        var orbitMap = MapId.Nullspace;
        await server.WaitAssertion(() => orbitMap = entMan.GetComponent<TransformComponent>(site.Cracker).MapID);

        var transport = await BuildTransport(pair, orbitMap, new Vector2(400f, 400f));

        // Park the transport so its own airlock sits where the hull's does, then weld the two ports together: the dock
        // is created at whatever relative pose the grids are in, so an aligned pair leaves the weld at rest.
        await server.WaitPost(() =>
        {
            var crackerDock = FindDock(entMan, site.Cracker);
            var transportDock = FindDock(entMan, transport);

            Assert.That(crackerDock, Is.Not.EqualTo(EntityUid.Invalid), "The cracker hull has no docking port.");
            Assert.That(transportDock, Is.Not.EqualTo(EntityUid.Invalid), "The transport has no docking port.");

            var wanted = transform.GetWorldPosition(crackerDock) - transform.GetWorldPosition(transportDock);
            transform.SetWorldPosition(transport, transform.GetWorldPosition(transport) + wanted);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitPost(() =>
        {
            var crackerDock = FindDock(entMan, site.Cracker);
            var transportDock = FindDock(entMan, transport);

            docking.Dock(
                (crackerDock, entMan.GetComponent<DockingComponent>(crackerDock)),
                (transportDock, entMan.GetComponent<DockingComponent>(transportDock)));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        var relative = Vector2.Zero;

        await server.WaitAssertion(() =>
        {
            var docked = new HashSet<EntityUid>();
            shuttles.GetAllDockedShuttles(site.Cracker, docked);

            Assert.That(docked, Does.Contain(transport), "Precondition: the transport is docked to the hull.");
            Assert.That(entMan.GetComponent<DockingComponent>(FindDock(entMan, transport)).Docked, Is.True,
                "Precondition: the transport's port reports itself docked.");

            relative = transform.GetWorldPosition(transport) - transform.GetWorldPosition(site.Cracker);

            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));
            Assert.That(crackers.TryTarget(cracker, out _), Is.True, "Precondition: the pair targets.");
        });

        await server.WaitPost(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));
            Assert.That(crackers.TryBegin(cracker, out var reason), Is.True, $"The begin was refused: {reason}");
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var now = transform.GetWorldPosition(transport) - transform.GetWorldPosition(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(now.X, Is.EqualTo(relative.X).Within(0.05f),
                    "The docked transport was left behind in X instead of being carried.");
                Assert.That(now.Y, Is.EqualTo(relative.Y).Within(0.05f),
                    "The docked transport was left behind in Y instead of being carried.");
                Assert.That(entMan.GetComponent<DockingComponent>(FindDock(entMan, transport)).Docked, Is.True,
                    "The snap broke the dock joint.");
            }
        });

        await server.WaitPost(() => entMan.DeleteEntity(transport));
        await server.WaitRunTicks(1);

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull in a CE grid network of two or more grids cannot be moved at all: the sync system intercepts every
    /// transform move and re-applies the old pose, while the state machine would have advanced anyway. It is refused
    /// rather than nudged.
    /// </summary>
    [Test]
    public async Task SnapIsRefusedInAGridNetwork()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, new Vector2(6f, 0f));
        await Energise(pair, site.Cracker);

        var orbitMap = MapId.Nullspace;
        await server.WaitAssertion(() => orbitMap = entMan.GetComponent<TransformComponent>(site.Cracker).MapID);

        var second = await BuildTransport(pair, orbitMap, new Vector2(600f, 600f));

        var before = Vector2.Zero;

        // The network is built and used inside ONE server callback on purpose. CEZGridConnectorSystem reconciles live
        // membership against the connectors it can actually see, and two grids with no connector between them are
        // removed again on its very next Update - so a hand-built network does not survive a tick boundary.
        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.TryGetGridNetwork(site.Cracker, out _), Is.False,
                "Precondition: the hull starts out in no network.");
            Assert.That(zLevels.TryGetGridNetwork(second, out _), Is.False,
                "Precondition: the second grid starts out in no network.");

            var built = zLevels.CreateGridNetwork();
            zLevels.TryAddGridToNetwork(built, site.Cracker);
            zLevels.TryAddGridToNetwork(built, second);

            Assert.That(zLevels.TryGetGridNetwork(site.Cracker, out var network), Is.True,
                "Precondition: the hull is in a grid network.");
            Assert.That(network.Comp.Grids, Has.Count.GreaterThanOrEqualTo(2),
                "Precondition: the network holds two grids.");

            before = transform.GetWorldPosition(site.Cracker);

            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(crackers.ComputeBlockers(cracker) & WFCrackBlocker.InGridNetwork,
                    Is.EqualTo(WFCrackBlocker.InGridNetwork), "The network is not reported as a blocker.");
                Assert.That(crackers.TryBegin(cracker, out var reason), Is.False,
                    "The begin was accepted for a hull in a grid network.");
                Assert.That(reason, Is.EqualTo("wf-crack-console-refuse-network"),
                    "The refusal did not name the grid network.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var after = transform.GetWorldPosition(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(after.X, Is.EqualTo(before.X).Within(0.01f), "The refused snap moved the hull in X.");
                Assert.That(after.Y, Is.EqualTo(before.Y).Within(0.01f), "The refused snap moved the hull in Y.");
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.False,
                    "The refused begin still locked the hull.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                    Is.EqualTo(WFCrackState.AnchorsLocked), "The refused begin still advanced the stage.");
            }
        });

        await server.WaitPost(() => entMan.DeleteEntity(second));
        await server.WaitRunTicks(1);

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Another grid over the destination footprint refuses the begin before any state change.</summary>
    [Test]
    public async Task SnapIsRefusedWhenTheDestinationIsBlocked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();
        var maps = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, new Vector2(6f, 0f));
        await Energise(pair, site.Cracker);

        var blocker = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(site.Cracker);
            var grid = mapMan.CreateGridEntity(xform.MapID);
            var floor = new Tile(tileDefs[FloorTile].TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

            for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            maps.SetTiles(grid.Owner, grid.Comp, tiles);
            blocker = grid.Owner;

            // Straight over the middle of where the hull is about to be, six tiles east of where it is now.
            transform.SetWorldPosition(blocker, transform.GetWorldPosition(site.Cracker) + new Vector2(17f, 7f));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(crackers.IsDestinationClear(cracker, new Vector2(6f, 0f)), Is.False,
                    "The clear-area check did not see the grid over the destination.");
                Assert.That(crackers.ComputeBlockers(cracker) & WFCrackBlocker.Obstructed,
                    Is.EqualTo(WFCrackBlocker.Obstructed), "The obstruction is not reported as a blocker.");
                Assert.That(crackers.TryBegin(cracker, out var reason), Is.False,
                    "The begin was accepted into an occupied destination.");
                Assert.That(reason, Is.EqualTo("wf-crack-console-refuse-obstructed"),
                    "The refusal did not name the obstruction.");
                Assert.That(cracker.Item2.State, Is.EqualTo(WFCrackState.AnchorsLocked),
                    "The refused begin still advanced the stage.");
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.False,
                    "The refused begin still locked the hull.");
            }
        });

        await server.WaitPost(() => entMan.DeleteEntity(blocker));
        await server.WaitRunTicks(1);

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// ShuttleSystem.Enable resolves ShuttleComponent as a required dependency and returns silently without one, while
    /// Disable does not, so a hull with no ShuttleComponent would force-anchor and never be releasable - no log, a dead
    /// abort path and a fall that never starts. It is refused instead.
    /// </summary>
    [Test]
    public async Task LockIsRefusedWithoutShuttleComponent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, new Vector2(6f, 0f));
        await Energise(pair, site.Cracker);

        await server.WaitPost(() => entMan.RemoveComponent<ShuttleComponent>(site.Cracker));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            Assert.That(crackers.TryTarget(cracker, out _), Is.True, "Precondition: the pair targets.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(crackers.TryBegin(cracker, out var reason), Is.False,
                    "A hull with no ShuttleComponent was allowed to lock itself.");
                Assert.That(reason, Is.EqualTo("wf-crack-console-refuse-no-shuttle"),
                    "The refusal did not name the missing shuttle component.");
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(site.Cracker), Is.False,
                    "The hull was force-anchored despite being unreleasable.");
                Assert.That(cracker.Item2.Locked, Is.False, "The hull recorded a lock it never applied.");
                Assert.That(cracker.Item2.State, Is.EqualTo(WFCrackState.AnchorsLocked),
                    "The refused begin still advanced the stage.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// One flag per fault, and BEGIN CRACK only when the whole list is clear: a rotor below full, a projector without
    /// power and a broken projector each block it on their own and each name themselves.
    /// </summary>
    [Test]
    public async Task BeginRequiresCentrifugeAtFullAndBothProjectors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();
        var damageable = server.System<DamageableSystem>();
        var receiver = server.System<SharedPowerReceiverSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, new Vector2(6f, 0f));

        // A cold rotor is the shipped state: charge: 0 in the prototype, so this needs no arranging.
        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(crackers.ComputeBlockers(cracker) & WFCrackBlocker.CentrifugeNotFull,
                    Is.EqualTo(WFCrackBlocker.CentrifugeNotFull), "A cold rotor is not reported as a blocker.");
                Assert.That(crackers.TryTarget(cracker, out _), Is.True, "Precondition: the pair still targets.");
                Assert.That(crackers.TryBegin(cracker, out _), Is.False, "The cut began on a cold rotor.");
            }
        });

        await Energise(pair, site.Cracker);
        await FreezeCharge(pair, site.Centrifuge);

        // Below FullOn, so the latch never closes.
        await SetCharge(pair, site.Centrifuge, 0.9f);

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));
            Assert.That(crackers.ComputeBlockers(cracker) & WFCrackBlocker.CentrifugeNotFull,
                Is.EqualTo(WFCrackBlocker.CentrifugeNotFull), "Ninety percent spin is not reported as short of full.");
        });

        await SetCharge(pair, site.Centrifuge, 1f);

        // A hull machine runs without cabling because the factory clears NeedsPower; putting it back leaves the
        // projector on an ungenerated net, which is exactly a brownout.
        await server.WaitPost(() => receiver.SetNeedsPower(site.Projectors[0], true));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(crackers.ComputeBlockers(cracker) & WFCrackBlocker.CentrifugeNotFull,
                    Is.EqualTo(WFCrackBlocker.None), "The rotor is still reported short of full at full charge.");
                Assert.That(crackers.ComputeBlockers(cracker) & WFCrackBlocker.ProjectorsUnpowered,
                    Is.EqualTo(WFCrackBlocker.ProjectorsUnpowered), "An unpowered projector is not a blocker.");
                Assert.That(crackers.TryBegin(cracker, out var reason), Is.False,
                    "The cut began with an unpowered projector.");
                Assert.That(reason, Is.EqualTo("wf-crack-console-blocker-projectors-unpowered"),
                    "The refusal did not name the unpowered projector.");
            }
        });

        await server.WaitPost(() => receiver.SetNeedsPower(site.Projectors[0], false));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitPost(() => damageable.TryChangeDamage(site.Projectors[1],
            new DamageSpecifier(proto.Index<DamageTypePrototype>(Blunt), FixedPoint2.New(ProjectorBreakingDamage)), true));

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityProjectorComponent>(site.Projectors[1]).Broken, Is.True,
                    "Precondition: the projector passed its breakage threshold.");
                Assert.That(crackers.ComputeBlockers(cracker) & WFCrackBlocker.ProjectorsBroken,
                    Is.EqualTo(WFCrackBlocker.ProjectorsBroken), "A broken projector is not a blocker.");
                Assert.That(crackers.TryBegin(cracker, out var reason), Is.False,
                    "The cut began with a broken projector.");
                Assert.That(reason, Is.EqualTo("wf-crack-console-blocker-projectors-broken"),
                    "The refusal did not name the broken projector.");
            }
        });

        await server.WaitPost(() =>
        {
            var projector = site.Projectors[1];
            damageable.SetAllDamage(projector, entMan.GetComponent<DamageableComponent>(projector), 0);
            var repaired = new RepairedEvent(
                (projector, entMan.GetComponent<RepairableComponent>(projector)),
                projector);
            entMan.EventBus.RaiseLocalEvent(projector, ref repaired);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(crackers.ComputeBlockers(cracker), Is.EqualTo(WFCrackBlocker.None),
                    "Everything is fixed but the blocker list is not empty.");
                Assert.That(crackers.TryBegin(cracker, out var reason), Is.True,
                    $"The cut was refused with everything clear: {reason}");
                Assert.That(cracker.Item2.State, Is.EqualTo(WFCrackState.Cracking), "The cut did not start.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Design D23: once the cut has begun there is no way back. Untargeting is refused and leaves both anchors and the
    /// stage alone, and no abort message exists in the shared vocabulary at all for anything else to send.
    /// </summary>
    [Test]
    public async Task ThereIsNoAbortAfterBegin()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, new Vector2(6f, 0f));
        await Energise(pair, site.Cracker);

        await server.WaitPost(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));
            Assert.That(crackers.TryTarget(cracker, out _), Is.True, "Precondition: the pair targets.");
            Assert.That(crackers.TryBegin(cracker, out var reason), Is.True, $"Precondition: the cut began: {reason}");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            var cracker = (site.Cracker, comp);
            var a = comp.AnchorA;
            var b = comp.AnchorB;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracking), "Precondition: the cut is running.");
                Assert.That(crackers.TryUntarget(cracker, out var reason), Is.False,
                    "Untargeting was accepted mid-cut.");
                Assert.That(reason, Is.EqualTo("wf-crack-console-refuse-no-abort"),
                    "The refusal did not name the missing abort.");
                Assert.That(comp.AnchorA, Is.EqualTo(a), "The refused untarget dropped the first anchor.");
                Assert.That(comp.AnchorB, Is.EqualTo(b), "The refused untarget dropped the second anchor.");
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracking), "The refused untarget moved the stage.");
            }

            // The vocabulary itself: only the three messages exist, so nothing a client can send could abort.
            var messages = typeof(WFCrackTargetMessage).Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(WFCrackTargetMessage).Namespace
                               && typeof(BoundUserInterfaceMessage).IsAssignableFrom(type))
                .Select(type => type.Name)
                .ToList();

            Assert.That(messages, Is.EquivalentTo(new[]
                {
                    nameof(WFCrackTargetMessage),
                    nameof(WFCrackUntargetMessage),
                    nameof(WFCrackBeginMessage),
                }),
                $"The crack console's message vocabulary changed: {string.Join(", ", messages)}");
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every field of the state object against the components it was built from, read straight off the server's builder
    /// with no client window standing up.
    /// </summary>
    [Test]
    public async Task StateObjectMatchesTheServer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();
        var receiver = server.System<SharedPowerReceiverSystem>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, new Vector2(6f, 0f));
        await Energise(pair, site.Cracker);

        await server.WaitPost(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));
            Assert.That(crackers.TryTarget(cracker, out _), Is.True, "Precondition: the pair targets.");
            Assert.That(crackers.TryBegin(cracker, out var reason), Is.True, $"Precondition: the cut began: {reason}");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            var cracker = (site.Cracker, comp);
            var centrifuge = entMan.GetComponent<WFCentrifugeComponent>(site.Centrifuge);
            var state = ReadConsoleState(pair, site);

            Assert.That(crackers.TryGetBerthRect(cracker, out var rect), Is.True, "Precondition: the berth resolves.");
            Assert.That(crackers.TryGetTargetedPair(cracker, out var a, out var b), Is.True,
                "Precondition: the targeted pair resolves.");
            Assert.That(crackers.TryGetCircle(a.Owner, b.Owner, out var circle, out var radius), Is.True,
                "Precondition: the circle resolves.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(state.Cracker, Is.EqualTo(entMan.GetNetEntity(site.Cracker)), "Wrong hull.");
                Assert.That(state.State, Is.EqualTo(comp.State), "Wrong stage.");
                Assert.That(state.AnchorA, Is.EqualTo(comp.AnchorA), "Wrong first anchor.");
                Assert.That(state.AnchorB, Is.EqualTo(comp.AnchorB), "Wrong second anchor.");
                Assert.That(state.AnchorAState, Is.EqualTo(a.Comp.State), "Wrong first anchor state.");
                Assert.That(state.AnchorBState, Is.EqualTo(b.Comp.State), "Wrong second anchor state.");
                Assert.That(state.AnchorADamaged, Is.EqualTo(a.Comp.Damaged), "Wrong first anchor damage flag.");
                Assert.That(state.AnchorBDamaged, Is.EqualTo(b.Comp.Damaged), "Wrong second anchor damage flag.");

                Assert.That(state.BerthCentre.X, Is.EqualTo(rect.Center.X).Within(0.01f), "Wrong berth centre X.");
                Assert.That(state.BerthCentre.Y, Is.EqualTo(rect.Center.Y).Within(0.01f), "Wrong berth centre Y.");
                Assert.That(state.BerthHalfExtents.X, Is.EqualTo(rect.Box.Width / 2f).Within(0.01f),
                    "Wrong berth half-extent X.");
                Assert.That(state.BerthHalfExtents.Y, Is.EqualTo(rect.Box.Height / 2f).Within(0.01f),
                    "Wrong berth half-extent Y.");
                Assert.That(state.BerthRotation.Theta, Is.EqualTo(rect.Rotation.Theta).Within(0.01d),
                    "Wrong berth rotation.");
                Assert.That(state.HullAabb, Is.EqualTo(entMan.GetComponent<MapGridComponent>(site.Cracker).LocalAABB),
                    "Wrong hull bounds.");

                Assert.That(state.CircleCentre.X, Is.EqualTo(circle.X).Within(0.01f), "Wrong circle centre X.");
                Assert.That(state.CircleCentre.Y, Is.EqualTo(circle.Y).Within(0.01f), "Wrong circle centre Y.");
                Assert.That(state.CircleRadius, Is.EqualTo(radius).Within(0.01f), "Wrong circle radius.");
                Assert.That(state.AlignTolerance, Is.EqualTo(comp.AlignTolerance).Within(0.01f), "Wrong tolerance.");
                Assert.That(state.BerthOffset.Length(), Is.LessThan(0.01f),
                    "The snapped hull should report no residual offset.");
                Assert.That(state.Aligned, Is.True, "A snapped hull should read as aligned.");

                Assert.That(state.CrackTotal, Is.EqualTo(comp.CrackDuration), "Wrong full crack duration.");
                Assert.That(state.CrackRemaining, Is.LessThanOrEqualTo(comp.CrackDuration),
                    "The remainder is longer than the whole cut.");
                Assert.That(state.CrackRemaining, Is.GreaterThan(TimeSpan.Zero), "The cut reads as already finished.");
                Assert.That(state.CrackPaused, Is.EqualTo(comp.CrackPaused), "Wrong pause flag.");
                Assert.That(state.GraceRunning, Is.EqualTo(comp.GraceRunning), "Wrong grace flag.");
                Assert.That(state.GraceRemaining, Is.EqualTo(TimeSpan.Zero), "A nominal hull has a grace countdown.");
                Assert.That(state.Failing, Is.EqualTo(comp.Failing), "Wrong failure flags.");
                Assert.That(state.AbortRemaining, Is.EqualTo(TimeSpan.Zero), "A nominal hull is spinning down.");
                Assert.That(state.PendingAbort, Is.Null, "A nominal hull has a pending abort.");

                Assert.That(state.Spin, Is.EqualTo(centrifuge.Spin).Within(0.0001f), "Wrong spin.");
                Assert.That(state.AtFull, Is.EqualTo(centrifuge.AtFull), "Wrong at-full latch.");
                Assert.That(state.Load, Is.EqualTo(centrifuge.Load).Within(0.01f), "Wrong load.");
                Assert.That(state.Capacity, Is.EqualTo(centrifuge.Capacity).Within(0.01f), "Wrong capacity.");

                Assert.That(state.Projectors, Has.Count.EqualTo(site.Projectors.Count),
                    "The state does not carry a row per projector.");
                Assert.That(state.Blockers, Is.EqualTo(crackers.ComputeBlockers(cracker)), "Wrong blocker flags.");
                Assert.That(state.CanTarget, Is.False, "A cutting hull offers a target button.");
                Assert.That(state.CanUntarget, Is.False, "A cutting hull offers an untarget button.");
                Assert.That(state.CanBegin, Is.False, "A cutting hull offers a begin button.");
            }

            for (var i = 0; i < site.Projectors.Count; i++)
            {
                var projector = site.Projectors[i];
                var row = state.Projectors[i];

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(row.State, Is.EqualTo(entMan.GetComponent<WFGravityProjectorComponent>(projector).State),
                        $"Projector row {i} reports the wrong state.");
                    Assert.That(row.Powered, Is.EqualTo(receiver.IsPowered(projector)),
                        $"Projector row {i} reports the wrong power.");
                    Assert.That(row.Broken, Is.EqualTo(entMan.GetComponent<WFGravityProjectorComponent>(projector).Broken),
                        $"Projector row {i} reports the wrong broken flag.");
                    Assert.That(row.GridLocalPos,
                        Is.EqualTo(entMan.GetComponent<TransformComponent>(projector).LocalPosition),
                        $"Projector row {i} reports the wrong position.");
                }
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Grid membership is not a PVS exemption: net.pvs_range is 25 tiles and the berth marker sits well past that on a
    /// MarkerBase prototype nobody stands near, so the state has to carry the derived geometry rather than let the client
    /// re-derive it. With the marker moved past that range the state still names a real berth rectangle and a row for
    /// every projector.
    /// DEVIATION FROM THE PLAN: the other half of the plan's test - "assert the client entity set does NOT contain those
    /// entities" - cannot be asserted here, because the integration pool disables PVS outright
    /// (Robust.UnitTesting/Pool/PoolManager.cs sets net.pvs false), so a pooled client holds every entity regardless of
    /// range. The distance precondition below stands in for it.
    /// </summary>
    [Test]
    public async Task StateIsSelfSufficientForAFarConsole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<ServerCrackerSystem>();
        var transform = server.System<SharedTransformSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);

        // A long spur of deck so the marker can be wrenched down well past the PVS range from the console. It has to
        // stay on real plating and stay anchored: an unanchored entity over empty deck is a faller on a z-network.
        await LayTiles(pair, site.Cracker, new Vector2i(0, 15), new Vector2i(14, 50));

        var berth = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            Assert.That(comp.Berth, Is.Not.Null, "Precondition: the hull resolved its berth marker.");

            berth = entMan.GetEntity(comp.Berth!.Value);
            transform.Unanchor(berth, entMan.GetComponent<TransformComponent>(berth));
            transform.SetLocalPosition(berth, new Vector2(7.5f, 45.5f));
            transform.AnchorEntity(berth);
            crackers.UpdateBerthPose((site.Cracker, comp));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            var range = cfg.GetCVar(CVars.NetMaxUpdateRange);

            Assert.That(entMan.EntityExists(berth), Is.True, "Precondition: the moved berth marker still exists.");
            var distance = (transform.GetWorldPosition(berth) - transform.GetWorldPosition(site.Console)).Length();

            var state = ReadConsoleState(pair, site);

            Assert.That(crackers.TryGetBerthRect((site.Cracker, comp), out var rect), Is.True,
                "Precondition: the berth rectangle still resolves.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(distance, Is.GreaterThan(range),
                    $"Precondition: the marker has to be further than net.pvs_range ({range}) from the console.");
                Assert.That(state.BerthHalfExtents.X, Is.GreaterThan(0f),
                    "The state carries no berth rectangle, so a far console would draw an empty site diagram.");
                Assert.That(state.BerthCentre.X, Is.EqualTo(rect.Center.X).Within(0.01f),
                    "The state's berth centre does not match the server's own derivation.");
                Assert.That(state.BerthCentre.Y, Is.EqualTo(rect.Center.Y).Within(0.01f),
                    "The state's berth centre does not match the server's own derivation.");
                Assert.That(state.Projectors, Has.Count.EqualTo(site.Projectors.Count),
                    "The state dropped a projector row, so the console panel would be short.");
                // The radar ghost has no BUI state, so it draws the rectangle straight off this field: it has to be the
                // berth CENTRE in grid-local terms, not the marker pose, and the marker-to-centre distance is not
                // networked for anyone to add back.
                var ghostCentre = Vector2.Transform(comp.BerthLocalPos, transform.GetWorldMatrix(site.Cracker));

                Assert.That(ghostCentre.X, Is.EqualTo(rect.Center.X).Within(0.01f),
                    "The grid component holds the marker pose, so the radar ghost draws the berth in the wrong place.");
                Assert.That(ghostCentre.Y, Is.EqualTo(rect.Center.Y).Within(0.01f),
                    "The grid component holds the marker pose, so the radar ghost draws the berth in the wrong place.");
            }
        });

        await Teardown(pair, site.Layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The docking port on a hull, or Invalid.</summary>
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
    /// A console state with non-trivial geometry: a rotated berth, a cut circle offset from it across two maps, a
    /// locked pair, a running cut and two projectors, one of them firing.
    /// </summary>
    /// <summary>
    /// Every crack stage through the console window and its stage strip. The strip used to place nine stage names by
    /// hand at fixed pixel offsets, so they drew through each other and through the bar under them; it is a strip of
    /// real controls now, and this drives all nine stages plus each countdown shape that rides on them, asserts no
    /// throw, and asserts the strip measures itself rather than relying on the window's MinSize.
    /// </summary>
    [Test]
    public async Task CrackConsoleWindowSurvivesEveryStage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });

        await pair.Client.WaitPost(() =>
        {
            var window = new WFCrackConsoleWindow();
            var timeline = new WFCrackTimeline();
            var size = window.SetSize;

            Assert.DoesNotThrow(() =>
            {
                foreach (var stage in Enum.GetValues<WFCrackState>())
                {
                    var state = SyntheticState();

                    state.State = stage;
                    state.CrackPaused = stage == WFCrackState.Cracking;
                    state.GraceRunning = stage == WFCrackState.Cracked;
                    state.DisconnectArmed = stage == WFCrackState.Disconnecting;
                    state.DisconnectRemaining = TimeSpan.FromSeconds(24);
                    state.EvacRunning = stage == WFCrackState.Released;
                    state.EvacRemaining = TimeSpan.FromSeconds(41);
                    state.PendingAbort = stage == WFCrackState.Surveying ? WFCrackState.Idle : null;

                    window.UpdateState(state);
                    timeline.SetState(state);

                    window.Measure(size);
                    window.Arrange(new UIBox2(0f, 0f, size.X, size.Y));
                    timeline.Measure(size);
                    timeline.Arrange(new UIBox2(0f, 0f, size.X, size.Y));
                }

                // No state at all: the strip is built and laid out before the console has pushed one.
                timeline.SetState(null);
                timeline.Measure(size);
            }, "The crack console window should take every crack stage without throwing.");

            timeline.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(timeline.DependenciesInjected, Is.True,
                    "WFCrackTimeline never had IoCManager.InjectDependencies called on it.");
                Assert.That(timeline.DesiredSize.X, Is.GreaterThan(0f),
                    "The stage strip measures as zero wide, so it vanishes in any parent that does not pin its size.");
                Assert.That(timeline.DesiredSize.Y, Is.GreaterThan(0f),
                    "The stage strip measures as zero tall, so it vanishes in any parent that does not pin its size.");
                Assert.That(window.MinSize.X, Is.GreaterThanOrEqualTo(timeline.DesiredSize.X),
                    "The window cannot be shrunk narrower than the stage strip, or the strip is clipped away.");
            }

            window.Dispose();
            timeline.Dispose();
        });

        await pair.CleanReturnAsync();
    }

    private static WFCrackConsoleState SyntheticState()
    {
        return new WFCrackConsoleState
        {
            State = WFCrackState.Cracking,
            Cracker = new NetEntity(1),
            AnchorA = new NetEntity(2),
            AnchorB = new NetEntity(3),
            CandidateA = new NetEntity(2),
            CandidateB = new NetEntity(3),
            AnchorAPos = new Vector2(-8f, 4f),
            AnchorBPos = new Vector2(8f, -4f),
            AnchorAState = WFAnchorState.Locked,
            AnchorBState = WFAnchorState.Locked,
            AnchorADamaged = false,
            AnchorBDamaged = true,
            BerthCentre = new Vector2(1.5f, -2.5f),
            BerthHalfExtents = new Vector2(24f, 24f),
            BerthRotation = Angle.FromDegrees(37f),
            HullAabb = new Box2(-7.5f, -7.5f, 7.5f, 7.5f),
            CircleCentre = new Vector2(0f, 0f),
            CircleRadius = 11.5f,
            BerthOffset = new Vector2(-1.5f, 2.5f),
            AlignTolerance = 8f,
            Aligned = true,
            CrackRemaining = TimeSpan.FromSeconds(184),
            CrackTotal = TimeSpan.FromSeconds(720),
            CrackPaused = true,
            GraceRemaining = TimeSpan.FromSeconds(97),
            GraceRunning = true,
            Failing = WFCrackFailure.ProjectorPower | WFCrackFailure.Centrifuge,
            AbortRemaining = TimeSpan.FromSeconds(12),
            PendingAbort = WFCrackState.AnchorsPlaced,
            Spin = 0.83f,
            AtFull = false,
            Load = 2100f,
            Capacity = 3000f,
            Projectors = new List<WFProjectorRow>
            {
                new(new Vector2(-4f, 2f), WFProjectorState.Firing, true, false),
                new(new Vector2(3f, -1f), WFProjectorState.Off, false, true),
            },
            Blockers = WFCrackBlocker.WrongState | WFCrackBlocker.ProjectorsUnpowered |
                       WFCrackBlocker.CentrifugeNotFull,
            CanTarget = false,
            CanUntarget = false,
            CanBegin = false,
        };
    }
}
