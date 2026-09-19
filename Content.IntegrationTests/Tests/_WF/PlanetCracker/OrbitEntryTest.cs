#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// You do not need an FTL drive to go into orbit. The shuttle console carries its own "enter planet orbit" action,
/// gated on being parked within the body's orbit range on the body's own sector map, and the orbit layer is no longer
/// an FTL destination at all. The hop itself reuses the ordinary FTL transit, so undocking, the hyperspace map and the
/// arrival sweep all still run - it simply never asks GetFTLRange, which gives a driveless hull a range of zero.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOrbitEntrySystem))]
public sealed class OrbitEntryTest
{
    /// <summary>The shuttle console prototype; the orbit action is a message on its own BUI key.</summary>
    private const string ConsoleProto = "ComputerShuttle";

    /// <summary>WFSurfaceAsclepiu's orbitRange, which is the band the console action is gated on.</summary>
    private const float OrbitRange = 2000f;

    /// <summary>
    /// A hop is a five second warm-up plus a five second transit. Arrival is polled rather than slept out, so a working
    /// hop only costs what it costs; this is just the give-up point.
    /// </summary>
    private const float HopTimeout = 20f;

    /// <summary>A sector body with its stack, plus a driveless hull carrying one shuttle console.</summary>
    private sealed class Site
    {
        public List<EntityUid> Layers = new();
        public EntityUid Body;
        public EntityUid SectorMap;
        public EntityUid Hull;
        public EntityUid Console;

        public EntityUid Air => Layers[1];
        public EntityUid Orbit => Layers[^1];
    }

    /// <summary>Out of range is refused, and the console offers nothing to press.</summary>
    [Test]
    public async Task RefusedOutOfRange()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(OrbitRange + 500f, 0f));
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.False,
                    "A hull well outside the orbit range entered orbit anyway.");
                Assert.That(reason,
                    Is.EqualTo(loc.GetString("wf-orbit-out-of-range", ("planet", Name(entMan, site.Body)))),
                    "Out of range was refused for some other reason.");
                Assert.That(entMan.GetComponent<TransformComponent>(site.Hull).MapUid, Is.EqualTo(site.SectorMap),
                    "The refused hull moved anyway.");
                Assert.That(OrbitTarget(entMan, site.Console)?.Planet, Is.Null,
                    "The console still offers a planet to a hull outside the range band.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>A hull on another map entirely is refused, even parked at the body's own coordinates.</summary>
    [Test]
    public async Task RefusedFromAnotherMap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        var elsewhere = await pair.CreateTestMap();

        await MoveTo(pair, site.Hull, elsewhere.MapUid, Vector2.Zero);
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.False,
                    "A hull on a different map entered orbit.");
                Assert.That(reason,
                    Is.EqualTo(loc.GetString("wf-orbit-not-in-sector", ("planet", Name(entMan, site.Body)))),
                    "Being off the sector map was refused for some other reason.");
                Assert.That(OrbitTarget(entMan, site.Console)?.Planet, Is.Null,
                    "The console offers a planet that is not even in this system.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>The whole point: in range, with no FTL drive anywhere aboard, the hull reaches the orbit layer.</summary>
    [Test]
    public async Task EntersOrbitWithoutAnFTLDrive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbits = server.System<WFOrbitEntrySystem>();
        var shuttles = server.System<ShuttleSystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(shuttles.TryGetFTLDrive(site.Hull, out _, out _), Is.False,
                    "Precondition: the hull carries no FTL drive.");
                Assert.That(shuttles.GetFTLRange(site.Hull), Is.EqualTo(0f),
                    "Precondition: a driveless hull has no FTL range at all, which is why the orbit button exists.");
            }

            var target = OrbitTarget(entMan, site.Console);
            Assert.That(target, Is.Not.Null, "The console offers nothing to a hull parked in range.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(target!.Planet, Is.EqualTo(entMan.GetNetEntity(site.Body)), "The console names the wrong body.");
                Assert.That(target.PlanetName, Is.EqualTo(Name(entMan, site.Body)), "The button could not label itself.");
                Assert.That(target.InOrbit, Is.False, "A hull on the sector map is not in orbit.");
            }
        });

        await server.WaitAssertion(() =>
            Assert.That(orbits.TryEnterOrbit(site.Console, site.Body, out var reason), Is.True,
                $"A hull 400 from the body was refused orbit: {reason}"));

        var arrived = await WaitForMap(pair, site.Hull, site.Orbit);

        await server.WaitAssertion(() =>
            Assert.That(arrived, Is.True,
                $"The hull never reached the orbit layer; it ended on {entMan.ToPrettyString(entMan.GetComponent<TransformComponent>(site.Hull).MapUid)}."));

        // Four seconds is the window PlanetNetworkTest.GridOnOrbitLayerDoesNotFall uses; an arrival that immediately
        // drops back through the stack would be an insertion in name only.
        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(site.Hull).MapUid, Is.EqualTo(site.Orbit),
                "The hull arrived in orbit and then left the layer again."));

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>Leave orbit is the same hop in reverse: back onto the sector map the body sits on.</summary>
    [Test]
    public async Task LeavesOrbitForTheSectorMap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.Orbit, new Vector2(60f, 0f));
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            var target = OrbitTarget(entMan, site.Console);
            Assert.That(target, Is.Not.Null, "The console offers nothing to a hull parked in orbit.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(target!.InOrbit, Is.True, "The console does not know the hull is in orbit.");
                Assert.That(target.PlanetName, Is.EqualTo(Name(entMan, site.Body)), "The button names the wrong world.");
            }
        });

        await server.WaitAssertion(() =>
            Assert.That(orbits.TryLeaveOrbit(site.Console, out var reason), Is.True,
                $"A hull in orbit was refused the climb out: {reason}"));

        var arrived = await WaitForMap(pair, site.Hull, site.SectorMap);

        await server.WaitAssertion(() =>
            Assert.That(arrived, Is.True,
                $"The hull never returned to the sector map; it ended on {entMan.ToPrettyString(entMan.GetComponent<TransformComponent>(site.Hull).MapUid)}."));

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>Leaving orbit is refused outright from anywhere that is not an orbit layer.</summary>
    [Test]
    public async Task LeaveOrbitRefusedOffTheOrbitLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var orbits = server.System<WFOrbitEntrySystem>();

        var site = await BuildSite(pair);
        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));
        await Sweep(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(orbits.TryLeaveOrbit(site.Console, out var reason), Is.False,
                    "A hull on the sector map left an orbit it was never in.");
                Assert.That(reason, Is.EqualTo(loc.GetString("wf-orbit-not-in-orbit")),
                    "Leaving orbit off an orbit layer was refused for some other reason.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The orbit layer is no longer offered as an FTL destination, and the outbound gate is unchanged: a hull on an air
    /// layer still cannot jump off the planet, while a hull in orbit still can.
    /// </summary>
    [Test]
    public async Task OrbitIsNotADestinationAndAirLayersStillCannotFTL()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var shuttles = server.System<ShuttleSystem>();

        var site = await BuildSite(pair);

        var orbitMapId = entMan.GetComponent<MapComponent>(site.Orbit).MapId;
        var sectorMapId = entMan.GetComponent<MapComponent>(site.SectorMap).MapId;

        // The sector map is an ordinary open destination, exactly as a station grid's map is at round start.
        await server.WaitPost(() => shuttles.TryAddFTLDestination(sectorMapId, true, false, false, out _));

        await MoveTo(pair, site.Hull, site.SectorMap, new Vector2(400f, 0f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<FTLDestinationComponent>(site.Orbit), Is.False,
                    "The orbit layer is still registered as an FTL destination.");
                Assert.That(shuttles.CanFTLTo(site.Hull, orbitMapId, site.Console), Is.False,
                    "The orbit layer is still offered as an FTL destination.");
            }
        });

        await MoveTo(pair, site.Hull, site.Air, Vector2.Zero);

        await server.WaitAssertion(() =>
            Assert.That(shuttles.CanFTLTo(site.Hull, sectorMapId, site.Console), Is.False,
                "A hull on an air layer must not be able to FTL off the planet; it climbs to orbit first."));

        await MoveTo(pair, site.Hull, site.Orbit, Vector2.Zero);

        await server.WaitAssertion(() =>
            Assert.That(shuttles.CanFTLTo(site.Hull, sectorMapId, site.Console), Is.True,
                "FTL out of orbit to elsewhere in the sector is the one jump off a planet that still works."));

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>An owned Asclepiu stack plus a driveless 5x5 hull carrying one shuttle console.</summary>
    private static async Task<Site> BuildSite(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapMan = server.ResolveDependency<IMapManager>();
        var maps = server.System<SharedMapSystem>();
        var receiver = server.System<SharedPowerReceiverSystem>();
        var shuttles = server.System<ShuttleSystem>();

        await EnableFeature(pair);

        var stack = await BuildOwnedStack(pair);
        var site = new Site { Layers = stack.Layers, Body = stack.Body, SectorMap = stack.BodyMap };

        await server.WaitPost(() =>
        {
            var grid = mapMan.CreateGridEntity(entMan.GetComponent<MapComponent>(site.SectorMap).MapId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }

            maps.SetTiles(grid.Owner, grid.Comp, tiles);
            entMan.EnsureComponent<ShuttleComponent>(grid.Owner);
            shuttles.Enable(grid.Owner, force: true);
            site.Hull = grid.Owner;

            // No cabling on a code-built hull, the same trick WFTestGridFactory uses. Deliberately no MachineFTLDrive:
            // the whole claim under test is that orbit needs none.
            site.Console = entMan.SpawnEntity(ConsoleProto, new EntityCoordinates(grid.Owner, new Vector2(2.5f, 2.5f)));
            receiver.SetNeedsPower(site.Console, false);
        });

        await MapInitHull(pair, site.Hull);
        await server.WaitRunTicks(2);
        return site;
    }

    /// <summary>Parks the hull on a map at a world offset, the way an arrival would leave it.</summary>
    private static async Task MoveTo(TestPair pair, EntityUid hull, EntityUid map, Vector2 position)
    {
        var server = pair.Server;
        var transform = server.System<SharedTransformSystem>();

        await server.WaitPost(() => transform.SetCoordinates(hull, new EntityCoordinates(map, position)));
        await server.WaitRunTicks(1);
    }

    /// <summary>Runs past the orbit system's one-second readout sweep, which is what fills the console component.</summary>
    private static Task Sweep(TestPair pair)
    {
        return pair.Server.WaitRunTicks(pair.SecondsToTicks(1.2f));
    }

    /// <summary>The console's current offer, or null when it has none.</summary>
    private static WFConsoleOrbitTargetComponent? OrbitTarget(IEntityManager entMan, EntityUid console)
    {
        return entMan.TryGetComponent(console, out WFConsoleOrbitTargetComponent? comp) ? comp : null;
    }

    /// <summary>The body's display name, which is what the button label substitutes.</summary>
    private static string Name(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityName;
    }

    /// <summary>
    /// Ticks in short bursts until the hull lands on the expected map, so a working hop costs only the ten seconds it
    /// actually takes rather than a fixed sleep. Bursts are short so an arrival is seen even if something later moves
    /// the hull off the layer again.
    /// </summary>
    private static async Task<bool> WaitForMap(TestPair pair, EntityUid hull, EntityUid map)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var burst = pair.SecondsToTicks(0.25f);
        var bursts = (int)(HopTimeout / 0.25f);

        for (var i = 0; i < bursts; i++)
        {
            await server.WaitRunTicks(burst);

            var arrived = false;
            await server.WaitPost(() => arrived = entMan.GetComponent<TransformComponent>(hull).MapUid == map);

            if (arrived)
                return true;
        }

        return false;
    }

    /// <summary>Tears the stack down through its own network entity and lets any transit map settle.</summary>
    private static async Task Teardown(TestPair pair, Site site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var networks = server.System<WFPlanetNetworkSystem>();

        await server.WaitPost(() =>
        {
            if (entMan.TryGetComponent(site.Body, out WFSectorPlanetComponent? sector)
                && sector.Network is { } network
                && entMan.TryGetEntity(network, out var networkUid))
            {
                networks.DeleteNetwork(networkUid.Value);
            }
        });

        await server.WaitRunTicks(5);
    }
}
