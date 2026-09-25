#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.PlanetCracker.Anchors;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server._WF.PlanetCracker.Testing;
using Content.Server.Parallax;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Cracker.BUI;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared._WF.Planets;
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
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>Planet cracker test scaffolding on top of <see cref="PlanetFixture"/>, pulled in with <c>using static</c>.</summary>
public static class PlanetCrackerFixture
{
    /// <summary>The deployable gravity anchor.</summary>
    public const string AnchorProto = "WFGravityAnchor";

    /// <summary>The shipped crack miner, which starts with a full <see cref="CellProto"/>.</summary>
    public const string MinerProto = "WFCrackMiner";

    /// <summary>The cell-less crack miner; every cell-behaviour test uses this one and seats its own cell.</summary>
    public const string MinerEmptyProto = "WFCrackMinerEmpty";

    /// <summary>The cell <see cref="SeatCell"/> seats by default, and the one the shipped miner starts with.</summary>
    public const string CellProto = "PowerCellHigh";

    /// <summary>CrackMinerTest's deep vein, which allows the fixture's deck plating.</summary>
    public const string VeinProto = "WFCrackMinerTestVein";

    /// <summary>The miner's cell slot id, as mining.yml declares it.</summary>
    public const string CellSlot = "cell_slot";

    /// <summary>Forces a marker layer to generate; clearLoaded false exercises the double-spawn guard.</summary>
    public static async Task ForceMarkers(TestPair pair, EntityUid ground, string layer, int ticks = 40, bool clearLoaded = true)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var biome = entMan.GetComponent<BiomeComponent>(ground);

            // BiomeComponent is access-locked, so the marker sets are written by reflection.
            MarkerLayers(biome).Add(layer);

            if (clearLoaded)
                LoadedMarkers(biome).Remove(layer);

            ForcedMarkerLayers(biome).Add(layer);
        });

        await pair.RunTicksSync(ticks);
    }

    /// <summary>Tears a whole site down: the stack through its network, then the sector body's own map.</summary>
    public static async Task Teardown(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        // Inside this class the own Teardown group hides the using-static one.
        await PlanetFixture.Teardown(pair, site.Layers);

        if (site.PlanetMap == EntityUid.Invalid)
            return;

        await server.WaitPost(() =>
        {
            if (entMan.EntityExists(site.PlanetMap))
                entMan.DeleteEntity(site.PlanetMap);
        });

        await server.WaitRunTicks(1);
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

    /// <summary>Zeroes one charging machine's rate so a parked charge stays put.</summary>
    public static async Task FreezeCharge(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => ChargeRateProperty.SetValue(entMan.GetComponent<PowerChargeComponent>(uid), 0f));
        await server.WaitRunTicks(1);
    }

    /// <summary>Sets one charging machine's charge by reflection and waits a second for the centrifuge sweep.</summary>
    public static async Task SetCharge(TestPair pair, EntityUid uid, float charge)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => ChargeProperty.SetValue(entMan.GetComponent<PowerChargeComponent>(uid), charge));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
    }

    /// <summary>Builds a crack site: an Asclepiu stack, laid ground and a surveying hull in orbit.</summary>
    public static async Task<CrackerSite> BuildCrackerInOrbit(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);

        var stack = await BuildOwnedStack(pair);
        var site = new CrackerSite { Layers = stack.Layers, Planet = stack.Body, PlanetMap = stack.BodyMap };

        // Covers a radius-10 cut and its rim, so tile counts don't depend on the biome seed.
        await LayTiles(pair, site.Ground, new Vector2i(-16, -16), new Vector2i(32, 16));

        var orbitMap = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFOrbitLayerComponent>(site.Orbit), Is.True,
                "Precondition: the top layer of the stack is the orbit layer.");

            orbitMap = entMan.GetComponent<MapComponent>(site.Orbit).MapId;
        });

        // The hull surveys from the orbit layer.
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

    /// <summary>Spawns and anchors an owned pair on the ground, optionally finishing both drills.</summary>
    public static async Task DeployPair(TestPair pair, CrackerSite site, float ax, float bx, bool drill)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var anchors = server.System<WFGravityAnchorSystem>();

        await server.WaitPost(() =>
        {
            // An unowned pair is invisible to the hull's state machine.
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

        // The shipped drill takes five minutes, so finish it through the admin path.
        await server.WaitPost(() =>
        {
            foreach (var anchor in site.Anchors)
            {
                anchors.CompleteDrill((anchor, entMan.GetComponent<WFGravityAnchorComponent>(anchor)));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
    }

    /// <summary>A hull in orbit with a drilled owned pair, aligned on the cut circle with a full rotor.</summary>
    public static async Task<CrackerSite> BuildReadyToCut(TestPair pair, Vector2? offset = null)
    {
        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 24f, true);
        await AlignHull(pair, site, offset ?? Vector2.Zero);
        await Energise(pair, site.Cracker);
        return site;
    }

    /// <summary>Widens the test hull's berth, which the factory shrinks too small to hold a disc.</summary>
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

    /// <summary>A hull ready to cut: an enlarged berth, a drilled pair 16 tiles apart and a full rotor.</summary>
    public static async Task<CrackerSite> BuildReadyToExtract(TestPair pair)
    {
        var site = await BuildCrackerInOrbit(pair);

        await EnlargeBerth(pair, site, new Vector2i(24, 24), 20f);
        await DeployPair(pair, site, 0f, 16f, true);
        await AlignHull(pair, site, Vector2.Zero);
        await Energise(pair, site.Cracker);

        return site;
    }

    /// <summary>Finishes the cut through the timer's expiry call; the shipped cut is too long to tick.</summary>
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

    /// <summary>A cut hull whose anchors were both force-switched off in one tick: disconnecting.</summary>
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

    /// <summary>A crack site with one anchored deep vein and a fake chunk marker on the ground grid.</summary>
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

        // Same spawn-then-anchor sequence as DeployPair.
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

            // A long grace keeps the chunk sweep from dropping the planet's own ground as an orphan.
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

    /// <summary>Replaces a miner's cell with one of a known charge and returns it.</summary>
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

    /// <summary>Zeroes a chunk's per-tile crash intensity for a deterministic landing; call before the drop.</summary>
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

    /// <summary>Every tile index whose centre is inside the cut circle, as the extraction tests it.</summary>
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

    /// <summary>Indices whose tile centre is in (inner, outer]; a null inner keeps the centre tile.</summary>
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

    /// <summary>Moves the hull so its berth centre sits the given offset short of the cut circle centre.</summary>
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

            // The berth is fixed in grid-local space, so the hull pose follows directly.
            var local = centre.Position - transform.GetWorldPosition(site.Cracker);
            transform.SetWorldPosition(site.Cracker, circle - offset - local);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
    }

    /// <summary>The console state a client would receive, built server-side; call on the server thread.</summary>
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

    /// <summary>Every gravity projector on a hull, ordered by grid-local X like the console rows.</summary>
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
    public static HashSet<ProtoId<BiomeMarkerLayerPrototype>> MarkerLayers(BiomeComponent biome)
    {
        return (HashSet<ProtoId<BiomeMarkerLayerPrototype>>) ResolveField("MarkerLayers").GetValue(biome)!;
    }

    /// <summary>The one-shot forcing set, cleared at the end of every marker pass.</summary>
    private static HashSet<ProtoId<BiomeMarkerLayerPrototype>> ForcedMarkerLayers(BiomeComponent biome)
    {
        return (HashSet<ProtoId<BiomeMarkerLayerPrototype>>) ResolveField("ForcedMarkerLayers").GetValue(biome)!;
    }

    /// <summary>The marker chunks already loaded, which guard against a second spawn.</summary>
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

/// <summary>One built crack site: the planet stack, the hull in orbit and its machines.</summary>
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
