#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._Mono.Cleanup;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.PlanetCracker.Chunk;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server._WF.Planets;
using Content.Server.Atmos.Components;
using Content.Server.Parallax;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.Planets;
using Content.Server.Decals;
using Content.Shared.Decals;
using Content.Shared.Gravity;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using PhysTransform = Robust.Shared.Physics.Transform;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>Disc extraction: what rides up, the pinned hole and rim, the parked chunk and the cracked flag.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetChunkSystem))]
public sealed class CrackExtractionTest
{
    /// <summary>A loose container.</summary>
    private const string Crate = "WFAnchorCrate";

    /// <summary>A walking mob, the case the disc filter exists for.</summary>
    private const string Mob = "MobHuman";

    /// <summary>A second tile type, so the struct copy has a TypeId change to carry as well as a variant.</summary>
    private const string SampleTile = "Plating";

    /// <summary>Exactly the tiles whose centre is inside the circle are copied.</summary>
    [Test]
    public async Task ExtractionCopiesEveryDiscTile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            var disc = DiscIndices(entMan, site.Ground, cut.Centre, cut.Radius);
            var rim = RimIndices(entMan, site.Ground, cut.Centre, cut.Radius);
            var chunkGrid = entMan.GetComponent<MapGridComponent>(cut.Chunk);
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(cut.Chunk);

            var copied = maps.GetAllTiles(cut.Chunk, chunkGrid).Count();
            var missing = disc.Count(index => maps.GetTileRef(cut.Chunk, chunkGrid, index).Tile.IsEmpty);
            var overshoot = rim.Count(index => !maps.GetTileRef(cut.Chunk, chunkGrid, index).Tile.IsEmpty);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(disc, Is.Not.Empty, "Precondition: the cut circle covers at least one tile.");
                Assert.That(missing, Is.Zero, $"{missing} of {disc.Count} disc tiles never reached the chunk grid.");
                Assert.That(overshoot, Is.Zero, $"{overshoot} rim tiles were taken as well; the radius test is not a tile-centre test.");
                Assert.That(copied, Is.EqualTo(disc.Count), "The chunk carries a different number of tiles than the circle contains.");
                Assert.That(comp.TileCount, Is.EqualTo(disc.Count), "The chunk component records a tile count the grid does not have.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The whole Tile struct is copied, keeping variant and rotation, not just its type.</summary>
    [Test]
    public async Task ExtractionCopiesTheWholeTileStruct()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var crackers = server.System<WFCrackerSystem>();

        var site = await BuildReadyToExtract(pair);
        var samples = new List<(Vector2i Index, Tile Tile)>();

        // Stamped after the pair is down, so the circle is known, and before the cut.
        await server.WaitPost(() =>
        {
            var pair2 = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            Assert.That(crackers.TryGetOwnedPair((site.Cracker, pair2), out var a, out var b, false), Is.True,
                "Precondition: the hull owns a pair to cut with.");
            Assert.That(crackers.TryGetCircle(a.Owner, b.Owner, out var centre, out var radius), Is.True,
                "Precondition: the pair has a cut circle.");

            var grid = entMan.GetComponent<MapGridComponent>(site.Ground);
            var typeId = tileDefs[SampleTile].TileId;
            var disc = DiscIndices(entMan, site.Ground, centre, radius);

            // Spread across the disc so a partial copy cannot pass by luck.
            for (var i = 0; i < disc.Count; i += Math.Max(1, disc.Count / 8))
            {
                samples.Add((disc[i], new Tile(typeId, 0, (byte)(3 + i % 5), (byte)(1 + i % 7))));
            }

            maps.SetTiles(site.Ground, grid, samples);
        });

        await server.WaitRunTicks(1);

        await BeginCut(pair, site);
        await CompleteCut(pair, site);

        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            var chunkGrid = entMan.GetComponent<MapGridComponent>(cut.Chunk);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(samples, Is.Not.Empty, "Precondition: at least one tile was stamped to compare against.");

                foreach (var (index, expected) in samples)
                {
                    var actual = maps.GetTileRef(cut.Chunk, chunkGrid, index).Tile;

                    Assert.That(actual.TypeId, Is.EqualTo(expected.TypeId), $"Tile {index} changed type on the way up.");
                    Assert.That(actual.Flags, Is.EqualTo(expected.Flags), $"Tile {index} lost its flags on the way up.");
                    Assert.That(actual.Variant, Is.EqualTo(expected.Variant), $"Tile {index} lost its variant on the way up.");
                    Assert.That(actual.RotationMirroring, Is.EqualTo(expected.RotationMirroring),
                        $"Tile {index} lost its rotation and mirroring on the way up.");
                }
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Both anchors ride up still paired and locked, so the finished cut is not aborted.</summary>
    [Test]
    public async Task BothAnchorsRideUpStillPairedAndLocked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            var a = entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[0]);
            var b = entMan.GetComponent<WFGravityAnchorComponent>(site.Anchors[1]);

            using (Assert.EnterMultipleScope())
            {
                foreach (var anchor in site.Anchors)
                {
                    var xform = entMan.GetComponent<TransformComponent>(anchor);

                    Assert.That(xform.GridUid, Is.EqualTo(cut.Chunk), "An anchor was left behind on the ground map.");
                    Assert.That(xform.Anchored, Is.True, "An anchor rode up loose instead of anchored to the chunk.");
                }

                Assert.That(a.Partner, Is.EqualTo(entMan.GetNetEntity(site.Anchors[1])), "The pair dissolved on the ride up.");
                Assert.That(b.Partner, Is.EqualTo(entMan.GetNetEntity(site.Anchors[0])), "The pair dissolved on the ride up.");
                Assert.That(a.State, Is.EqualTo(WFAnchorState.Locked), "An anchor was demoted out of Locked by the ride.");
                Assert.That(b.State, Is.EqualTo(WFAnchorState.Locked), "An anchor was demoted out of Locked by the ride.");
                Assert.That(a.Damaged, Is.False, "The ride damaged an anchor.");
                Assert.That(b.Damaged, Is.False, "The ride damaged an anchor.");
            }
        });

        // StartAbort fires on the first sweep that sees no pair, so five seconds is enough.
        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFCrackState.Cracked), "The hull fell out of Cracked after its extraction.");
                Assert.That(comp.PendingAbort, Is.Null, "The hull started an abort after a successful extraction.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Anything standing on a copied tile rides up with it, and anything standing off one does not.</summary>
    [Test]
    public async Task LooseEntitiesAndAMobRideUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildReadyToExtract(pair);

        var insideCrate = EntityUid.Invalid;
        var insideMob = EntityUid.Invalid;
        var outsideCrate = EntityUid.Invalid;
        var outsideMob = EntityUid.Invalid;

        // The circle is radius 10 about (8.5, 0.5): two inside, two outside.
        await server.WaitPost(() =>
        {
            insideCrate = entMan.SpawnEntity(Crate, new EntityCoordinates(site.Ground, new Vector2(8.5f, 2.5f)));
            insideMob = entMan.SpawnEntity(Mob, new EntityCoordinates(site.Ground, new Vector2(6.5f, 3.5f)));
            outsideCrate = entMan.SpawnEntity(Crate, new EntityCoordinates(site.Ground, new Vector2(8.5f, 14.5f)));
            outsideMob = entMan.SpawnEntity(Mob, new EntityCoordinates(site.Ground, new Vector2(-6.5f, 0.5f)));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await BeginCut(pair, site);
        await CompleteCut(pair, site);

        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(insideCrate).ParentUid, Is.EqualTo(cut.Chunk),
                    "A crate standing inside the circle was left on the ground map.");
                Assert.That(entMan.GetComponent<TransformComponent>(insideMob).ParentUid, Is.EqualTo(cut.Chunk),
                    "A mob standing inside the circle was left on the ground map.");
                Assert.That(entMan.GetComponent<TransformComponent>(outsideCrate).ParentUid, Is.EqualTo(site.Ground),
                    "A crate standing outside the circle was dragged up with the disc.");
                Assert.That(entMan.GetComponent<TransformComponent>(outsideMob).ParentUid, Is.EqualTo(site.Ground),
                    "A mob standing outside the circle was dragged up with the disc.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>A mob overlapping the circle but standing on an uncopied tile stays behind.</summary>
    [Test]
    public async Task ARimStraddlingMobIsLeftBehind()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var lookup = server.System<EntityLookupSystem>();

        var site = await BuildReadyToExtract(pair);

        // 10.1 tiles out on tile (16, 7), whose centre is 10.63 out: inside the query shape, outside the disc.
        var position = new Vector2(16.1325f, 7.1148f);
        var straddler = EntityUid.Invalid;

        await server.WaitPost(() => straddler = entMan.SpawnEntity(Mob, new EntityCoordinates(site.Ground, position)));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        var index = Vector2i.Zero;

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(site.Ground);
            index = maps.TileIndicesFor(site.Ground, grid, entMan.GetComponent<TransformComponent>(straddler).Coordinates);
        });

        await BeginCut(pair, site);
        await CompleteCut(pair, site);

        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            var disc = DiscIndices(entMan, site.Ground, cut.Centre, cut.Radius);
            var chunkGrid = entMan.GetComponent<MapGridComponent>(cut.Chunk);

            // The extraction's query shape does find it.
            var overlapping = new HashSet<EntityUid>();
            lookup.GetLocalEntitiesIntersecting(
                site.Ground,
                new PhysShapeCircle(cut.Radius, cut.Centre),
                PhysTransform.Empty,
                overlapping,
                LookupFlags.Uncontained);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(disc, Does.Not.Contain(index), "Precondition: the straddler's own tile is outside the disc.");
                Assert.That(overlapping, Does.Contain(straddler),
                    "Precondition: the straddler's fixture overlaps the cut circle, so the prefilter would take it.");
                Assert.That(entMan.GetComponent<TransformComponent>(straddler).ParentUid, Is.EqualTo(site.Ground),
                    "A rim-straddling mob rode up on a tile that was never copied.");
                Assert.That(maps.GetTileRef(cut.Chunk, chunkGrid, index).Tile.IsEmpty, Is.True,
                    "The straddler's tile was copied onto the chunk after all.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The hole is empty and pinned in the biome, which is the only record that it is deliberate.</summary>
    [Test]
    public async Task TheHoleIsEmptyAndPinned()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            var disc = DiscIndices(entMan, site.Ground, cut.Centre, cut.Radius);
            var filled = HoleTiles(entMan, site.Ground, cut.Centre, cut.Radius).Count(entry => !entry.Tile.IsEmpty);
            var pinned = PinnedCount(pair, site.Ground, disc);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(filled, Is.Zero, $"{filled} of {disc.Count} disc tiles are still solid ground.");
                Assert.That(pinned, Is.EqualTo(disc.Count), $"Only {pinned} of {disc.Count} disc tiles are pinned against the biome.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The pinned hole and rim decals survive a biome unload and reload.</summary>
    [Test]
    public async Task TheHoleAndRimSurviveAnUnloadAndReload()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        // One callback, so the biome's own loader cannot run in between.
        await server.WaitPost(() =>
        {
            var biome = new Entity<BiomeComponent, MapGridComponent>(
                site.Ground,
                entMan.GetComponent<BiomeComponent>(site.Ground),
                entMan.GetComponent<MapGridComponent>(site.Ground));

            foreach (var origin in BiomeChunkOrigins(entMan, site.Ground, cut.Centre, cut.Radius))
            {
                biomes.WfLoadChunk(biome, origin);
                biomes.WfUnloadChunk(biome, origin);
                biomes.WfLoadChunk(biome, origin);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var disc = DiscIndices(entMan, site.Ground, cut.Centre, cut.Radius);
            var refilled = HoleTiles(entMan, site.Ground, cut.Centre, cut.Radius).Count(entry => !entry.Tile.IsEmpty);
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(cut.Chunk);
            var decals = RimDecals(pair, site.Ground, cut.Centre, cut.Radius);
            var lost = comp.RimDecals.Count(id => !decals.ContainsKey(id));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(refilled, Is.Zero, $"The biome regenerated {refilled} of {disc.Count} hole tiles.");
                Assert.That(comp.RimDecals, Is.Not.Empty, "Precondition: the rim was decalled at all.");
                Assert.That(lost, Is.Zero, $"{lost} of {comp.RimDecals.Count} rim decals did not survive the unload.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The rim decals sit on pinned ground outside the circle; decals cannot live on space.</summary>
    [Test]
    public async Task TheRimIsDecalled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            var rim = RimIndices(entMan, site.Ground, cut.Centre, cut.Radius);
            var grid = entMan.GetComponent<MapGridComponent>(site.Ground);
            var comp = entMan.GetComponent<WFPlanetChunkComponent>(cut.Chunk);
            var decals = RimDecals(pair, site.Ground, cut.Centre, cut.Radius);

            var empty = rim.Count(index => maps.GetTileRef(site.Ground, grid, index).Tile.IsEmpty);
            var pinned = PinnedCount(pair, site.Ground, rim);
            var stamped = new List<Vector2i>();

            foreach (var id in comp.RimDecals)
            {
                Assert.That(decals.TryGetValue(id, out var decal), Is.True,
                    $"Rim decal {id} is recorded on the chunk but is not on the ground grid.");

                stamped.Add(new Vector2i((int)MathF.Floor(decal!.Coordinates.X), (int)MathF.Floor(decal.Coordinates.Y)));
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(rim, Is.Not.Empty, "Precondition: the ring outside the circle covers at least one tile.");
                Assert.That(empty, Is.Zero, $"{empty} rim tiles are empty, so their decals could never be stamped.");
                Assert.That(pinned, Is.EqualTo(rim.Count), $"Only {pinned} of {rim.Count} rim tiles are pinned; the ring would unload.");
                Assert.That(comp.RimDecals, Has.Count.EqualTo(rim.Count), "The rim carries a different number of decals than it has tiles.");
                Assert.That(stamped, Is.EquivalentTo(rim), "The rim decals are not stamped one per rim tile.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>A berth marked too close to the deck still hangs the disc clear of the hull.</summary>
    [Test]
    public async Task TheChunkClearsTheHullWhenTheBerthIsTooClose()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var lookup = server.System<EntityLookupSystem>();

        var site = await BuildCrackerInOrbit(pair);
        await EnlargeBerth(pair, site, new Vector2i(12, 12), 8f);
        await DeployPair(pair, site, 0f, 16f, true);
        await AlignHull(pair, site, Vector2.Zero);
        await Energise(pair, site.Cracker);
        await BeginCut(pair, site);

        // The hull footprint before the gangway, which overlaps the disc by design.
        var hull = Box2.Empty;
        await server.WaitPost(() => hull = lookup.GetWorldAABB(site.Cracker));

        await CompleteCut(pair, site);

        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            var chunk = lookup.GetWorldAABB(cut.Chunk);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(cut.Chunk).MapUid, Is.EqualTo(site.Orbit),
                    "The chunk is not hanging on the orbit layer.");
                Assert.That(hull.Intersects(chunk), Is.False,
                    $"The disc {chunk} was hung through the hull {hull}.");

                // The gangway: lattice from the marker's tile out over the gap and one tile into the disc.
                var comp = entMan.GetComponent<WFPlanetChunkComponent>(cut.Chunk);
                Assert.That(comp.GangwayTiles.Count, Is.GreaterThanOrEqualTo(3),
                    "No gangway was laid from the hull to the chunk.");
                Assert.That(entMan.GetNetEntity(site.Cracker), Is.EqualTo(comp.GangwayHull),
                    "The gangway is recorded on the wrong hull.");

                var hullGrid = entMan.GetComponent<MapGridComponent>(site.Cracker);
                var maps = server.System<SharedMapSystem>();
                var lattice = server.ResolveDependency<ITileDefinitionManager>()[WFPlanetChunkSystem.GangwayTile].TileId;

                foreach (var index in comp.GangwayTiles)
                {
                    Assert.That(maps.TryGetTileRef(site.Cracker, hullGrid, index, out var tileRef) && tileRef.Tile.TypeId == lattice,
                        Is.True, $"Gangway tile {index} is not lattice on the hull.");
                }

                var far = maps.GridTileToWorld(site.Cracker, hullGrid, comp.GangwayTiles[^1]).Position;
                Assert.That(chunk.Contains(far), Is.True, "The gangway's last tile does not reach the disc.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The chunk keeps the ground's rotation and parks as a static, airless, self-lit body.</summary>
    [Test]
    public async Task TheChunkHangsInTheBerth()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));

            Assert.That(crackers.TryGetBerthCentre(cracker, out var berth), Is.True,
                "Precondition: the hull still resolves its berth centre.");

            var expected = transform.GetWorldPosition(site.Ground) + (berth.Position - cut.Centre);
            var actual = transform.GetWorldPosition(cut.Chunk);
            var body = entMan.GetComponent<PhysicsComponent>(cut.Chunk);
            var gravity = entMan.GetComponent<GravityComponent>(cut.Chunk);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(cut.Chunk).MapUid, Is.EqualTo(site.Orbit),
                    "The chunk is not hanging on the orbit layer.");
                Assert.That((actual - expected).Length(), Is.LessThan(0.01f),
                    $"The chunk is at {actual} rather than {expected}; the hole and the disc no longer line up.");
                Assert.That(body.BodyType, Is.EqualTo(BodyType.Static), "The chunk is not a static body and will drift out of the berth.");
                Assert.That(entMan.HasComponent<PreventGridAnchorChangesComponent>(cut.Chunk), Is.True,
                    "The chunk can have its anchoring changed out from under it.");
                Assert.That(entMan.HasComponent<ForceAnchorComponent>(cut.Chunk), Is.True,
                    "The chunk carries no force-anchor marker for the impact protection to read.");
                Assert.That(entMan.HasComponent<CleanupImmuneComponent>(cut.Chunk), Is.True,
                    "The chunk is not immune to the grid cleanup sweep.");
                Assert.That(gravity.Enabled, Is.True, "The chunk has no gravity, so nothing can stand on it.");
                Assert.That(gravity.Inherent, Is.True, "The chunk's gravity is not inherent and the refresh sweep will wipe it.");
                Assert.That(entMan.HasComponent<GridAtmosphereComponent>(cut.Chunk), Is.False,
                    "The chunk grew a grid atmosphere; design D3 says a cut disc has none.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The berthed chunk is detached terrain to the Planets module, so orbit decay never stamps it.</summary>
    [Test]
    public async Task TheChunkIsDetachedTerrainAndNeverDecays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        // Past orbit decay's settle delay and its two adrift sweeps.
        await server.WaitRunTicks(pair.SecondsToTicks(13f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFDetachedTerrainComponent>(cut.Chunk), Is.True,
                    "The chunk is not marked as detached terrain, so planet flight and decay treat it as a hull.");
                Assert.That(entMan.HasComponent<WFOrbitDecayComponent>(cut.Chunk), Is.False,
                    "The chunk in the berth was given an orbit decay countdown.");
                Assert.That(entMan.GetComponent<TransformComponent>(cut.Chunk).MapUid, Is.EqualTo(site.Orbit),
                    "The chunk left the orbit layer while berthed.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Extraction draws the hole on the orbit radar through the orbit layer's scar list.</summary>
    [Test]
    public async Task ExtractionRecordsARadarScar()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            var scars = entMan.GetComponent<WFOrbitLayerComponent>(site.Orbit).RadarScars;

            Assert.That(scars, Has.Count.EqualTo(1), "The extraction did not record exactly one radar scar.");
            Assert.That((scars[0] - new Vector3(cut.Centre, cut.Radius)).Length(), Is.LessThan(0.01f),
                $"The radar scar {scars[0]} is not the cut circle {cut.Centre} r{cut.Radius}.");
        });

        await Cleanup(pair, site);
    }

    /// <summary>The chunk was built on the ground map and re-parented, so it has its z-physics components.</summary>
    [Test]
    public async Task TheChunkIsZPhysicsAttached()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<CEZPhysicsComponent>(cut.Chunk), Is.True,
                    "The chunk has no CE z-physics, so it has no altitude at all.");
                Assert.That(entMan.HasComponent<CEZGridFallerComponent>(cut.Chunk), Is.True,
                    "The chunk has no faller, so nothing could ever push it down the stack.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>A chunk is a grid on a layer and never carries a planet-layer marker.</summary>
    [Test]
    public async Task TheChunkNeverCarriesPlanetLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFPlanetLayerComponent>(cut.Chunk), Is.False,
                    "The chunk grid is marked as a planet layer.");
                Assert.That(entMan.HasComponent<WFOrbitLayerComponent>(cut.Chunk), Is.False,
                    "The chunk grid is marked as an orbit layer.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Extraction sets the cracked flag on the sector body, which outlives the z-network.</summary>
    [Test]
    public async Task ExtractionFlagsThePlanet()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var events = server.System<WFAnchorTestEventSystem>();

        await server.WaitPost(() => events.Clear());

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFPlanetCrackedComponent>(site.Planet), Is.True,
                    "The planet was not flagged cracked, so it can be cut again.");
                Assert.That(events.PlanetsCracked, Has.Count.EqualTo(1), "The cracked hook did not fire exactly once.");
                Assert.That(events.PlanetsCracked[0].Planet, Is.EqualTo(site.Planet), "The cracked hook named another body.");
                Assert.That(events.PlanetsCracked[0].Chunk, Is.EqualTo(cut.Chunk), "The cracked hook named another chunk.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>A cracked planet refuses a second cut, and names that first as the only permanent fault.</summary>
    [Test]
    public async Task ASecondCrackIsRefused()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var crackers = server.System<WFCrackerSystem>();

        var site = await BuildExtracted(pair);

        await server.WaitAssertion(() =>
        {
            var cracker = (site.Cracker, entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker));
            var blockers = crackers.ComputeBlockers(cracker);
            var began = crackers.TryBegin(cracker, out var reason);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(blockers.HasFlag(WFCrackBlocker.PlanetCracked), Is.True,
                    $"A cracked planet does not block a second cut ({blockers}).");
                Assert.That(WFCrackerSystem.GetBlockerReason(blockers), Is.EqualTo("wf-crack-console-blocker-planet-cracked"),
                    "The refusal names something the crew could fix instead of the permanent fault.");
                Assert.That(began, Is.False, "A second cut was allowed on an already cracked planet.");
                Assert.That(reason, Is.EqualTo("wf-crack-console-blocker-planet-cracked"), "The refusal reason is not the cracked planet.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Ring progress reaches the client on the anchors, which carry a global PVS override.</summary>
    [Test]
    public async Task RingProgressIsNetworkedZeroToOne()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var clientEntMan = client.EntMan;
        var crackers = server.System<WFCrackerSystem>();

        var site = await BuildReadyToExtract(pair);

        // Five minutes keeps the begin under the 0.01 dirty epsilon and eight seconds of cutting above it.
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFPlanetCrackerComponent>(site.Cracker);
            comp.BaseCrackTime = TimeSpan.FromSeconds(450);

            var cracker = (site.Cracker, comp);

            Assert.That(crackers.TryTarget(cracker, out var targeting), Is.True, $"Precondition: the pair targets: {targeting}");
            Assert.That(crackers.TryBegin(cracker, out var reason), Is.True, $"Precondition: the cut begins: {reason}");
        });

        await pair.RunTicksSync(10);

        var begin = await ReadClientProgress(pair, site);

        await server.WaitRunTicks(pair.SecondsToTicks(8f));
        await pair.RunTicksSync(10);

        var middle = await ReadClientProgress(pair, site);

        await CompleteCut(pair, site);
        await pair.RunTicksSync(10);

        var end = await ReadClientProgress(pair, site);

        await client.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var value in begin)
                {
                    Assert.That(value, Is.EqualTo(0f).Within(0.001f), "A fresh cut did not start the ring at nothing.");
                }

                foreach (var value in middle)
                {
                    Assert.That(value, Is.GreaterThan(0f), "The ring never started growing on the client.");
                    Assert.That(value, Is.LessThan(1f), "The ring was already complete a quarter of the way through the cut.");
                }

                foreach (var value in end)
                {
                    Assert.That(value, Is.EqualTo(1f).Within(0.001f), "A finished cut is not a full circle.");
                }
            }
        });

        Assert.That(clientEntMan.EntityExists(pair.ToClientUid(site.Anchors[0])), Is.True,
            "Precondition: the anchors reach the client at all, which is what the PVS override is for.");

        await Cleanup(pair, site);
    }

    /// <summary>Beams are reconciled every sweep; checked server-side, as projectors have no PVS override.</summary>
    [Test]
    public async Task BeamStateIsReconciledServerSide()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var transform = server.System<SharedTransformSystem>();

        var site = await BuildReadyToExtract(pair);
        await BeginCut(pair, site);

        // The sweep runs every 0.25 s while a hull is cutting.
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var targets = site.Anchors.Select(anchor => entMan.GetNetEntity(anchor)).ToList();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(site.Projectors, Is.Not.Empty, "Precondition: the hull carries projectors to fire.");

                foreach (var projector in site.Projectors)
                {
                    Assert.That(entMan.TryGetComponent(projector, out WFCrackBeamComponent? beam), Is.True,
                        "A projector is not firing a beam during the cut.");
                    Assert.That(targets, Does.Contain(beam!.Target), "A projector's beam is aimed at something that is not a targeted anchor.");
                }

                var mounts = site.Projectors.Select(mount => transform.GetWorldPosition(mount)).ToList();

                foreach (var anchor in site.Anchors)
                {
                    Assert.That(entMan.TryGetComponent(anchor, out WFCrackBeamTargetComponent? surface), Is.True,
                        "A targeted anchor carries no surface beam target during the cut.");
                    Assert.That(
                        mounts.Any(mount => (mount - surface!.ProjectorWorldPos).Length() <= MountTolerance),
                        Is.True,
                        "A targeted anchor's surface beam names a point that is not one of the hull's mounts.");
                }
            }
        });

        await CompleteCut(pair, site);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var projector in site.Projectors)
                {
                    Assert.That(entMan.HasComponent<WFCrackBeamComponent>(projector), Is.False,
                        "A projector is still firing after the cut finished.");
                }

                foreach (var anchor in site.Anchors)
                {
                    Assert.That(entMan.HasComponent<WFCrackBeamTargetComponent>(anchor), Is.False,
                        "A surface beam target outlived the cut that stamped it.");
                }
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Projectors face their anchor for the cut and return to their mapped facing afterwards.</summary>
    [Test]
    public async Task ProjectorsFaceTheirAnchorForTheCutAndGoBackAfterwards()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        var site = await BuildReadyToExtract(pair);

        var placed = new List<Angle>();

        await server.WaitAssertion(() =>
        {
            Assert.That(site.Projectors, Is.Not.Empty, "Precondition: the hull carries projectors to swing.");

            foreach (var projector in site.Projectors)
            {
                placed.Add(entMan.GetComponent<TransformComponent>(projector).LocalRotation);
            }
        });

        await BeginCut(pair, site);

        // The sweep runs every 0.25 s while cutting.
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var projector in site.Projectors)
                {
                    Assert.That(entMan.TryGetComponent(projector, out WFCrackBeamComponent? beam), Is.True,
                        "Precondition: a projector is firing a beam, which is what it should be facing along.");
                    Assert.That(entMan.TryGetEntity(beam!.Target, out var target), Is.True,
                        "Precondition: the beam's target anchor resolves on the server.");

                    var delta = transform.GetWorldPosition(target!.Value) - transform.GetWorldPosition(projector);
                    var expected = Angle.FromWorldVec(delta);
                    var actual = transform.GetWorldRotation(projector);

                    Assert.That(actual.EqualsApprox(expected, FacingTolerance), Is.True,
                        $"A firing projector faces {actual.Degrees:0.0} degrees, not the {expected.Degrees:0.0} its anchor is at.");
                }
            }
        });

        await CompleteCut(pair, site);
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                for (var i = 0; i < site.Projectors.Count; i++)
                {
                    var actual = entMan.GetComponent<TransformComponent>(site.Projectors[i]).LocalRotation;

                    Assert.That(actual.EqualsApprox(placed[i], FacingTolerance), Is.True,
                        $"A projector kept facing {actual.Degrees:0.0} degrees after the cut instead of the {placed[i].Degrees:0.0} it was placed at.");
                    Assert.That(entMan.GetComponent<WFGravityProjectorComponent>(site.Projectors[i]).PlacedRotation,
                        Is.Null, "A projector is still holding a remembered facing after the cut ended.");
                }
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The extraction hook fires once per disc, and a repeated completion cuts no second disc.</summary>
    [Test]
    public async Task ExtractionRaisesItsHookOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var events = server.System<WFAnchorTestEventSystem>();

        await server.WaitPost(() => events.Clear());

        var site = await BuildExtracted(pair);
        var cut = await ReadCut(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(events.ChunksExtracted, Has.Count.EqualTo(1), "The extraction hook did not fire exactly once.");
                Assert.That(events.ChunksExtracted[0].Chunk, Is.EqualTo(cut.Chunk), "The extraction hook named another chunk.");
                Assert.That(events.ChunksExtracted[0].Cracker, Is.EqualTo(site.Cracker), "The extraction hook named another hull.");
            }
        });

        // Same entry point and pair again; the back-link must refuse it.
        await CompleteCut(pair, site);

        await server.WaitAssertion(() =>
        {
            var chunks = 0;
            var query = entMan.AllEntityQueryEnumerator<WFPlanetChunkComponent>();

            while (query.MoveNext(out _, out _))
            {
                chunks++;
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks, Is.EqualTo(1), "A second completion cut a second disc out of the same hull.");
                Assert.That(events.ChunksExtracted, Has.Count.EqualTo(1), "A second completion raised the extraction hook again.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Both targeted anchors' ring progress, as the CLIENT half of the pair has them.</summary>
    private static async Task<List<float>> ReadClientProgress(TestPair pair, CrackerSite site)
    {
        var client = pair.Client;
        var clientEntMan = client.EntMan;
        var values = new List<float>();

        await client.WaitAssertion(() =>
        {
            foreach (var anchor in site.Anchors)
            {
                var uid = pair.ToClientUid(anchor);

                Assert.That(clientEntMan.TryGetComponent(uid, out WFGravityAnchorComponent? comp), Is.True,
                    "An anchor never reached the client, so its ring value cannot be read.");

                values.Add(comp!.CrackProgress);
            }
        });

        return values;
    }

    /// <summary>Every decal near the cut by id, via the decal system since DecalGridComponent is locked.</summary>
    private static Dictionary<uint, Decal> RimDecals(TestPair pair, EntityUid ground, Vector2 centre, float radius)
    {
        var decals = pair.Server.System<DecalSystem>();
        var span = (radius + 4f) * 2f;
        var found = new Dictionary<uint, Decal>();

        foreach (var (id, decal) in decals.GetDecalsIntersecting(ground, Box2.CenteredAround(centre, new Vector2(span, span))))
        {
            found[id] = decal;
        }

        return found;
    }

    /// <summary>How far off a mount the anchor's copy of its position may sit, in tiles.</summary>
    private const float MountTolerance = 0.5f;

    /// <summary>How far off a mount's facing may be, in radians.</summary>
    private const double FacingTolerance = 0.01;

    /// <summary>Every 8-tile biome chunk origin the disc and its rim touch.</summary>
    private static HashSet<Vector2i> BiomeChunkOrigins(IEntityManager entMan, EntityUid ground, Vector2 centre, float radius)
    {
        var origins = new HashSet<Vector2i>();

        foreach (var index in DiscIndices(entMan, ground, centre, radius).Concat(RimIndices(entMan, ground, centre, radius)))
        {
            origins.Add(SharedMapSystem.GetChunkIndices(index, BiomeChunkSize) * BiomeChunkSize);
        }

        return origins;
    }

    /// <summary>SharedBiomeSystem.ChunkSize, which is protected and therefore mirrored here.</summary>
    private const byte BiomeChunkSize = 8;

    /// <summary>The one cut disc and the circle it came out of, read off the chunk itself.</summary>
    private sealed class Cut
    {
        /// <summary>The chunk grid.</summary>
        public EntityUid Chunk;

        /// <summary>Raw world XY of the cut circle's centre on the ground layer.</summary>
        public Vector2 Centre;

        /// <summary>The circle's radius in tiles.</summary>
        public float Radius;
    }

    /// <summary>Finds the one chunk and reads its geometry back, failing loudly when no disc was cut at all.</summary>
    private static async Task<Cut> ReadCut(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var cut = new Cut();

        await server.WaitAssertion(() =>
        {
            cut.Chunk = FindChunk(entMan);

            Assert.That(cut.Chunk, Is.Not.EqualTo(EntityUid.Invalid), "The completed cut produced no chunk at all.");

            var comp = entMan.GetComponent<WFPlanetChunkComponent>(cut.Chunk);
            cut.Centre = comp.HoleCentre;
            cut.Radius = comp.Radius;
        });

        return cut;
    }

    /// <summary>The chunk goes before the stack it is hanging over.</summary>
    private static async Task Cleanup(TestPair pair, CrackerSite site)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var chunk = FindChunk(entMan);

            if (chunk != EntityUid.Invalid)
                entMan.DeleteEntity(chunk);
        });

        await server.WaitRunTicks(1);

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }
}
