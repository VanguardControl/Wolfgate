#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.ShipRepair;
using Content.Server._WF.ShipRepair;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Decals;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WF.ShipRepair;
using Content.Shared.Atmos;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.ShipRepair;

/// <summary>One repair snapshot per broken hull, and the SRD's rebuild guard.</summary>
[TestFixture]
[TestOf(typeof(WFHullSectionSystem))]
public sealed class HullSectionTest
{
    /// <summary>Where the test hull sits on the test map, clear of the map's own one-tile grid.</summary>
    private static readonly Vector2 HullOrigin = new(20f, 20f);

    /// <summary>The column cut out of the 15x15 test hull; the four columns past it become the section.</summary>
    private const int SeamColumn = 10;

    /// <summary>Speeds the SRD's do-afters up so a reattach takes about a second at most.</summary>
    private const float QuickTool = 0.03f;

    /// <summary>The biggest piece keeps the snapshot; a section, and a section of a section, only link back to it.</summary>
    [Test]
    public async Task SplitKeepsOneSnapshotAndLinksEverySection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var (hull, section) = await BuildSplitHull(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ShipRepairDataComponent>(hull).Chunks, Is.Not.Empty);
            Assert.That(em.HasComponent<ShipRepairDataComponent>(section), Is.False, "The section carries its own copy of the blueprint.");

            Cut(pair, section, Enumerable.Range(SeamColumn + 1, 4).Select(x => new Vector2i(x, 7)));
            var sections = SectionsOf(em, hull);
            Assert.That(sections, Has.Count.EqualTo(2), "A piece of the section did not link back to the hull.");
            Assert.That(sections.Any(uid => em.HasComponent<ShipRepairDataComponent>(uid)), Is.False,
                "A piece of the section carries its own copy of the blueprint.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A deleted hull hands its snapshot to its largest section on the same map; a section with none left is wreckage.</summary>
    [Test]
    public async Task DeletingTheHullPromotesItsLargestSection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var (hull, large) = await BuildSplitHull(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            var chunks = em.GetComponent<ShipRepairDataComponent>(hull).Chunks.Count;
            // The top four rows of the hull's side make a second, smaller section.
            Cut(pair, hull, Enumerable.Range(0, SeamColumn).Select(x => new Vector2i(x, 10)));
            var small = SectionsOf(em, hull).Single(uid => uid != large);

            em.DeleteEntity(hull);

            Assert.That(em.TryGetComponent<ShipRepairDataComponent>(large, out var inherited), Is.True,
                "The largest section did not take over the blueprint.");
            Assert.That(inherited!.Chunks, Has.Count.EqualTo(chunks));
            Assert.That(em.HasComponent<WFHullSectionComponent>(large), Is.False, "The new hull still links to the old one.");
            Assert.That(em.GetComponent<WFHullSectionComponent>(small).Hull, Is.EqualTo(large), "The other section was not repointed.");

            // With its new hull gone and no section left on that map, the last piece is plain wreckage.
            var elsewhere = server.System<SharedMapSystem>().CreateMap(out _);
            server.System<SharedTransformSystem>().SetCoordinates(small, new EntityCoordinates(elsewhere, Vector2.Zero));
            em.DeleteEntity(large);

            Assert.That(em.HasComponent<WFHullSectionComponent>(small), Is.False, "A section with no hull left is still linked.");
            Assert.That(em.HasComponent<ShipRepairDataComponent>(small), Is.False, "A section on another map took over the blueprint.");
            em.DeleteEntity(elsewhere);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The SRD rebuilds the hull except where a nearby section belongs or another grid has moved in.</summary>
    [Test]
    public async Task RebuildLeavesRoomForSectionsAndOtherGrids()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var (hull, section) = await BuildSplitHull(pair, map.MapId);
        var debris = await BuildDebris(pair, map.MapId, 3, new Vector2(-40f, -40f));
        var seam = new Vector2i(SeamColumn, 5);
        var reserved = new Vector2i(SeamColumn + 1, 5);
        var free = new Vector2i(3, 3);
        var other = new Vector2i(4, 3);

        await server.WaitPost(() =>
        {
            Hold(pair, hull, section, debris);
            Cut(pair, hull, new[] { free, other });
            xforms.SetWorldPosition(section, xforms.GetWorldPosition(section) + new Vector2(5f, 0f));
        });
        var (user, tool) = await Worker(pair, () => new EntityCoordinates(hull, new Vector2(5.5f, 9.5f)));

        // The seam is nobody's place, so it rebuilds.
        await server.WaitPost(() => Click(em, user, tool, TileCentre(hull, seam)));
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, seam), Is.True, "The SRD no longer rebuilds a clear tile."));

        // The section sits five tiles off its place: its tiles are reserved.
        await server.WaitPost(() => Click(em, user, tool, TileCentre(hull, reserved)));
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, reserved), Is.False,
            "The SRD rebuilt the hull where a detached section belongs."));

        // Out of reattach range the section reserves nothing.
        await server.WaitPost(() =>
        {
            xforms.SetWorldPosition(section, xforms.GetWorldPosition(section) + new Vector2(100f, 0f));
            Click(em, user, tool, TileCentre(hull, reserved));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, reserved), Is.True,
            "A section out of range still reserved its place."));

        // A grid that moves over the tile while the repair runs stops it.
        await server.WaitPost(() =>
        {
            Click(em, user, tool, TileCentre(hull, free));
            var spot = Vector2.Transform(maps.TileCenterToVector((hull, em.GetComponent<MapGridComponent>(hull)), free), xforms.GetWorldMatrix(hull));
            xforms.SetWorldPosition(debris, spot - new Vector2(1.5f, 1.5f));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, free), Is.False,
            "The SRD rebuilt a tile under another grid."));

        // Clear of other grids and sections, both tiles rebuild.
        await server.WaitPost(() =>
        {
            xforms.SetWorldPosition(debris, new Vector2(-40f, -40f));
            Click(em, user, tool, TileCentre(hull, free));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitPost(() => Click(em, user, tool, TileCentre(hull, other)));
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(HasTile(pair, hull, free), Is.True, "The SRD no longer rebuilds a clear tile.");
            Assert.That(HasTile(pair, hull, other), Is.True, "The SRD no longer rebuilds a clear tile.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Standing on planet ground, the SRD works on the hull beside the click rather than on the ground map.</summary>
    [Test]
    public async Task SrdUsedFromPlanetGroundReachesTheHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-32, -32), new Vector2i(64, 64));
        var hull = await BuildHull(pair, em.GetComponent<MapComponent>(ground).MapId);
        await MapInitHull(pair, hull);
        var edge = new Vector2i(0, 5);

        await server.WaitPost(() =>
        {
            server.System<ShipRepairSystem>().GenerateRepairData(hull);
            Cut(pair, hull, new[] { edge });
        });
        var (user, tool) = await Worker(pair, () =>
        {
            var world = Vector2.Transform(new Vector2(-1.5f, 5.5f), server.System<SharedTransformSystem>().GetWorldMatrix(hull));
            return new EntityCoordinates(ground, world);
        }, 1f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(user).GridUid, Is.EqualTo(ground), "The crew member is not standing on planet ground.");
            Click(em, user, tool, TileCentre(hull, edge));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, edge), Is.True,
            "The SRD used from planet ground did not rebuild the hull."));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Builds the test hull, snapshots it and cuts the seam column; returns the hull and its one section.</summary>
    private static async Task<(EntityUid Hull, EntityUid Section)> BuildSplitHull(TestPair pair, MapId map, Action<EntityUid>? beforeCut = null)
    {
        var server = pair.Server;
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.GridSplitting, true));
        var hull = await BuildHull(pair, map, HullOrigin);
        var section = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            beforeCut?.Invoke(hull);
            server.System<ShipRepairSystem>().GenerateRepairData(hull);
            Cut(pair, hull, Enumerable.Range(0, 15).Select(y => new Vector2i(SeamColumn, y)));
            var sections = SectionsOf(server.EntMan, hull);
            Assert.That(sections, Has.Count.EqualTo(1), "Cutting the seam did not leave one section linked to the hull.");
            section = sections[0];
        });

        return (hull, section);
    }

    /// <summary>Removes tiles; a cut across the grid splits it.</summary>
    private static void Cut(TestPair pair, EntityUid grid, IEnumerable<Vector2i> tiles)
    {
        var comp = pair.Server.EntMan.GetComponent<MapGridComponent>(grid);
        pair.Server.System<SharedMapSystem>().SetTiles(grid, comp, tiles.Select(tile => (tile, Tile.Empty)).ToList());
    }

    /// <summary>Makes grids static, so nothing pushes them apart while a test lines them up.</summary>
    private static void Hold(TestPair pair, params EntityUid[] grids)
    {
        foreach (var grid in grids)
        {
            pair.Server.System<SharedPhysicsSystem>().SetBodyType(grid, BodyType.Static);
        }
    }

    /// <summary>Every grid linked to a hull.</summary>
    private static List<EntityUid> SectionsOf(IEntityManager entMan, EntityUid hull)
    {
        var found = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<WFHullSectionComponent>();
        while (query.MoveNext(out var uid, out var section))
        {
            if (section.Hull == hull)
                found.Add(uid);
        }

        return found;
    }

    /// <summary>Spawns a crew member holding an SRD, its do-afters scaled by the multiplier.</summary>
    private static async Task<(EntityUid User, EntityUid Tool)> Worker(TestPair pair, Func<EntityCoordinates> at, float multiplier = QuickTool)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var user = EntityUid.Invalid;
        var tool = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var coords = at();
            user = entMan.SpawnEntity("MobHuman", coords);
            tool = entMan.SpawnEntity("ShipRepairDevice", coords);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            entMan.GetComponent<ShipRepairToolComponent>(tool).RepairTimeMultiplier = multiplier;
        });

        await server.WaitRunTicks(1);
        return (user, tool);
    }

    /// <summary>Uses the SRD at a spot, as a click within reach does.</summary>
    private static void Click(IEntityManager entMan, EntityUid user, EntityUid tool, EntityCoordinates at)
    {
        entMan.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(user, tool, null, at, true));
    }

    /// <summary>The centre of a grid tile, in that grid's coordinates.</summary>
    private static EntityCoordinates TileCentre(EntityUid grid, Vector2i index)
    {
        return new EntityCoordinates(grid, new Vector2(index.X + 0.5f, index.Y + 0.5f));
    }

    private static bool HasTile(TestPair pair, EntityUid grid, Vector2i index)
    {
        var comp = pair.Server.EntMan.GetComponent<MapGridComponent>(grid);
        return !pair.Server.System<SharedMapSystem>().GetTileRef(grid, comp, index).Tile.IsEmpty;
    }

}
