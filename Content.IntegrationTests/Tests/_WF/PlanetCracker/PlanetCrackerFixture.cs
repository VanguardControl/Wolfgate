#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.PlanetCracker.Anchors;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Server._WF.PlanetCracker.Testing;
using Content.Server.Parallax;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Flight;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Cracker.BUI;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Movement.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Markers;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// The scaffolding every planet cracker fixture shares, lifted out of the three that had grown their own verbatim
/// copies. Every fixture in this namespace pulls it in with <c>using static</c>, so the call sites read the same as
/// the private helpers they replaced.
/// PlanetNetworkTest keeps its own BuildStandalone and Teardown: they wrap the layers in a Stack record and tear down
/// through the network uid rather than the ground layer, so they are a different shape rather than a fourth copy.
/// </summary>
public static class PlanetCrackerFixture
{
    /// <summary>The crackable world every fixture builds its stack from.</summary>
    public const string SurfaceProto = "WFSurfaceAsclepiu";

    /// <summary>The deployable gravity anchor.</summary>
    public const string AnchorProto = "WFGravityAnchor";

    /// <summary>The sector body every owned stack hangs off; the only thing the cracked flag can live on.</summary>
    public const string PlanetBodyProto = "PlanetEntity";

    /// <summary>Deck plating, used for both hull decks and hand-laid ground.</summary>
    public const string FloorTile = "FloorSteel";

    /// <summary>What <see cref="AttachViewer"/> attaches the session to; a mob, never a ghost.</summary>
    public const string ViewerProto = "MobHuman";

    /// <summary>The crack miner as it ships, which map-inits with a free full <see cref="CellProto"/> in its bay.</summary>
    public const string MinerProto = "WFCrackMiner";

    /// <summary>The cell-less crack miner; every cell-behaviour test uses this one and seats its own cell.</summary>
    public const string MinerEmptyProto = "WFCrackMinerEmpty";

    /// <summary>The cell <see cref="SeatCell"/> seats by default, and the one the shipped miner starts with.</summary>
    public const string CellProto = "PowerCellHigh";

    /// <summary>The test vein CrackMinerTest declares; a WFDeepVein whose whitelist admits the fixture's deck plating.</summary>
    public const string VeinProto = "WFCrackMinerTestVein";

    /// <summary>The miner's cell slot id, as mining.yml declares it.</summary>
    public const string CellSlot = "cell_slot";

    /// <summary>Turns the feature on for this pair; TestPair reverts the change when the pair is returned.</summary>
    public static async Task EnableFeature(TestPair pair)
    {
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCrackerCVars.PlanetNetworks, true));
    }

    /// <summary>Builds an unowned Asclepiu stack at the origin and returns its layers, ground first.</summary>
    public static async Task<List<EntityUid>> BuildStandalone(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var layers = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var surface = proto.Index<WFPlanetSurfacePrototype>(SurfaceProto);
            var built = networks.BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);

            Assert.That(built, Is.Not.Null, "The planet network failed to build.");
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(built!.Value).Layers);
        });

        await server.WaitRunTicks(1);
        return layers;
    }

    /// <summary>
    /// Builds an Asclepiu stack the way the round does: a sector body on its own map, which owns the network.
    /// BuildStandalone passes no body, so both of the extraction's planet resolution paths come back empty and the
    /// cracked flag silently goes nowhere; anything that cares about the flag has to come through here.
    /// </summary>
    public static async Task<(List<EntityUid> Layers, EntityUid Body, EntityUid BodyMap)> BuildOwnedStack(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var networks = server.System<WFPlanetNetworkSystem>();
        var map = await pair.CreateTestMap();
        var layers = new List<EntityUid>();
        var body = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            body = entMan.SpawnEntity(PlanetBodyProto, new MapCoordinates(Vector2.Zero, map.MapId));

            // Through the one production writer rather than a hand-written EnsureComponent/Surface pair, so
            // WFPlanetRegistrySystem.ApplySurface really is the only thing in the tree that writes Surface and
            // Sanctioned and a fixture body's Sanctioned mirrors its surface prototype (plan D-J).
            ApplySurfaceTo(pair, body, SurfaceProto);

            var sector = new Entity<WFSectorPlanetComponent>(body, entMan.GetComponent<WFSectorPlanetComponent>(body));

            Assert.That(networks.TryBuildNetwork(sector, out var network), Is.True,
                "The sector body's planet network failed to build.");

            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);
        });

        await server.WaitRunTicks(1);
        return (layers, body, map.MapUid);
    }

    /// <summary>
    /// Attaches this pair's one session to a fresh entity on a map, which is the only thing that makes a biome
    /// generate anything at all.
    /// BiomeSystem.Update early-exits while _handledEntities is empty (Content.Server/Parallax/BiomeSystem.cs:198-202)
    /// and that set is filled only by ProcessPlayerChunkRequests, from attached players and from view subscriptions
    /// (BiomeSystem.PlayerTracker.cs:25-61). From there the path is AddChunksInRange (:70, the 16-tile _loadArea at
    /// BiomeSystem.cs:49/:65) plus AddMarkerChunksInRange (:80) -> LoadChunks (BiomeSystem.cs:248) ->
    /// BuildMarkerChunks (BiomeSystem.MarkerProcessor.cs:24) -> LoadChunkMarkers (:259), which is the only engine path
    /// that ever spawns a marker entity, and it only runs for chunks inside that load area. BiomeSystem.Preload
    /// (BiomeSystem.PlanetSetup.cs:152) registers chunk indices and spawns nothing, so it is not a substitute.
    /// A ghost is deliberately not used: CanLoad excludes GhostComponent holders that lack the AllowBiomeLoading tag
    /// (BiomeSystem.PlayerTracker.cs:65-68), so an observer would generate nothing.
    /// </summary>
    public static async Task<EntityUid> AttachViewer(TestPair pair, EntityUid map, Vector2 pos)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var viewer = EntityUid.Invalid;

        Assert.That(pair.Player, Is.Not.Null,
            "AttachViewer needs a session, so the pair has to be built with PoolSettings { Connected = true }.");

        await server.WaitPost(() =>
        {
            viewer = entMan.SpawnEntity(ViewerProto, new EntityCoordinates(map, pos));
            server.PlayerMan.SetAttachedEntity(pair.Player!, viewer);
        });

        await pair.RunTicksSync(2);
        return viewer;
    }

    /// <summary>
    /// Forces one marker layer to generate under whatever viewer is already attached, and actually forces it.
    /// Both writes are required and the order matters: BuildMarkerChunks returns for a chunk that is already in
    /// LoadedMarkers BEFORE it ever reads ForcedMarkerLayers (BiomeSystem.MarkerProcessor.cs:38-41), and
    /// ForcedMarkerLayers is cleared at the end of every pass (:118), so it is a one-shot that has to be set
    /// immediately before the ticks. The only in-repo precedent is BiomeSystem.Commands.cs:158-163, which writes the
    /// same pair. Pass clearLoaded false to leave LoadedMarkers alone, which is what exercises the double-spawn guard.
    /// Note that forcing also bulldozes any anchored entity already sitting on a candidate tile
    /// (BiomeSystem.MarkerProcessor.cs:58-70): acceptable on a throwaway test stack, never in production.
    /// </summary>
    public static async Task ForceMarkers(TestPair pair, EntityUid ground, string layer, int ticks = 40, bool clearLoaded = true)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var biome = entMan.GetComponent<BiomeComponent>(ground);

            // BiomeComponent is [Access(typeof(SharedBiomeSystem))] and nothing public forces a layer, so the three
            // marker sets are written by reflection the same way Energise writes PowerChargeComponent's ramp.
            MarkerLayers(biome).Add(layer);

            if (clearLoaded)
                LoadedMarkers(biome).Remove(layer);

            ForcedMarkerLayers(biome).Add(layer);
        });

        await pair.RunTicksSync(ticks);
    }

    /// <summary>
    /// Stamps a surface onto a test body through the one production writer, so a hand-built body carries Sanctioned
    /// as well as Surface. Must be called from inside a server thread callback.
    /// </summary>
    public static void ApplySurfaceTo(TestPair pair, EntityUid body, string surfaceId)
    {
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        pair.Server.System<WFPlanetRegistrySystem>()
            .ApplySurface(body, proto.Index<WFPlanetSurfacePrototype>(surfaceId));
    }

    /// <summary>
    /// Materialises a rectangle of ground so the footprint checks have solid tiles to find. SetTiles is deliberate:
    /// unlike BiomeSystem.ReserveTiles it leaves BiomeComponent.ModifiedTiles alone, so a reservation test can still
    /// tell what the anchor itself pinned.
    /// </summary>
    public static async Task LayTiles(TestPair pair, EntityUid ground, Vector2i from, Vector2i to)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var floor = new Tile(tileDefs[FloorTile].TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

            for (var x = from.X; x <= to.X; x++)
            for (var y = from.Y; y <= to.Y; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            maps.SetTiles(ground, grid, tiles);
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>Tears a stack down through its own network entity.</summary>
    public static async Task Teardown(TestPair pair, List<EntityUid> layers)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var networks = server.System<WFPlanetNetworkSystem>();

        await server.WaitPost(() =>
        {
            if (entMan.TryGetComponent(layers[0], out CEZMapComponent? zMap) && zMap.NetworkUid is { } network)
                networks.DeleteNetwork(network);
        });

        await server.WaitRunTicks(5);
    }

    /// <summary>
    /// Tears a whole site down: the stack through its network, then the sector body's own map.
    /// The older Teardown(pair, site.Layers) call sites keep working untouched; they simply leave the sector map to
    /// the pool, exactly as PlanetNetworkTest's sector fixture already does.
    /// </summary>
    public static async Task Teardown(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await Teardown(pair, site.Layers);

        if (site.PlanetMap == EntityUid.Invalid)
            return;

        await server.WaitPost(() =>
        {
            if (entMan.EntityExists(site.PlanetMap))
                entMan.DeleteEntity(site.PlanetMap);
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>Every entity parented straight to a grid; machine parts live inside their machine, not here.</summary>
    public static IEnumerable<EntityUid> Children(IEntityManager entMan, EntityUid grid)
    {
        var found = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<TransformComponent>();

        while (query.MoveNext(out var uid, out var xform))
        {
            if (xform.ParentUid == grid)
                found.Add(uid);
        }

        return found;
    }

    /// <summary>Prototype id to count for everything sitting on a grid.</summary>
    public static Dictionary<string, int> Contents(IEntityManager entMan, EntityUid grid)
    {
        var counts = new Dictionary<string, int>();

        foreach (var uid in Children(entMan, grid))
        {
            if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID is not { } id)
                continue;

            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        return counts;
    }

    /// <summary>Builds the tiny cracker through the factory and lets its fixtures settle.</summary>
    public static async Task<EntityUid> BuildCracker(TestPair pair, MapId map, Vector2? offset = null)
    {
        var server = pair.Server;
        var factory = server.System<WFTestGridFactory>();
        var grid = EntityUid.Invalid;

        await server.WaitPost(() => grid = factory.BuildCracker(map, offset ?? Vector2.Zero));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        return grid;
    }

    /// <summary>Builds the micro transport through the factory and lets its fixtures settle.</summary>
    public static async Task<EntityUid> BuildTransport(TestPair pair, MapId map, Vector2? offset = null)
    {
        var server = pair.Server;
        var factory = server.System<WFTestGridFactory>();
        var grid = EntityUid.Invalid;

        await server.WaitPost(() => grid = factory.BuildTransport(map, offset ?? new Vector2(100f, 0f)));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        return grid;
    }

    /// <summary>
    /// Builds a bare square grid with nothing whatsoever on it: no console, no thrusters, no shuttle component. This
    /// is debris - a fragment split off in combat, or a wreck - which is what orbit decay is mostly about.
    /// </summary>
    public static async Task<EntityUid> BuildDebris(TestPair pair, MapId map, int size = 3, Vector2? offset = null)
    {
        var server = pair.Server;
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var maps = server.System<SharedMapSystem>();
        var transform = server.System<SharedTransformSystem>();
        var grid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var built = mapMan.CreateGridEntity(map);
            transform.SetLocalPosition(built.Owner, offset ?? Vector2.Zero);

            var floor = new Tile(tileDefs[FloorTile].TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>(size * size);

            for (var x = 0; x < size; x++)
            for (var y = 0; y < size; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            maps.SetTiles(built.Owner, built.Comp, tiles);
            grid = built.Owner;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        return grid;
    }

    /// <summary>
    /// Map-initialises a code-built hull.
    /// MapManager's grid creation deliberately leaves a new grid un-map-initialised even on a live map, and adding a
    /// component only re-raises MapInitEvent on an entity that IS map-initialised. Without this the crack's
    /// AddComp&lt;ForceAnchorComponent&gt; never reaches ForceAnchorSystem's handler, so the hull would never actually
    /// go static and every lock assertion would be testing nothing.
    /// </summary>
    public static async Task MapInitHull(TestPair pair, EntityUid grid)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => entMan.RunMapInit(grid, entMan.GetComponent<MetaDataComponent>(grid)));
        await server.WaitRunTicks(1);
    }

    /// <summary>
    /// Winds every PowerCharge machine on a hull up to full so its gravity generator activates. The shipped charge
    /// rates are 100 s for the mini gravgen and 240 s for the centrifuge, which no integration test can tick through;
    /// PowerChargeComponent is [Access(typeof(PowerChargeSystem))], so the rate is raised by reflection and the
    /// machine still has to charge, activate and light the grid on its own.
    /// </summary>
    public static async Task Energise(TestPair pair, EntityUid grid)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            foreach (var uid in Children(entMan, grid))
            {
                if (entMan.TryGetComponent(uid, out PowerChargeComponent? charge))
                    ChargeRateProperty.SetValue(charge, 10f);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));
    }

    /// <summary>
    /// Pins one charging machine's ramp so a parked charge stays parked. Without it the shipped rate keeps creeping the
    /// charge back up under every threshold assertion.
    /// </summary>
    public static async Task FreezeCharge(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => ChargeRateProperty.SetValue(entMan.GetComponent<PowerChargeComponent>(uid), 0f));
        await server.WaitRunTicks(1);
    }

    /// <summary>
    /// Parks one charging machine at an exact charge and lets the centrifuge sweep read it. PowerChargeComponent is
    /// [Access(typeof(PowerChargeSystem))] and the shipped ramp is 240 s, so the level is set by reflection the same
    /// way Energise sets the rate; a full second covers the sweep's own 0.25 s gate.
    /// </summary>
    public static async Task SetCharge(TestPair pair, EntityUid uid, float charge)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => ChargeProperty.SetValue(entMan.GetComponent<PowerChargeComponent>(uid), charge));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
    }

    /// <summary>Builds a full crack site: an Asclepiu stack, laid ground, and a surveying cracker hull in orbit.</summary>
    public static async Task<CrackerSite> BuildCrackerInOrbit(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);

        var stack = await BuildOwnedStack(pair);
        var site = new CrackerSite { Layers = stack.Layers, Planet = stack.Body, PlanetMap = stack.BodyMap };

        // Wide enough that a radius-10 cut circle plus its rim ring is hand-laid deck rather than biome terrain, which
        // is what makes the extraction's tile counts deterministic instead of seed-dependent.
        await LayTiles(pair, site.Ground, new Vector2i(-16, -16), new Vector2i(32, 16));

        var orbitMap = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFOrbitLayerComponent>(site.Orbit), Is.True,
                "Precondition: the top layer of the stack is the orbit layer.");

            orbitMap = entMan.GetComponent<MapComponent>(site.Orbit).MapId;
        });

        // The hull has to sit on the orbit layer: that is the survey edge, and it is what the fall has to leave.
        site.Cracker = await BuildCracker(pair, orbitMap);
        await MapInitHull(pair, site.Cracker);

        // Two seconds covers the cracker sweep's 1 Hz gate, which is what walks Idle to Surveying.
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            site.Console = FindConsole(entMan, site.Cracker);
            site.Centrifuge = FindCentrifuge(entMan, site.Cracker);
            site.Projectors = FindProjectors(entMan, site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(site.Console, Is.Not.EqualTo(EntityUid.Invalid), "The hull carries no crack console.");
                Assert.That(site.Centrifuge, Is.Not.EqualTo(EntityUid.Invalid), "The hull carries no centrifuge.");
                Assert.That(site.Projectors, Has.Count.EqualTo(2), "The hull does not carry its two projectors.");
                Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                    Is.EqualTo(WFCrackState.Surveying), "A hull parked on the orbit layer should be surveying.");
            }
        });

        return site;
    }

    /// <summary>
    /// Spawns an owned anchor pair on the ground layer, wrenches both down and optionally finishes both drills, which
    /// is what walks the hull Surveying to AnchorsPlaced to AnchorsLocked.
    /// </summary>
    public static async Task DeployPair(TestPair pair, CrackerSite site, float ax, float bx, bool drill)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var anchors = server.System<WFGravityAnchorSystem>();

        await server.WaitPost(() =>
        {
            // Hand-spawned anchors belong to nobody, and an unowned pair is invisible to the hull's state machine.
            foreach (var x in new[] { ax, bx })
            {
                var anchor = entMan.SpawnEntity(AnchorProto, new EntityCoordinates(site.Ground, new Vector2(x + 0.5f, 0.5f)));
                entMan.GetComponent<WFGravityAnchorComponent>(anchor).Cracker = entMan.GetNetEntity(site.Cracker);
                site.Anchors.Add(anchor);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitPost(() =>
        {
            foreach (var anchor in site.Anchors)
            {
                transform.AnchorEntity(anchor);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        if (!drill)
            return;

        // The shipped drill is five minutes; the admin path finishes it with the same lock, thunk and event.
        await server.WaitPost(() =>
        {
            foreach (var anchor in site.Anchors)
            {
                anchors.CompleteDrill((anchor, entMan.GetComponent<WFGravityAnchorComponent>(anchor)));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
    }

    /// <summary>
    /// A hull in orbit with a drilled, owned pair, flown onto the cut circle and a rotor wound to full: everything a
    /// begin-crack needs, with nothing blocking.
    /// </summary>
    public static async Task<CrackerSite> BuildReadyToCut(TestPair pair, Vector2? offset = null)
    {
        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, offset ?? Vector2.Zero);
        await Energise(pair, site.Cracker);
        return site;
    }

    /// <summary>
    /// Widens the test hull's berth. The factory shrinks it to 12x12 at Distance 8 because the hull itself is tiny
    /// (WFTestGridFactory), and no faithfully sized disc fits in that, so any extraction test has to grow it first.
    /// </summary>
    public static async Task EnlargeBerth(TestPair pair, CrackerSite site, Vector2i size, float distance)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var cracker = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            Assert.That(entMan.TryGetEntity(cracker.Berth, out var berth), Is.True,
                "Precondition: the hull resolved its berth marker at map init.");

            var marker = entMan.GetComponent<WFChunkBerthComponent>(berth!.Value);
            marker.Size = size;
            marker.Distance = distance;
            entMan.Dirty(berth.Value, marker);
        });

        // UpdateBerthPose runs off the cracker sweep, which is 1 Hz while nothing is cutting.
        await server.WaitRunTicks(pair.SecondsToTicks(2f));
    }

    /// <summary>
    /// A hull that could cut a disc free right now: a berth big enough to hold one, a drilled pair 16 tiles apart
    /// (radius 10, inside the anchors' 16..40 band) and the hull flown onto the circle with a full rotor.
    /// </summary>
    public static async Task<CrackerSite> BuildReadyToExtract(TestPair pair)
    {
        var site = await BuildCrackerInOrbit(pair);

        await EnlargeBerth(pair, site, new Vector2i(24, 24), 20f);
        await DeployPair(pair, site, 0f, 16f, true);
        await AlignHull(pair, site, Vector2.Zero);
        await Energise(pair, site.Cracker);

        return site;
    }

    /// <summary>
    /// Finishes the cut through the same call the timer's expiry makes, which is what raises the extraction hook.
    /// The shipped cut is eight minutes at this pair distance, so no test can tick one out.
    /// </summary>
    public static async Task CompleteCut(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();

        await server.WaitPost(() =>
            crackers.CompleteCrack((site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker))));

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
    }

    /// <summary>Builds a site, begins its cut and completes it, so the disc is already hanging in the berth.</summary>
    public static async Task<CrackerSite> BuildExtracted(TestPair pair)
    {
        var site = await BuildReadyToExtract(pair);

        await BeginCut(pair, site);
        await CompleteCut(pair, site);

        return site;
    }

    /// <summary>
    /// A hull whose disc is cut free and whose two anchors have both been switched off in the same tick, which is the
    /// whole disconnect: the first switch-off arms the 60 s pairing window and the second commits it.
    /// ForceSwitchOff is used rather than the verb because it skips the cancellable attempt entirely, which is exactly
    /// the `wfcracker disconnect` path the protocol has to work through.
    /// </summary>
    public static async Task<CrackerSite> BuildDisconnecting(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();
        var site = await BuildExtracted(pair);

        await server.WaitPost(() =>
        {
            foreach (var anchor in site.Anchors)
            {
                anchors.ForceSwitchOff((anchor, entMan.GetComponent<WFGravityAnchorComponent>(anchor)));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker).State,
                Is.EqualTo(WFCrackState.Disconnecting),
                "Precondition: both anchors switched off in one tick commits the disconnect."));

        return site;
    }

    /// <summary>
    /// A crack site with one live deep vein anchored on the ground and the ground grid wearing a FAKE chunk marker, so
    /// F6's behaviour can be driven without going anywhere near F5's extraction. Returns the site and the vein.
    /// Two traps are worth spelling out, because both are silent.
    /// (a) WFDeepVeinSystem.OnMapInit QueueDels any vein whose tile is not in its own AllowedTiles, and the whole
    /// fixture lays FloorSteel, which the shipped default { FloorPlanetGrass, FloorPlanetDirt } excludes. That is what
    /// <see cref="VeinProto"/> exists for - a child of WFDeepVein whose whitelist is the deck plating - and it is also
    /// why the site is built on BuildCrackerInOrbit: the vein stamps itself from the ground layer's
    /// WFPlanetLayer -> WFPlanetNetwork -> WFSurfaceAsclepiu chain, whose `veins: WFVeinTableAsclepiu` is the only
    /// thing standing between a hand-spawned vein and a second self-delete.
    /// (b) The chunk marker is a fake on the planet's OWN ground grid, which keeps the F6 tests independent of F5. The
    /// hour-long WatchdogGrace is what stops WFPlanetChunkSystem's 1 Hz sweep from reading that grid as an orphaned
    /// chunk and calling DropChunk on the planet itself.
    /// </summary>
    public static async Task<(CrackerSite Site, EntityUid Vein)> BuildMinerSite(TestPair pair, Vector2i? tile = null)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildCrackerInOrbit(pair);
        var index = tile ?? new Vector2i(4, 4);

        await LayTiles(pair, site.Ground, index - new Vector2i(2, 2), index + new Vector2i(2, 2));

        var vein = EntityUid.Invalid;

        // The DeployPair recipe verbatim: spawn on the tile centre, one tick for the spawn to initialise, anchor inside
        // a WaitPost, two ticks for the snap-grid write and the physics settle.
        await server.WaitPost(() => vein = entMan.SpawnEntity(VeinProto,
            new EntityCoordinates(site.Ground, new Vector2(index.X + 0.5f, index.Y + 0.5f))));

        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            if (entMan.EntityExists(vein) && !entMan.GetComponent<TransformComponent>(vein).Anchored)
                transform.AnchorEntity(vein);
        });

        await server.WaitRunTicks(2);

        await server.WaitPost(() =>
        {
            var chunk = entMan.EnsureComponent<WFPlanetChunkComponent>(site.Ground);

            chunk.WatchdogGrace = TimeSpan.FromHours(1);
            chunk.ExtractedAt = timing.CurTime;
            entMan.Dirty(site.Ground, chunk);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.EntityExists(vein), Is.True,
                    "The test vein deleted itself; its whitelist no longer admits the fixture's deck plating.");
                Assert.That(entMan.GetComponent<TransformComponent>(vein).Anchored, Is.True,
                    "The test vein is not anchored, so no tile-index lookup would ever find it.");
                Assert.That(entMan.GetComponent<WFDeepVeinComponent>(vein).Remaining, Is.GreaterThan(0),
                    "The test vein was never stamped, so there is nothing for a miner to cut.");
            }
        });

        return (site, vein);
    }

    /// <summary>
    /// Puts a cell of a known charge in a miner's bay and hands it back.
    /// The eject and the assertion are both mandatory. WFCrackMiner declares `startingItem: PowerCellHigh`, which
    /// ItemSlotsSystem spawns and inserts on MapInit (ItemSlotsSystem.cs:68-80), so an insert into an occupied slot
    /// returns false (CanInsert, :325-326) and a test that ignored the result would go on mining off the free full
    /// 1080 J cell it never meant to use. SetCharge rather than a field write because BatteryComponent is
    /// [Access(typeof(SharedBatterySystem))]; read the charge back with PowerCellSystem.TryGetBatteryFromSlot.
    /// </summary>
    public static async Task<EntityUid> SeatCell(TestPair pair, EntityUid miner, float charge, string cell = CellProto)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var batteries = server.System<BatterySystem>();
        var slots = server.System<ItemSlotsSystem>();
        var seated = EntityUid.Invalid;

        // Tolerates an empty slot: WFCrackMinerEmpty has nothing to throw out.
        await server.WaitPost(() => slots.TryEject(miner, CellSlot, null, out _));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            seated = entMan.SpawnEntity(cell, entMan.GetComponent<TransformComponent>(miner).Coordinates);

            Assert.That(slots.TryInsert(miner, CellSlot, seated, null), Is.True,
                $"The miner refused {cell}; a silent refusal must never masquerade as a seated cell.");

            batteries.SetCharge(seated, charge);
        });

        await server.WaitRunTicks(1);
        return seated;
    }

    /// <summary>Every unit of one ore entity lying loose on a grid, stack counts summed.</summary>
    public static int OreOnGrid(IEntityManager entMan, EntityUid grid, string oreEntity)
    {
        var total = 0;

        foreach (var uid in Children(entMan, grid))
        {
            if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID != oreEntity)
                continue;

            total += entMan.TryGetComponent(uid, out StackComponent? stack) ? stack.Count : 1;
        }

        return total;
    }

    /// <summary>
    /// Zeroes the chunk's per-tile crash intensity, and is MANDATORY for every test that lets a chunk land.
    /// At the production defaults (CrashTileIntensity 4, CrashTileMaxIntensity 2) the crash footprint is roughly twice
    /// the disc's area - QueueExplosion merges same-prototype blasts within one tile by ADDING TotalIntensity while
    /// MaxTileIntensity caps the per-tile output - so rim tiles are inside it. The Default prototype's TileBreakChance
    /// interpolates to about 0.1 per tile at intensity 2 (tileBreakChance [0, 0.5, 1] over tileBreakIntensity
    /// [0, 10, 30], linear at ExplosionPrototype.cs:126-139) and DecalSystem.OnTileChanged deletes any decal on a tile
    /// that becomes space (DecalSystem.cs:165-177), so rim tiles and rim decals are probabilistic under a live crash.
    /// Zero suppresses the per-tile blasts entirely through the totalIntensity &lt;= 0 early return
    /// (ExplosionSystem.cs:374), which is what makes a landing deterministic enough to assert against.
    /// Must be called BEFORE the drop: DropChunk copies the value onto CEZGridFallerComponent.
    /// </summary>
    public static async Task SoftenCrash(TestPair pair, EntityUid chunk)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => entMan.GetComponent<WFPlanetChunkComponent>(chunk).CrashTileIntensity = 0f);
        await server.WaitRunTicks(1);
    }

    /// <summary>The one chunk grid in the world, or Invalid; extraction tests only ever cut one.</summary>
    public static EntityUid FindChunk(IEntityManager entMan)
    {
        var query = entMan.AllEntityQueryEnumerator<WFPlanetChunkComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>
    /// Every tile index whose CENTRE falls inside the cut circle, which is the extraction's own membership test and
    /// therefore the only correct definition of "the disc" for an assertion.
    /// </summary>
    public static List<Vector2i> DiscIndices(IEntityManager entMan, EntityUid ground, Vector2 centre, float radius)
    {
        return IndicesInBand(entMan, ground, centre, null, radius);
    }

    /// <summary>The ring of indices whose tile centre falls in (radius, radius + 1]; the decals' own ground.</summary>
    public static List<Vector2i> RimIndices(IEntityManager entMan, EntityUid ground, Vector2 centre, float radius)
    {
        return IndicesInBand(entMan, ground, centre, radius, radius + 1f);
    }

    /// <summary>What the ground grid currently holds at every disc index, for the hole assertions.</summary>
    public static List<(Vector2i Index, Tile Tile)> HoleTiles(IEntityManager entMan, EntityUid ground, Vector2 centre, float radius)
    {
        var maps = entMan.System<SharedMapSystem>();
        var grid = entMan.GetComponent<MapGridComponent>(ground);
        var tiles = new List<(Vector2i, Tile)>();

        foreach (var index in DiscIndices(entMan, ground, centre, radius))
        {
            tiles.Add((index, maps.GetTileRef(ground, grid, index).Tile));
        }

        return tiles;
    }

    /// <summary>How many of these indices the biome has pinned against regeneration.</summary>
    public static int PinnedCount(TestPair pair, EntityUid ground, IEnumerable<Vector2i> indices)
    {
        var entMan = pair.Server.EntMan;
        var biomes = pair.Server.System<BiomeSystem>();
        var biome = new Entity<BiomeComponent>(ground, entMan.GetComponent<BiomeComponent>(ground));
        var pinned = 0;

        foreach (var index in indices)
        {
            if (biomes.WfIsPinned(biome, index))
                pinned++;
        }

        return pinned;
    }

    /// <summary>
    /// Indices whose tile centre sits in the half-open ring (inner, outer], walked over the outer box.
    /// A null inner means no lower bound at all, which is not the same as zero: the tile sitting exactly on the circle
    /// centre is inside the disc and a zero bound would silently drop it.
    /// </summary>
    private static List<Vector2i> IndicesInBand(IEntityManager entMan, EntityUid ground, Vector2 centre, float? inner, float outer)
    {
        var half = entMan.GetComponent<MapGridComponent>(ground).TileSizeHalfVector;
        var innerSq = inner is { } value ? value * value : -1f;
        var outerSq = outer * outer;
        var found = new List<Vector2i>();

        var span = outer + 2f;
        var minX = (int)MathF.Floor(centre.X - span);
        var maxX = (int)MathF.Ceiling(centre.X + span);
        var minY = (int)MathF.Floor(centre.Y - span);
        var maxY = (int)MathF.Ceiling(centre.Y + span);

        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var index = new Vector2i(x, y);
            var distanceSq = ((Vector2)index + half - centre).LengthSquared();

            if (distanceSq <= innerSq || distanceSq > outerSq)
                continue;

            found.Add(index);
        }

        return found;
    }

    /// <summary>Targets the hull's pair and begins the cut, failing loudly on either refusal.</summary>
    public static async Task BeginCut(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();

        await server.WaitPost(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            Assert.That(crackers.TryTarget(cracker, out var targeting), Is.True,
                $"Precondition: the pair targets: {targeting}");
            Assert.That(crackers.TryBegin(cracker, out var reason), Is.True,
                $"Precondition: the cut begins: {reason}");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
    }

    /// <summary>Parks a hull at a world position on its current map, the way a pilot would fly it there.</summary>
    public static async Task MoveHullTo(TestPair pair, EntityUid hull, Vector2 position)
    {
        var server = pair.Server;
        var transform = server.System<SharedTransformSystem>();

        await server.WaitPost(() => transform.SetWorldPosition(hull, position));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
    }

    /// <summary>
    /// Flies the hull so its berth centre lands exactly the given raw XY delta short of the cut circle centre, which is
    /// what TryGetBerthOffset then reports. The berth centre is read back off the marker rather than assumed, so the
    /// hull layout can change without every alignment test moving with it.
    /// </summary>
    public static async Task AlignHull(TestPair pair, CrackerSite site, Vector2 offset)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();
        var transform = server.System<SharedTransformSystem>();

        await server.WaitPost(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            Assert.That(crackers.TryGetBerthCentre(cracker, out var centre), Is.True,
                "Precondition: the hull resolved its berth centre.");
            Assert.That(crackers.TryGetOwnedPair(cracker, out var a, out var b, false), Is.True,
                "Precondition: the hull owns a pair to aim at.");
            Assert.That(crackers.TryGetCircle(a.Owner, b.Owner, out var circle, out _), Is.True,
                "Precondition: the pair has a cut circle.");

            // The berth sits at a fixed grid-local place, so the hull pose that puts it where we want is arithmetic.
            var local = centre.Position - transform.GetWorldPosition(site.Cracker);
            transform.SetWorldPosition(site.Cracker, circle - offset - local);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
    }

    /// <summary>
    /// The exact state object a client would receive, built server-side with no window standing up.
    /// Must be called from inside a server thread callback.
    /// </summary>
    public static WFCrackConsoleState ReadConsoleState(TestPair pair, CrackerSite site)
    {
        return pair.Server.System<WFCrackConsoleSystem>().BuildState(site.Console);
    }

    /// <summary>The crack console resting on a hull, or Invalid.</summary>
    public static EntityUid FindConsole(IEntityManager entMan, EntityUid cracker)
    {
        foreach (var uid in Children(entMan, cracker))
        {
            if (entMan.HasComponent<WFCrackConsoleComponent>(uid))
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>The shuttle console resting on a hull, or Invalid; the one the orbit and flight actions come from.</summary>
    public static EntityUid FindShuttleConsole(IEntityManager entMan, EntityUid hull)
    {
        foreach (var uid in Children(entMan, hull))
        {
            if (entMan.HasComponent<ShuttleConsoleComponent>(uid))
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>
    /// Seats a pilot at the hull's own shuttle console holding the descend key. CollectPilotVerticalInputs reads
    /// nothing but the console's grid and the held buttons (CEZLevelsSystem.PilotControl.cs), so the input is written
    /// directly rather than driven through the console UI. Returns the pilot.
    /// </summary>
    public static Task<EntityUid> HoldDescend(TestPair pair, EntityUid hull)
    {
        return HoldVertical(pair, hull, ShuttleButtons.DescendZ);
    }

    /// <summary>Seats a pilot at the hull's own shuttle console holding one of the two vertical keys.</summary>
    public static async Task<EntityUid> HoldVertical(TestPair pair, EntityUid hull, ShuttleButtons button)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var pilot = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var console = FindShuttleConsole(entMan, hull);

            Assert.That(console, Is.Not.EqualTo(EntityUid.Invalid), "The hull has no shuttle console to pilot from.");

            pilot = entMan.SpawnEntity(ViewerProto, new EntityCoordinates(hull, new Vector2(2.5f, 3.5f)));
            var pilotComp = entMan.EnsureComponent<PilotComponent>(pilot);
            pilotComp.Console = console;
            pilotComp.HeldButtons = button;
        });

        await server.WaitRunTicks(1);

        return pilot;
    }

    /// <summary>
    /// Drops a hull out of orbit through the console action F10 put the decision behind, and hands back the refusal
    /// text when the server said no. The pilot gate is bypassed deliberately: the system method is the authority and
    /// the BUI message only forwards to it.
    /// </summary>
    /// <param name="settle">Seconds of ticks to run afterwards; zero leaves the hull exactly where the call put it.</param>
    public static async Task<string?> EnterAtmosphere(TestPair pair, EntityUid hull, bool confirmed = true, float settle = 1f)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbit = server.System<WFOrbitEntrySystem>();
        string? reason = null;

        await server.WaitPost(() =>
        {
            var console = FindShuttleConsole(entMan, hull);

            Assert.That(console, Is.Not.EqualTo(EntityUid.Invalid), "The hull has no shuttle console to descend from.");

            orbit.TryEnterAtmosphere(console, confirmed, out reason);
        });

        if (settle > 0f)
            await server.WaitRunTicks(pair.SecondsToTicks(settle));

        return reason;
    }

    /// <summary>Bolts converted thrusters onto a hull. The legacy lift parameter is capacity; rated force is lift times 9.81.</summary>
    public static async Task<List<EntityUid>> AddLandingThrusters(TestPair pair, EntityUid hull, int count, float lift = 50f)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var receiver = server.System<SharedPowerReceiverSystem>();
        var thrusters = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var i = 0; i < count; i++)
            {
                var uid = entMan.SpawnEntity("WFThrusterLanding", new EntityCoordinates(hull, new Vector2(1.5f + i, 1.5f)));

                // No cabling on a code-built hull, exactly as WFTestGridFactory.SpawnOnHull does it.
                receiver.SetNeedsPower(uid, false);
                server.System<ThrusterSystem>().WfSetRatedThrust(uid, lift * 9.81f);

                thrusters.Add(uid);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        return thrusters;
    }

    /// <summary>Isolates gravgen and converted-lift tests from the cracker's ordinary engines.</summary>
    public static async Task RemoveOrdinaryThrusters(TestPair pair, EntityUid hull)
    {
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.EntMan;
            foreach (var uid in Children(entMan, hull))
            {
                if (entMan.TryGetComponent<ThrusterComponent>(uid, out var thruster)
                    && thruster.Type == ThrusterType.Linear
                    && !entMan.HasComponent<WFLandingThrusterComponent>(uid))
                    entMan.DeleteEntity(uid);
            }
        });
    }

    /// <summary>The centrifuge resting on a hull, or Invalid.</summary>
    public static EntityUid FindCentrifuge(IEntityManager entMan, EntityUid cracker)
    {
        foreach (var uid in Children(entMan, cracker))
        {
            if (entMan.HasComponent<WFCentrifugeComponent>(uid))
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>Every gravity projector resting on a hull, ordered by grid-local X the way the console rows are.</summary>
    public static List<EntityUid> FindProjectors(IEntityManager entMan, EntityUid cracker)
    {
        var found = new List<EntityUid>();

        foreach (var uid in Children(entMan, cracker))
        {
            if (entMan.HasComponent<WFGravityProjectorComponent>(uid))
                found.Add(uid);
        }

        found.Sort((x, y) => entMan.GetComponent<TransformComponent>(x).LocalPosition.X
            .CompareTo(entMan.GetComponent<TransformComponent>(y).LocalPosition.X));

        return found;
    }

    /// <summary>The marker-layer set a biome generates from, which is access-locked to SharedBiomeSystem.</summary>
    private static HashSet<ProtoId<BiomeMarkerLayerPrototype>> MarkerLayers(BiomeComponent biome)
    {
        return (HashSet<ProtoId<BiomeMarkerLayerPrototype>>) ResolveField("MarkerLayers").GetValue(biome)!;
    }

    /// <summary>The one-shot forcing set, cleared at the end of every marker pass.</summary>
    private static HashSet<ProtoId<BiomeMarkerLayerPrototype>> ForcedMarkerLayers(BiomeComponent biome)
    {
        return (HashSet<ProtoId<BiomeMarkerLayerPrototype>>) ResolveField("ForcedMarkerLayers").GetValue(biome)!;
    }

    /// <summary>Which marker chunks are already done, which is the guard a second forced pass has to trip over.</summary>
    private static Dictionary<string, HashSet<Vector2i>> LoadedMarkers(BiomeComponent biome)
    {
        return (Dictionary<string, HashSet<Vector2i>>) ResolveField("LoadedMarkers").GetValue(biome)!;
    }

    /// <summary>One access-locked biome field, failing loudly here rather than as a null deref in a test.</summary>
    private static FieldInfo ResolveField(string name)
    {
        var field = typeof(BiomeComponent).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(field, Is.Not.Null, $"BiomeComponent.{name} was not found.");
        return field!;
    }

    /// <summary>PowerChargeComponent.ChargeRate, which is access-locked to its own system.</summary>
    private static readonly PropertyInfo ChargeRateProperty = Resolve("ChargeRate");

    /// <summary>PowerChargeComponent.Charge, which is access-locked to its own system.</summary>
    private static readonly PropertyInfo ChargeProperty = Resolve("Charge");

    /// <summary>One access-locked charge property, failing loudly here rather than as a null deref in a test.</summary>
    private static PropertyInfo Resolve(string name)
    {
        var property = typeof(PowerChargeComponent).GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(property, Is.Not.Null, $"PowerChargeComponent.{name} was not found.");
        return property!;
    }
}

/// <summary>One built crack site: the planet stack, the hull in orbit and everything on it the tests reach for.</summary>
public sealed class CrackerSite
{
    /// <summary>The stack's layers, ground first, orbit last.</summary>
    public List<EntityUid> Layers = new();

    /// <summary>The biome ground layer the anchors are wrenched down on.</summary>
    public EntityUid Ground => Layers[0];

    /// <summary>The vacuum orbit layer the hull parks on.</summary>
    public EntityUid Orbit => Layers[^1];

    /// <summary>The sector body that owns the stack; where the cracked flag lands.</summary>
    public EntityUid Planet;

    /// <summary>The throwaway sector map the body was spawned on, torn down with the site.</summary>
    public EntityUid PlanetMap;

    /// <summary>The cracker hull grid.</summary>
    public EntityUid Cracker;

    /// <summary>The hull's crack control console.</summary>
    public EntityUid Console;

    /// <summary>The hull's gravitic centrifuge.</summary>
    public EntityUid Centrifuge;

    /// <summary>The hull's gravity projectors, ordered by grid-local X.</summary>
    public List<EntityUid> Projectors = new();

    /// <summary>Anchors deployed on the ground layer for this site.</summary>
    public List<EntityUid> Anchors = new();
}
