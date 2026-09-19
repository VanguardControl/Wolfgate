#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Server._WF.PlanetCracker.Testing;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Gravity;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// The two code-built test hulls: their exact contents and layout, the masses the pooled gravgen check weighs them
/// with, the D11 virtual mass a carried anchor adds, and the ownership stamp both spawn paths have to apply.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTestGridFactory))]
public sealed class CrackerTestGridTest
{
    private const string Crate = "WFAnchorCrate";
    private const string Anchor = "WFGravityAnchor";
    private const string Gravgen = "WFTransportGravgen";

    /// <summary>Plan F.1 plus the FTL drive: fifteen entities on the tiny cracker.</summary>
    private static readonly Dictionary<string, int> CrackerContents = new()
    {
        ["ComputerShuttle"] = 1,
        ["WFCrackConsole"] = 1,
        ["DebugGyroscope"] = 1,
        ["MachineFTLDrive"] = 1,
        ["WFCentrifuge"] = 1,
        ["WFGravityProjector"] = 2,
        ["WFChunkBerthMarker"] = 1,
        ["AirlockShuttle"] = 1,
        ["WFAnchorCrate"] = 2,
        ["DebugThruster"] = 4,
    };

    /// <summary>
    /// Plan F.2, as F10 leaves it: the gravity generator is gone - the transport flies over a planet on landing
    /// thrusters, which is the only lift there is over one - so nine entities rather than eight.
    /// </summary>
    private static readonly Dictionary<string, int> TransportContents = new()
    {
        ["ComputerShuttle"] = 1,
        ["WFThrusterLanding"] = 2,
        ["DebugGyroscope"] = 1,
        ["AirlockShuttle"] = 1,
        ["WFAnchorCrate"] = 1,
        ["DebugThruster"] = 4,
    };

    /// <summary>Tiles in the tiny cracker hull, 15x15.</summary>
    private const int CrackerTiles = 225;

    /// <summary>Tiles in the micro transport hull, 7x9.</summary>
    private const int TransportTiles = 63;

    /// <summary>ShuttleSystem.TileDensityMultiplier; the tests read it back rather than trusting the constant.</summary>
    private const float TileDensity = 0.5f;

    /// <summary>WFTransportGravgen.maxHandledMass.</summary>
    private const float TransportCapacity = 40f;

    /// <summary>WFCentrifuge.maxHandledMass.</summary>
    private const float CentrifugeCapacity = 3000f;

    /// <summary>WFGravityAnchorComponent.VirtualMass and WFAnchorCrateComponent.VirtualMass.</summary>
    private const float AnchorVirtualMass = 6f;

    [Test]
    public async Task CrackerGridHasTheExpectedContents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(cracker);
            Assert.That(maps.GetAllTiles(cracker, grid).Count(), Is.EqualTo(CrackerTiles),
                "The tiny cracker should be a solid 15x15 of deck plating.");

            var contents = Contents(entMan, cracker);

            using (Assert.EnterMultipleScope())
            {
                foreach (var (proto, count) in CrackerContents)
                {
                    Assert.That(contents.GetValueOrDefault(proto), Is.EqualTo(count),
                        $"The cracker carries the wrong number of {proto}.");
                }

                Assert.That(contents.Values.Sum(), Is.EqualTo(CrackerContents.Values.Sum()),
                    $"The cracker carries entities the layout does not list: {string.Join(", ", contents.Keys)}");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The layout-typo guard: a tile index off the hull would leave an entity over empty space.</summary>
    [Test]
    public async Task EverythingSitsOnASolidTile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(cracker);
            var children = Children(entMan, cracker).ToList();

            Assert.That(children, Has.Count.EqualTo(CrackerContents.Values.Sum()),
                "Precondition: the hull carries its fifteen entities.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var uid in children)
                {
                    var xform = entMan.GetComponent<TransformComponent>(uid);
                    var index = maps.TileIndicesFor(cracker, grid, xform.Coordinates);

                    Assert.That(maps.TryGetTileRef(cracker, grid, index, out var tile) && !tile.Tile.IsEmpty, Is.True,
                        $"{entMan.ToPrettyString(uid)} at {index} is not standing on deck plating.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Anchored per role, not across the board: the crates carry Anchorable flags: None, which AnchorableSystem
    /// refuses outright, so a blanket assertion would be asserting something the prototype cannot do.
    /// </summary>
    [Test]
    public async Task OnlyTheMachinesAreAnchored()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var uid in Children(entMan, cracker))
                {
                    var proto = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                    var anchored = entMan.GetComponent<TransformComponent>(uid).Anchored;

                    if (proto == Crate)
                        Assert.That(anchored, Is.False, "An anchor crate is anchored; it is meant to be dragged.");
                    else
                        Assert.That(anchored, Is.True, $"{proto} should be wrenched down on the hull.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TransportGridHasTheExpectedContents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        var map = await pair.CreateTestMap();
        var transport = await BuildTransport(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(transport);
            Assert.That(maps.GetAllTiles(transport, grid).Count(), Is.EqualTo(TransportTiles),
                "The micro transport should be a solid 7x9 of deck plating.");

            var contents = Contents(entMan, transport);

            using (Assert.EnterMultipleScope())
            {
                foreach (var (proto, count) in TransportContents)
                {
                    Assert.That(contents.GetValueOrDefault(proto), Is.EqualTo(count),
                        $"The transport carries the wrong number of {proto}.");
                }

                Assert.That(contents.Values.Sum(), Is.EqualTo(TransportContents.Values.Sum()),
                    $"The transport carries entities the layout does not list: {string.Join(", ", contents.Keys)}");

                var crate = Children(entMan, transport)
                    .First(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == Crate);

                Assert.That(entMan.GetComponent<TransformComponent>(crate).Anchored, Is.False,
                    "The transport's crate is anchored; it is cargo.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Both hulls have to actually fly. A thruster only adds to ShuttleComponent.LinearThrust if the grid already
    /// carries that component when the thruster initialises (ThrusterSystem.EnableThruster returns early otherwise,
    /// and its IsOn guard means nothing re-registers it later), so this is the guard on the build order.
    /// </summary>
    [Test]
    public async Task BothHullsRegisterTheirThrust()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);
        var transport = await BuildTransport(pair, map.MapId, new Vector2(200f, 0f));

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var crackerShuttle = entMan.GetComponent<ShuttleComponent>(cracker);
            var transportShuttle = entMan.GetComponent<ShuttleComponent>(transport);

            using (Assert.EnterMultipleScope())
            {
                // Thruster facing picks the index: South 0, East 1, North 2, West 3 (ThrusterSystem.cs:342).
                for (var dir = 0; dir < 4; dir++)
                {
                    Assert.That(crackerShuttle.LinearThrust[dir], Is.GreaterThan(0f),
                        $"The cracker registered no thrust in direction {dir}.");
                    Assert.That(transportShuttle.LinearThrust[dir], Is.GreaterThan(0f),
                        $"The transport registered no thrust in direction {dir}.");
                }

                Assert.That(crackerShuttle.AngularThrust, Is.GreaterThan(0f),
                    "The cracker's gyroscope registered no angular thrust.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The pooled lift check weighs a hull as tiles x TileDensityMultiplier and nothing else.</summary>
    [Test]
    public async Task HullMassMatchesTileCount()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);
        var transport = await BuildTransport(pair, map.MapId, new Vector2(200f, 0f));

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                // Not exact: grid chunk fixtures are polygons with a physics skin, so a hull measures fractionally
                // UNDER tile count times the density multiplier - the 15x15 cracker reads 112.2002 against 112.5.
                // Hence a half-open band exactly one tile's 0.5 contribution wide instead of a tolerance: a hull
                // one tile too small falls below it and one tile too large rises above the exact figure.
                Assert.That(entMan.GetComponent<PhysicsComponent>(cracker).FixturesMass,
                    Is.GreaterThan(CrackerTiles * TileDensity - TileDensity)
                        .And.LessThanOrEqualTo(CrackerTiles * TileDensity),
                    "The cracker hull's mass no longer matches its tile count.");

                Assert.That(entMan.GetComponent<PhysicsComponent>(transport).FixturesMass,
                    Is.GreaterThan(TransportTiles * TileDensity - TileDensity)
                        .And.LessThanOrEqualTo(TransportTiles * TileDensity),
                    "The transport hull's mass no longer matches its tile count.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Hull 31.5 plus one crated anchor's 6 virtual mass is 37.5, inside the 40 the mini gravgen is rated for.</summary>
    [Test]
    public async Task TransportGravgenIsRatedForTheHullPlusOneAnchor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();

        var map = await pair.CreateTestMap();
        var transport = await BuildTransport(pair, map.MapId);
        await AddGravgen(pair, transport);
        await Energise(pair, transport);

        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.TryGetGravgenLoad(transport, out var mass, out var capacity), Is.True,
                "The transport reports no gravgen load at all.");

            var hull = entMan.GetComponent<PhysicsComponent>(transport).FixturesMass;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(capacity, Is.EqualTo(TransportCapacity).Within(0.01f),
                    "The transport gravgen is not rated at 40.");
                Assert.That(mass - hull, Is.EqualTo(AnchorVirtualMass).Within(0.01f),
                    "The load is not the hull plus exactly one crated anchor's virtual mass.");
                Assert.That(mass, Is.LessThanOrEqualTo(capacity), "The transport cannot lift its own single anchor.");
                Assert.That(capacity, Is.LessThan(50f), "The rating is not a downgrade from the stock mini gravgen.");
                Assert.That(capacity, Is.LessThan(TransportTiles), "The rating lets the transport lift twice its hull.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The test that proves D11 bites: without the two marked lines in CEZLevelsSystem.Gravity.cs the reported mass
    /// would sit at the bare hull's 31.5 however much cargo was aboard.
    /// </summary>
    [Test]
    public async Task SecondAnchorOverloadsTheTransport()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();

        var map = await pair.CreateTestMap();
        var transport = await BuildTransport(pair, map.MapId);
        await AddGravgen(pair, transport);
        await Energise(pair, transport);

        var before = 0f;

        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.TryGetGravgenLoad(transport, out before, out var capacity), Is.True,
                "The transport reports no gravgen load at all.");
            Assert.That(before, Is.LessThanOrEqualTo(capacity), "Precondition: one anchor still flies.");
        });

        await server.WaitPost(() => entMan.SpawnEntity(Crate, new EntityCoordinates(transport, new Vector2(1.5f, 7.5f))));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.TryGetGravgenLoad(transport, out var mass, out var capacity), Is.True,
                "The transport reports no gravgen load at all.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mass - before, Is.EqualTo(AnchorVirtualMass).Within(0.01f),
                    "The second crate did not add its virtual mass to the load.");
                Assert.That(mass, Is.GreaterThan(capacity), "Two anchors should put the transport over its rating.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Both exclusions in the capacity sweep at once: a planet ground layer is itself a grid, and a wrenched-down
    /// anchor is terrain. Either one leaking would put cargo mass on a whole planet network's pooled lift.
    /// </summary>
    [Test]
    public async Task DeployedAnchorsDoNotLoadAPlanetLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        var ground = stack[0];

        await LayTiles(pair, ground, new Vector2i(-4, -4), new Vector2i(28, 8));

        var anchors = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            anchors.Add(entMan.SpawnEntity(Anchor, new EntityCoordinates(ground, new Vector2(0.5f, 0.5f))));
            anchors.Add(entMan.SpawnEntity(Anchor, new EntityCoordinates(ground, new Vector2(24.5f, 0.5f))));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<WFGridAnchorLoadComponent>(ground), Is.False,
                "Loose anchors resting on a planet ground layer added cargo mass to the layer itself."));

        await server.WaitPost(() =>
        {
            foreach (var anchor in anchors)
            {
                transform.AnchorEntity(anchor);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var anchor in anchors)
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(anchor).Anchored, Is.True,
                        "Precondition: the anchor is wrenched down on the ground layer.");
                    Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(anchor).State,
                        Is.Not.EqualTo(WFAnchorState.Loose), "Precondition: the anchor deployed.");
                }

                Assert.That(entMan.HasComponent<WFGridAnchorLoadComponent>(ground), Is.False,
                    "Deployed anchors added cargo mass to the planet ground layer.");
            }
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>The player-facing half of D11: the rating stays at one while the count climbs past it.</summary>
    [Test]
    public async Task AnchorCapacityCountsWhatIsAboard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        var transport = await BuildTransport(pair, map.MapId);
        await AddGravgen(pair, transport);

        await server.WaitPost(() => entMan.SpawnEntity(Crate, new EntityCoordinates(transport, new Vector2(1.5f, 7.5f))));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var gravgen = Children(entMan, transport)
                .First(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == Gravgen);

            var capacity = entMan.GetComponent<WFAnchorCapacityComponent>(gravgen);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(capacity.Aboard, Is.EqualTo(2), "The capacity sweep did not count both crates.");
                Assert.That(capacity.Capacity, Is.EqualTo(1), "The transport is rated for more than one anchor.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The centrifuge's placeholder rating has to clear the hull and must never read as the infinite-lift zero.</summary>
    [Test]
    public async Task CentrifugeIsRatedAboveTheHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);
        await Energise(pair, cracker);

        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.TryGetGravgenLoad(cracker, out var mass, out var capacity), Is.True,
                "The cracker reports no gravgen load at all.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(capacity, Is.EqualTo(CentrifugeCapacity).Within(0.01f),
                    "The centrifuge is not rated at 3000.");
                Assert.That(capacity, Is.GreaterThan(0f),
                    "A rating of zero or less reads as infinite lift in HasPooledGravgenSupport.");
                Assert.That(capacity, Is.GreaterThan(CrackerTiles * TileDensity),
                    "The centrifuge cannot lift its own hull.");
                Assert.That(mass - entMan.GetComponent<PhysicsComponent>(cracker).FixturesMass,
                    Is.EqualTo(2f * AnchorVirtualMass).Within(0.01f),
                    "The load is not the hull plus exactly its two crated anchors' virtual mass.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Proves the factory's SetNeedsPower(false) route gets the centrifuge all the way to gravity on the grid.</summary>
    [Test]
    public async Task CentrifugeActivatesWithoutCabling()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);
        await Energise(pair, cracker);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent(cracker, out GravityComponent? gravity), Is.True,
                "The cracker hull never got a gravity component.");
            Assert.That(gravity!.Enabled, Is.True, "The centrifuge charged but never switched the grid's gravity on.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The centrifuge sweep's readout: it climbs from a cold start with the charge, and the at-full latch closes only
    /// at FullOn and then holds until the spin falls below FullOff (design D25). Stands in for the headless vv check,
    /// which no automated run can perform.
    /// </summary>
    [Test]
    public async Task CentrifugeSpinReadoutTracksChargeWithHysteresis()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);
        var centrifuge = EntityUid.Invalid;
        var cold = 1f;

        await server.WaitAssertion(() =>
        {
            foreach (var uid in Children(entMan, cracker))
            {
                if (entMan.HasComponent<WFCentrifugeComponent>(uid))
                    centrifuge = uid;
            }

            Assert.That(centrifuge, Is.Not.EqualTo(EntityUid.Invalid), "The cracker hull carries no centrifuge.");

            var comp = entMan.GetComponent<WFCentrifugeComponent>(centrifuge);
            cold = comp.Spin;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cold, Is.LessThan(0.1f), "charge: 0 did not leave the rotor cold.");
                Assert.That(comp.AtFull, Is.False, "A cold rotor already reads as at full.");
            }
        });

        await SetCharge(pair, centrifuge, 0.5f);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFCentrifugeComponent>(centrifuge);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Spin, Is.GreaterThan(cold), "The spin readout did not climb with the charge.");
                Assert.That(comp.Spin, Is.EqualTo(0.5f).Within(0.05f),
                    "The spin readout is not the charge as a fraction of MaxCharge.");
                Assert.That(comp.AtFull, Is.False, "Half spin reads as at full.");
            }
        });

        await SetCharge(pair, centrifuge, 0.96f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFCentrifugeComponent>(centrifuge).AtFull, Is.False,
                "The at-full latch closed below FullOn.");
        });

        await SetCharge(pair, centrifuge, 0.99f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFCentrifugeComponent>(centrifuge).AtFull, Is.True,
                "The at-full latch never closed at FullOn.");
        });

        await SetCharge(pair, centrifuge, 0.96f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFCentrifugeComponent>(centrifuge).AtFull, Is.True,
                "The latch let go between FullOff and FullOn, so there is no hysteresis.");
        });

        await SetCharge(pair, centrifuge, 0.94f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFCentrifugeComponent>(centrifuge).AtFull, Is.False,
                "The latch held below FullOff.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The berth hangs eight tiles beyond the north hull edge, clear of the 15x15 deck.</summary>
    [Test]
    public async Task BerthCentreSitsOffTheHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var crackers = server.System<WFCrackerSystem>();

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(cracker);
            Assert.That(comp.Berth, Is.Not.Null, "The cracker never resolved its berth marker.");

            Assert.That(crackers.TryGetBerthCentre((cracker, comp), out var centre), Is.True,
                "The berth centre could not be computed.");

            var grid = entMan.GetComponent<MapGridComponent>(cracker);
            var local = maps.WorldToLocal(cracker, grid, centre.Position);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(local.X, Is.EqualTo(7.5f).Within(0.01f), "The berth is not centred on the hull.");
                Assert.That(local.Y, Is.EqualTo(22.5f).Within(0.01f), "The berth is not eight tiles off the north edge.");

                var index = maps.LocalToTile(cracker, grid, new EntityCoordinates(cracker, local));
                Assert.That(maps.TryGetTileRef(cracker, grid, index, out var tile) && !tile.Tile.IsEmpty, Is.False,
                    "The berth centre sits on the hull instead of off it.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The berth rectangle is one derivation off the berth centre, so the two can never drift apart.</summary>
    [Test]
    public async Task BerthRectIsCentredOnTheBerthCentre()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();
        var maps = server.System<SharedMapSystem>();

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);

        // The factory sizes the berth after spawning its marker, so the pose the radar ghost reads is only final once
        // the cracker sweep has refreshed it.
        await server.WaitRunTicks(pair.SecondsToTicks(1.1f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(cracker);

            Assert.That(crackers.TryGetBerthCentre((cracker, comp), out var centre), Is.True,
                "Precondition: the berth centre resolves.");
            Assert.That(crackers.TryGetBerthRect((cracker, comp), out var rect), Is.True,
                "The berth rectangle could not be computed.");

            var berth = entMan.GetComponent<WFChunkBerthComponent>(entMan.GetEntity(comp.Berth!.Value));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(rect.Center.X, Is.EqualTo(centre.Position.X).Within(0.01f),
                    "The berth rectangle is not centred on the berth centre.");
                Assert.That(rect.Center.Y, Is.EqualTo(centre.Position.Y).Within(0.01f),
                    "The berth rectangle is not centred on the berth centre.");
                Assert.That(rect.Box.Width, Is.EqualTo((float) berth.Size.X).Within(0.01f),
                    "The berth rectangle is not the marker's width.");
                Assert.That(rect.Box.Height, Is.EqualTo((float) berth.Size.Y).Within(0.01f),
                    "The berth rectangle is not the marker's height.");

                // The radar ghost draws the rectangle straight off this field, so it has to be the berth CENTRE, not
                // the marker: nothing on the client can recover the marker-to-centre distance, which is not networked.
                var localCentre = maps.WorldToLocal(cracker, entMan.GetComponent<MapGridComponent>(cracker),
                    centre.Position);

                Assert.That(comp.BerthLocalPos.X, Is.EqualTo(localCentre.X).Within(0.01f),
                    "The grid component holds the marker pose, not the berth centre the radar ghost draws around.");
                Assert.That(comp.BerthLocalPos.Y, Is.EqualTo(localCentre.Y).Within(0.01f),
                    "The grid component holds the marker pose, not the berth centre the radar ghost draws around.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Ownership is what keeps two crackers from sharing a pair, so an unowned crate must stay unowned.</summary>
    [Test]
    public async Task CratesAreBoundToTheCracker()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        var cracker = await BuildCracker(pair, map.MapId);

        var loose = EntityUid.Invalid;
        await server.WaitPost(() => loose = entMan.SpawnEntity(Crate, map.GridCoords));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var owner = entMan.GetNetEntity(cracker);
            var crates = Children(entMan, cracker)
                .Where(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == Crate)
                .ToList();

            Assert.That(crates, Has.Count.EqualTo(2), "Precondition: two crates shipped with the cracker.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var crate in crates)
                {
                    Assert.That(entMan.GetComponent<WFAnchorCrateComponent>(crate).Cracker, Is.EqualTo(owner),
                        "A crate aboard the cracker was never stamped with its hull.");
                }

                Assert.That(entMan.GetComponent<WFAnchorCrateComponent>(loose).Cracker, Is.Null,
                    "A crate on an unrelated grid was stamped with the cracker.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The admin and ERT path raises no ShipyardShuttlePurchaseEvent, so it calls BindAboard directly; this is that
    /// call, on a hand-built hull the shipyard never touched.
    /// </summary>
    [Test]
    public async Task AdminSpawnBindsAnchorsToo()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var ownership = server.System<WFCrackerOwnershipSystem>();

        var map = await pair.CreateTestMap();
        var hull = EntityUid.Invalid;
        var crates = new List<EntityUid>();
        var bound = 0;

        await server.WaitPost(() =>
        {
            var grid = mapMan.CreateGridEntity(map.MapId);
            var floor = new Tile(tileDefs[FloorTile].TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

            for (var x = 0; x < 4; x++)
            for (var y = 0; y < 4; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            maps.SetTiles(grid.Owner, grid.Comp, tiles);
            hull = grid.Owner;

            entMan.EnsureComponent<WFPlanetCrackerComponent>(hull);
            crates.Add(entMan.SpawnEntity(Crate, new EntityCoordinates(hull, new Vector2(1.5f, 1.5f))));
            crates.Add(entMan.SpawnEntity(Crate, new EntityCoordinates(hull, new Vector2(2.5f, 2.5f))));
        });

        await server.WaitRunTicks(1);

        await server.WaitPost(() => bound = ownership.BindAboard(hull));

        await server.WaitAssertion(() =>
        {
            var owner = entMan.GetNetEntity(hull);

            using (Assert.EnterMultipleScope())
            {
                // Crates bind the moment they land on the deck (EntParentChangedMessage), so the explicit sweep finds
                // nothing left to stamp; what matters is that both crates end up owned, asserted below.
                Assert.That(bound, Is.EqualTo(0), "BindAboard found crates the deck landing had not already bound.");

                foreach (var crate in crates)
                {
                    Assert.That(entMan.GetComponent<WFAnchorCrateComponent>(crate).Cracker, Is.EqualTo(owner),
                        "A crate on the admin-spawned hull was left unowned.");
                }
            }
        });

        await server.WaitPost(() => entMan.DeleteEntity(hull));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The combined spawn the admin command and the shipyard purchase both end in: the transport arrives docked to the
    /// cracker's airlock with its crate already stamped, so nobody has to spawn and fly it over.
    /// Nothing in the test pair can fire ShipyardShuttlePurchaseEvent cheaply - it comes out of the shipyard console
    /// against a mapped vessel, and no cracker vessel is mapped yet - so this exercises the routine both paths share,
    /// WFCrackerOwnershipSystem.DockTransport.
    /// </summary>
    [Test]
    public async Task CombinedSpawnArrivesDocked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var factory = server.System<WFTestGridFactory>();

        var map = await pair.CreateTestMap();
        var cracker = EntityUid.Invalid;
        var transport = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var built = factory.BuildCrackerWithTransport(map.MapId, Vector2.Zero);
            cracker = built.Cracker;
            transport = built.Transport;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var crackerDocks = Docks(entMan, cracker);
            var transportDocks = Docks(entMan, transport);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(crackerDocks, Has.Count.EqualTo(1), "The tiny cracker should carry one docking airlock.");
                Assert.That(transportDocks, Has.Count.EqualTo(1), "The micro transport should carry one docking airlock.");

                var crackerDock = entMan.GetComponent<DockingComponent>(crackerDocks[0]);

                Assert.That(crackerDock.DockedWith, Is.EqualTo(transportDocks[0]),
                    "The transport should have arrived docked to the cracker's airlock.");
                Assert.That(entMan.GetComponent<DockingComponent>(transportDocks[0]).DockedWith, Is.EqualTo(crackerDocks[0]),
                    "The transport's airlock should point back at the cracker's.");

                var owner = entMan.GetNetEntity(cracker);
                var crates = Children(entMan, transport)
                    .Where(entMan.HasComponent<WFAnchorCrateComponent>)
                    .ToList();

                Assert.That(crates, Has.Count.EqualTo(1), "The transport should still carry its one anchor crate.");
                Assert.That(entMan.GetComponent<WFAnchorCrateComponent>(crates[0]).Cracker, Is.EqualTo(owner),
                    "The docked transport's crate should belong to the cracker it came with.");
            }
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(transport);
            entMan.DeleteEntity(cracker);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every wfcracker subcommand the crack control added, run through the console host against a spawned hull.
    /// The arity is per subcommand rather than a blanket length check, so this pins both halves: the one-argument forms
    /// dispatch and move the hull, and a one-argument form handed a second argument is refused instead. Stands in for
    /// the by-hand console pass, which no automated run can perform.
    /// </summary>
    [Test]
    public async Task WfCrackerCommandSubcommandsDispatch()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);

        var stack = await BuildStandalone(pair);
        var ground = stack[0];
        var orbit = stack[^1];
        var orbitMap = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFOrbitLayerComponent>(orbit), Is.True,
                "Precondition: the top layer of the stack is the orbit layer.");

            orbitMap = entMan.GetComponent<MapComponent>(orbit).MapId;
        });

        await LayTiles(pair, ground, new Vector2i(-4, -4), new Vector2i(28, 8));

        // The hull has to be on the orbit layer for the survey edge and for the fall to have somewhere to go.
        var cracker = await BuildCracker(pair, orbitMap);
        var anchors = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            anchors.Add(entMan.SpawnEntity(Anchor, new EntityCoordinates(ground, new Vector2(0.5f, 0.5f))));
            anchors.Add(entMan.SpawnEntity(Anchor, new EntityCoordinates(ground, new Vector2(24.5f, 0.5f))));

            // Hand-spawned anchors belong to nobody, and an unowned pair is invisible to the hull's state machine.
            foreach (var anchor in anchors)
            {
                entMan.GetComponent<WFGravityAnchorComponent>(anchor).Cracker = entMan.GetNetEntity(cracker);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));
        await server.WaitPost(() =>
        {
            foreach (var anchor in anchors)
            {
                transform.AnchorEntity(anchor);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(cracker).State,
                Is.EqualTo(WFCrackState.AnchorsPlaced),
                "Precondition: an owned pair on the surface puts the hull at anchors-placed."));

        // Two arguments: both drills finish at once and the pair becomes targetable.
        await server.WaitPost(() => server.ConsoleHost.ExecuteCommand(null, "wfcracker complete drill"));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var anchor in anchors)
                {
                    Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(anchor).State,
                        Is.EqualTo(WFAnchorState.Locked), "complete drill left an anchor unlocked.");
                }

                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(cracker).State,
                    Is.EqualTo(WFCrackState.AnchorsLocked), "complete drill did not move the hull's stage.");
            }
        });

        // One argument, which the old blanket two-argument gate would have refused outright.
        await server.WaitPost(() => server.ConsoleHost.ExecuteCommand(null, "wfcracker disconnect"));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var anchor in anchors)
                {
                    Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(anchor).State,
                        Is.EqualTo(WFAnchorState.Off),
                        "The one-argument disconnect did not switch both anchors off.");
                }
            }
        });

        // Two arguments again: the stage is forced straight to a cut, which is the only escape hatch design D23 leaves.
        await server.WaitPost(() => server.ConsoleHost.ExecuteCommand(null, "wfcracker state Cracking"));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(cracker).State, Is.EqualTo(WFCrackState.Cracking),
                "state <stage> did not move the hull's stage."));

        // The refusal paths (`state NotAStage`, `fall now`) are deliberately NOT driven here: every one of them ends in
        // shell.WriteError, and TestingServerConsoleHost.WriteError is an Assert.Fail, so a pair cannot watch a console
        // command refuse anything. What the per-subcommand arity buys is covered positively instead - `disconnect` above
        // and `fall` below are one-argument forms the old blanket two-argument gate would have rejected outright.

        await server.WaitPost(() => server.ConsoleHost.ExecuteCommand(null, "wfcracker fall"));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(cracker).State,
                    Is.EqualTo(WFCrackState.Falling), "The one-argument fall did not push the hull.");
                Assert.That(entMan.HasComponent<CEZGridFallerComponent>(cracker), Is.True,
                    "The fall never made the hull a faller.");
            }
        });

        // The hull is mid-transit, so it goes before the stack it was falling into.
        await server.WaitPost(() => entMan.DeleteEntity(cracker));
        await server.WaitRunTicks(1);

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Bolts the old transport gravity generator back onto a hull. F10 took it out of the factory layout - the
    /// transport flies on landing thrusters now - but it is still the fixture that carries WFAnchorCapacity and the
    /// rating the D11 tests are about, so those tests bring their own.
    /// </summary>
    private static async Task<EntityUid> AddGravgen(TestPair pair, EntityUid transport)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var receiver = server.System<SharedPowerReceiverSystem>();
        var uid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            uid = entMan.SpawnEntity(Gravgen, new EntityCoordinates(transport, new Vector2(3.5f, 4.5f)));

            // No cabling on a code-built hull, exactly as WFTestGridFactory.SpawnOnHull does it.
            receiver.SetNeedsPower(uid, false);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        return uid;
    }

    /// <summary>The docking airlocks riding on one grid.</summary>
    private static List<EntityUid> Docks(IEntityManager entMan, EntityUid grid)
    {
        return Children(entMan, grid).Where(entMan.HasComponent<DockingComponent>).ToList();
    }
}
