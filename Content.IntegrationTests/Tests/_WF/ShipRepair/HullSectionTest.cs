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

/// <summary>One repair snapshot per broken hull.</summary>
[TestFixture]
[TestOf(typeof(WFHullSectionServerSystem))]
public sealed class HullSectionTest
{
    /// <summary>Where the test hull sits on the test map, clear of the map's own one-tile grid.</summary>
    private static readonly Vector2 HullOrigin = new(20f, 20f);

    /// <summary>The column cut out of the 15x15 test hull; the four columns past it become the section.</summary>
    private const int SeamColumn = 10;

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

}
