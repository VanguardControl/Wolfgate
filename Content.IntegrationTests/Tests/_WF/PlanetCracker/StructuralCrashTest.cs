using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.PlanetCracker.Flight;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Content.Shared.Stunnable;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

[TestFixture]
public sealed class StructuralCrashTest
{
    [Test]
    public void FracturesVaryTheirPositionAndCanShearSmallDiagonalSections()
    {
        var hull = new HashSet<Vector2i>();
        for (var x = 0; x < 40; x++)
        for (var y = 0; y < 30; y++) hull.Add(new Vector2i(x, y));
        var layouts = new HashSet<string>();
        var small = false;
        var jagged = false;
        for (var seed = 0; seed < 16; seed++)
        {
            var cut = WFCrashFractures.Plan(hull, 2, seed);
            Assert.That(cut.Select(t => t.X).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(cut.Select(t => t.Y).Distinct().Count(), Is.GreaterThan(1));
            jagged |= cut.Select(t => t.X + t.Y).Distinct().Count() > 1 && cut.Select(t => t.X - t.Y).Distinct().Count() > 1;
            layouts.Add(string.Join(";", cut.OrderBy(t => t.X).ThenBy(t => t.Y)));
            var remaining = new HashSet<Vector2i>(hull);
            remaining.ExceptWith(cut);
            var pieces = WFCrashFractures.Sections(remaining);
            Assert.That(pieces.Count, Is.EqualTo(2));
            small |= pieces.Min(s => s.Count) < hull.Count / 4;
        }
        Assert.That(layouts.Count, Is.GreaterThan(2));
        Assert.That(jagged, Is.True, "Cuts must wander rather than remaining perfect diagonal lines.");
        Assert.That(small, Is.True, "Fractures must allow smaller end/side sections, not always halves.");
    }

    [TestCase(2)]
    [TestCase(4)]
    public void FracturesPreserveSubstantialSections(int parts)
    {
        foreach (var shape in new[] { 0, 1, 2 })
        {
            var tiles = new HashSet<Vector2i>();
            for (var x = 0; x < 30; x++)
            for (var y = 0; y < 20; y++)
            {
                // Rectangle, H hull, and U hull: cuts must honour gaps and narrow connections.
                if (shape == 1 && x > 7 && x < 22 && (y < 7 || y > 12)) continue;
                if (shape == 2 && x > 7 && x < 22 && y > 6) continue;
                tiles.Add(new Vector2i(x, y));
            }
            var cut = WFCrashFractures.Plan(tiles, parts);
            Assert.That(cut.Count, Is.InRange(1, tiles.Count / 8));
            tiles.ExceptWith(cut);
            var sections = WFCrashFractures.Sections(tiles);
            Assert.That(sections.Count, Is.InRange(2, parts));
            Assert.That(sections.Min(s => s.Count), Is.GreaterThanOrEqualTo(6));
        }
    }

    [TestCase("/SharedMaps/_Mono/Shuttles/vaquita.yml")]
    [TestCase("/SharedMaps/_Mono/Shuttles/BlackMarket/hazel.yml")]
    [TestCase("/SharedMaps/_Mono/Shuttles/Expedition/arkansaw.yml")]
    public async Task RealHullLayoutsHaveBoundedFractures(string path)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(map.MapId, new ResPath(path), out var hull), Is.True);
            var tiles = new HashSet<Vector2i>();
            var all = server.System<SharedMapSystem>().GetAllTilesEnumerator(hull!.Value.Owner, hull.Value.Comp);
            while (all.MoveNext(out var tile)) tiles.Add(tile.Value.GridIndices);
            var cut = WFCrashFractures.Plan(tiles, 4);
            Assert.That(cut.Count, Is.InRange(1, tiles.Count / 8));
            tiles.ExceptWith(cut);
            Assert.That(WFCrashFractures.Sections(tiles).Count, Is.InRange(2, 4));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(0)]
    [TestCase(35)]
    public async Task BreakupCreatesMovingSectionsAndHurtsCrewOnceWithSeatProtection(int rotation)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-32, -32), new Vector2i(64, 64));
        var hull = await BuildCracker(pair, em.GetComponent<MapComponent>(ground).MapId);
        await MapInitHull(pair, hull);
        var originalTiles = 0;
        await server.WaitAssertion(() =>
        {
            server.System<SharedTransformSystem>().SetWorldRotation(hull, Angle.FromDegrees(rotation));
            var grid = em.GetComponent<MapGridComponent>(hull);
            var maps = server.System<SharedMapSystem>();
            var tiles = maps.GetAllTilesEnumerator(hull, grid);
            while (tiles.MoveNext(out _)) originalTiles++;
            var standing = em.SpawnEntity("MobHuman", new EntityCoordinates(hull, new Vector2(2.5f, 2.5f)));
            var seated = em.SpawnEntity("MobHuman", new EntityCoordinates(hull, new Vector2(3.5f, 2.5f)));
            var chair = em.SpawnEntity("Chair", new EntityCoordinates(hull, new Vector2(3.5f, 2.5f)));
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(seated, seated, chair), Is.True);
            server.System<SharedPhysicsSystem>().SetLinearVelocity(hull, new Vector2(3f, 0));
            var flight = server.System<WFFlightSystem>();
            flight.StructuralCrash((hull, grid), 1f);
            var effects = em.EntityQueryEnumerator<MetaDataComponent>();
            var bursts = 0;
            while (effects.MoveNext(out var effect, out var meta))
                if (meta.EntityPrototype?.ID == "WFCrashBurst") bursts++;
            Assert.That(bursts, Is.InRange(3, 8), "Visible bursts must cover fractures and interiors without an effect flood.");
            var standingDamage = em.GetComponent<DamageableComponent>(standing).TotalDamage;
            var seatedDamage = em.GetComponent<DamageableComponent>(seated).TotalDamage;
            Assert.That(standingDamage.Float(), Is.GreaterThan(0f));
            Assert.That(seatedDamage.Float(), Is.InRange(1f, standingDamage.Float() - 1f));
            Assert.That(em.HasComponent<KnockedDownComponent>(standing), Is.True);
            flight.StructuralCrash((hull, grid), 1f);
            Assert.That(em.GetComponent<DamageableComponent>(standing).TotalDamage, Is.EqualTo(standingDamage));
        });
        await server.WaitRunTicks(12);
        await server.WaitAssertion(() =>
        {
            var sections = 0;
            var velocities = new List<Vector2>();
            var spins = new List<float>();
            var remaining = 0;
            var latticeTiles = 0;
            var ownedTiles = new HashSet<Vector2i>();
            var latticeType = server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId;
            var query = em.EntityQueryEnumerator<MapGridComponent, WFSkidComponent, PhysicsComponent>();
            while (query.MoveNext(out var uid, out var grid, out var skid, out var body))
            {
                if (em.GetComponent<TransformComponent>(uid).MapUid != ground) continue;
                sections++;
                velocities.Add(body.LinearVelocity);
                spins.Add(body.AngularVelocity);
                Assert.That(body.FixedRotation, Is.False);
                Assert.That(float.IsFinite(body.AngularVelocity), Is.True);
                Assert.That(MathF.Abs(body.AngularVelocity), Is.LessThanOrEqualTo(0.65f));
                Assert.That(skid.ScarredTiles.Count, Is.GreaterThan(0), "Every section must leave a terrain trail.");
                Assert.That(float.IsFinite(body.LinearVelocity.LengthSquared()), Is.True);
                Assert.That(skid.Loop, Is.Not.Null, "Every moving section needs its own grind loop.");
                var grind = em.GetComponent<AudioComponent>(skid.Loop!.Value);
                Assert.That(grind.FileName, Is.EqualTo("/Audio/_WF/PlanetCracker/Flight/ground_grind_loop.ogg"));
                Assert.That(grind.Params.MaxDistance, Is.GreaterThanOrEqualTo(80f));
                var tiles = server.System<SharedMapSystem>().GetAllTilesEnumerator(uid, grid);
                while (tiles.MoveNext(out var tile))
                {
                    remaining++;
                    Assert.That(ownedTiles.Add(tile.Value.GridIndices), Is.True, "Each original seam cell must belong to exactly one section.");
                    if (tile.Value.Tile.TypeId == latticeType) latticeTiles++;
                }
            }
            Assert.That(sections, Is.InRange(2, 4));
            Assert.That(spins.Any(s => s > 0.01f) && spins.Any(s => s < -0.01f), Is.True,
                "Detached sections should retain different spin directions after ground friction.");
            Assert.That(velocities.Any(v => Vector2.Distance(v, velocities[0]) > 0.1f), Is.True, "Sections need different velocities to separate.");
            Assert.That(latticeTiles, Is.GreaterThan(0), "The tear must retain exposed lattice on a detached section.");
            Assert.That(remaining, Is.EqualTo(originalTiles), "Seam tiles should remain as lattice, not vanish between sections.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
