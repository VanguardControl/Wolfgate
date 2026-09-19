#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Server._WF.PlanetCracker.Survey;
using Content.Server.Parallax;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Markers;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;
using PhysTransform = Robust.Shared.Physics.Transform;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// Deep veins end to end: that an anchored, bodyless vein is still found by the query the extraction lifts a chunk
/// with, that the marker layer places them deterministically and actually generates under a real viewer, that the tile
/// whitelist the marker layer cannot express holds, that a second pass does not double-spawn, that contents are rolled
/// from data and not from a per-process hash, and that the rating maths buckets the way the bands document says.
/// Generation is always proven on the grass-only test surface declared below, never on Asclepiu: Asclepiu's biome
/// stacks MonoOcean and Snow over Grasslands (Resources/Prototypes/_WF/PlanetCracker/biomes.yml:15-32), so on a live
/// seed the AllowedTiles self-delete can legitimately empty the result and the test would fail with no bug present.
/// Hand-laying tiles is not a fix for that: LoadChunkMarkers skips any node already in the chunk's ModifiedTiles set
/// (BiomeSystem.MarkerProcessor.cs:279-280) and re-sets the tile from TryGetBiomeTile before spawning (:285-289), so
/// pinned or hand-laid ground suppresses the spawn instead of steering it.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDeepVeinSystem))]
public sealed class DeepVeinTest
{
    /// <summary>
    /// A grass-only world with its own marker layer, so vein generation is a fact about the code rather than about
    /// which way Asclepiu's ocean noise fell this run. SurveyorTest builds on the same surface for the same reason.
    /// planetType is PlanetTypeBarren, which SystemKyphrus does not contain, so this surface never registers itself
    /// onto a sector body and never collides with WFSurfaceAsclepiu in WFPlanetRegistrySystem's per-type map.
    /// </summary>
    [TestPrototypes]
    public const string Prototypes = @"
- type: biomeTemplate
  id: WFTestGrassBiome
  layers:
  - !type:BiomeTileLayer
    threshold: -1.0
    tile: FloorPlanetGrass

- type: planet
  id: WFTestGrassPlanet
  biome: WFTestGrassBiome
  mapName: wf-planet-asclepiu-surface
  mapLight: ""#c8fdff""
  atmosphere:
    volume: 2500
    immutable: True
    temperature: 293.15
    moles:
    - 21.824879
    - 82.10312

- type: wfPlanetSurface
  id: WFTestVeinSurface
  planetType: PlanetTypeBarren
  ground: WFTestGrassPlanet
  airLayers: 1
  cloudLayer: false
  buildAtRoundStart: false
  sanctioned: true
  veins: WFVeinTableAsclepiu
  seed: 424242

- type: biomeMarkerLayer
  id: WFTestDeepVeins
  prototype: WFDeepVein
  size: 32
  radius: 4
  maxCount: 64
  minGroupSize: 1
  maxGroupSize: 1
";

    /// <summary>The grass-only surface every generation test builds on. Plain string: the linter skips test prototypes.</summary>
    public const string TestSurface = "WFTestVeinSurface";

    /// <summary>The tighter marker layer the forcing helper drives.</summary>
    private const string TestMarkerLayer = "WFTestDeepVeins";

    /// <summary>The vein entity itself.</summary>
    public const string VeinProto = "WFDeepVein";

    /// <summary>A tile the vein's default whitelist admits.</summary>
    public const string GrassTile = "FloorPlanetGrass";

    /// <summary>A tile the whitelist does not admit; Asclepiu's Snow template fills with it.</summary>
    private const string SnowTile = "FloorSnow";

    /// <summary>The vein's own RSI, as the prototype names it.</summary>
    private const string VeinRsi = "_WF/PlanetCracker/Decals/deep_vein.rsi";

    /// <summary>BiomeSystem's load area, which bounds where a marker pass can ever put an entity.</summary>
    private const int LoadArea = 16;

    /// <summary>
    /// RUN THIS FIRST: if it fails, every vein is silently left behind the day a chunk lifts.
    /// A vein carries no PhysicsComponent, so the broadphase inserts it with
    /// <c>staticBody: body?.BodyType == BodyType.Static</c> (EntityLookupSystem.cs:648-657) and then picks
    /// <c>staticBody ? StaticSundriesTree : SundriesTree</c> (:491-496) - a null body is not Static, so the vein rides
    /// the DYNAMIC SundriesTree no matter how it is anchored. LookupFlags.Uncontained covers it through its Sundries
    /// bit; narrowing the flag to Static alone would return zero veins with no error anywhere.
    /// </summary>
    [Test]
    public async Task AnchoredVeinIsFoundByTheExtractionQuery()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var layers = await BuildTestGround(pair);
        var ground = layers[0];
        var centre = new Vector2(5.5f, 0.5f);

        await LayTile(pair, ground, new Vector2i(5, 0), GrassTile);

        var vein = await SpawnVein(pair, ground, centre);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(vein), Is.True,
                "The vein deleted itself on ground its own whitelist admits.");
            Assert.That(entMan.GetComponent<TransformComponent>(vein).Anchored, Is.True,
                "The vein is not anchored, so the prototype lost `- type: Transform / anchored: true`.");

            var found = new HashSet<EntityUid>();

            // F5 step 5's exact query, flag and all.
            entMan.System<EntityLookupSystem>().GetLocalEntitiesIntersecting(
                ground,
                new PhysShapeCircle(2f, centre),
                PhysTransform.Empty,
                found,
                LookupFlags.Uncontained);

            Assert.That(found, Does.Contain(vein),
                "The extraction's own lookup does not find an anchored vein; the chunk would lift without it.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The per-layer placement maths is a pure function of its Random, so the same seed has to lay the same nodes.
    /// No player and no ticks: this is the engine call BuildMarkerChunks makes, driven directly.
    /// What it cannot pin is the per-layer index that goes into the marker seed, which comes from dictionary
    /// enumeration order (BiomeSystem.MarkerProcessor.cs:30-34,:49) over a HashSet.
    /// </summary>
    [Test]
    public async Task MarkerNodesAreDeterministic()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        var layers = await BuildTestGround(pair);
        var ground = layers[0];

        await server.WaitAssertion(() =>
        {
            var biomes = server.System<BiomeSystem>();
            var biome = entMan.GetComponent<BiomeComponent>(ground);
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var layerProto = proto.Index<BiomeMarkerLayerPrototype>(TestMarkerLayer);
            var bounds = new Box2i(0, 0, 32, 32);

            biomes.GetMarkerNodes(ground, biome, grid, layerProto, false, bounds, 20, new Random(1234),
                out var first, out _);
            biomes.GetMarkerNodes(ground, biome, grid, layerProto, false, bounds, 20, new Random(1234),
                out var second, out _);
            biomes.GetMarkerNodes(ground, biome, grid, layerProto, false, bounds, 20, new Random(99),
                out var other, out _);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first, Is.Not.Empty, "The layer placed no nodes at all, so nothing here is being measured.");
                Assert.That(second, Has.Count.EqualTo(first.Count), "The same seed laid a different number of nodes.");
                Assert.That(second.Keys.All(first.ContainsKey), Is.True, "The same seed laid nodes in different places.");
                Assert.That(other.Keys.All(first.ContainsKey) && other.Count == first.Count, Is.False,
                    "A different seed laid exactly the same nodes, so the seed is not reaching the placement.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The whole generation path, driven the only way it can be driven: an attached player on the ground map, because
    /// BiomeSystem.Update early-exits with no handled entities and only ProcessPlayerChunkRequests fills that set.
    /// Marker entities only ever appear inside the 16-tile load area, so that is the window this asserts on.
    /// </summary>
    [Test]
    public async Task MarkersGenerateUnderAnAttachedViewer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var layers = await BuildTestGround(pair);
        var ground = layers[0];

        await AttachViewer(pair, ground, Vector2.Zero);
        await ForceMarkers(pair, ground, TestMarkerLayer);

        await server.WaitAssertion(() =>
        {
            var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
            var veins = VeinsOn(entMan, ground);
            var near = veins.Where(vein => InLoadArea(entMan, ground, vein)).ToList();

            Assert.That(near, Is.Not.Empty,
                "The forced marker layer generated no deep veins inside the load area at all.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var vein in near)
                {
                    var xform = entMan.GetComponent<TransformComponent>(vein);

                    Assert.That(xform.Anchored, Is.True,
                        "A marker-spawned vein is unanchored; LoadChunkMarkers never anchors what it spawns.");
                    Assert.That(TileUnder(entMan, tileDefs, ground, vein), Is.EqualTo(GrassTile),
                        "A surviving vein sits on ground its own whitelist does not admit.");
                }
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The whitelist is the vein's own, because BiomeMarkerLayerPrototype has no tile whitelist in this fork: a flat
    /// layer scatters veins into the sea and onto the icecap, and only the MapInit handler can throw those away.
    /// </summary>
    [Test]
    public async Task VeinsOffTheWhitelistDeleteThemselves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var layers = await BuildTestGround(pair);
        var ground = layers[0];

        await LayTile(pair, ground, new Vector2i(10, 0), SnowTile);
        await LayTile(pair, ground, new Vector2i(12, 0), GrassTile);

        var onSnow = await SpawnVein(pair, ground, new Vector2(10.5f, 0.5f));
        var onGrass = await SpawnVein(pair, ground, new Vector2(12.5f, 0.5f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.EntityExists(onSnow), Is.False,
                    "A vein on snow survived, so the self-delete whitelist is not running.");
                Assert.That(entMan.EntityExists(onGrass), Is.True,
                    "A vein on grass deleted itself, so the whitelist is throwing away good ground.");
            }

            var comp = entMan.GetComponent<WFDeepVeinComponent>(onGrass);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(string.IsNullOrEmpty(comp.Ore.Id), Is.False, "The surviving vein was never stamped with an ore.");
                Assert.That(comp.TotalYield, Is.GreaterThanOrEqualTo(1500),
                    "The surviving vein's yield is below WFVeinTableAsclepiu's range.");
                Assert.That(comp.TotalYield, Is.LessThanOrEqualTo(4000),
                    "The surviving vein's yield is above WFVeinTableAsclepiu's sanctioned range.");
                Assert.That(comp.Remaining, Is.EqualTo(comp.TotalYield), "F6's remaining counter was not seeded.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The real guard is BuildMarkerChunks' early return for a chunk already in LoadedMarkers
    /// (BiomeSystem.MarkerProcessor.cs:38-39), which it reads BEFORE it ever looks at ForcedMarkerLayers. So the second
    /// pass here deliberately leaves LoadedMarkers alone; clearing it would be testing the forcing helper instead.
    /// </summary>
    [Test]
    public async Task MarkersDoNotDoubleSpawn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        var layers = await BuildTestGround(pair);
        var ground = layers[0];

        await AttachViewer(pair, ground, Vector2.Zero);
        await ForceMarkers(pair, ground, TestMarkerLayer);

        var first = 0;

        await server.WaitAssertion(() =>
        {
            first = VeinsOn(entMan, ground).Count;
            Assert.That(first, Is.GreaterThan(0), "Nothing generated on the first pass, so there is nothing to double.");
        });

        await ForceMarkers(pair, ground, TestMarkerLayer, clearLoaded: false);

        await server.WaitAssertion(() =>
        {
            Assert.That(VeinsOn(entMan, ground), Has.Count.EqualTo(first),
                "A second marker pass added veins over ground it had already done.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Contents are rolled from the biome seed and the vein's own tile index and nothing else, so the same world lays
    /// the same vein on the same tile every time. Run across two separate pairs because HashCode.Combine and
    /// string.GetHashCode are randomised per process: if either ever creeps into that path, a comparison inside one
    /// pair would still pass.
    /// </summary>
    [Test]
    public async Task VeinContentsAreSeedDeterministic()
    {
        var first = await Sample();
        var second = await Sample();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second[0].Ore, Is.EqualTo(first[0].Ore), "The same tile rolled a different ore on a second stack.");
            Assert.That(second[0].Yield, Is.EqualTo(first[0].Yield), "The same tile rolled a different yield on a second stack.");
            Assert.That(second[1].Ore, Is.EqualTo(first[1].Ore), "The same tile rolled a different ore on a second stack.");
            Assert.That(second[1].Yield, Is.EqualTo(first[1].Yield), "The same tile rolled a different yield on a second stack.");
            Assert.That(first[0].Ore == first[1].Ore && first[0].Yield == first[1].Yield, Is.False,
                "Two different tile indices rolled identical contents, so the index is not reaching the mix.");
        }

        return;

        async Task<List<(string Ore, int Yield)>> Sample()
        {
            await using var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var entMan = server.EntMan;

            var layers = await BuildTestGround(pair);
            var ground = layers[0];
            var stamps = new List<(string, int)>();

            await LayTile(pair, ground, new Vector2i(3, 0), GrassTile);
            await LayTile(pair, ground, new Vector2i(4, 0), GrassTile);

            var a = await SpawnVein(pair, ground, new Vector2(3.5f, 0.5f));
            var b = await SpawnVein(pair, ground, new Vector2(4.5f, 0.5f));

            await server.WaitAssertion(() =>
            {
                foreach (var vein in new[] { a, b })
                {
                    Assert.That(entMan.EntityExists(vein), Is.True, "A hand-spawned vein did not survive its MapInit.");

                    var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);
                    stamps.Add((comp.Ore.Id, comp.TotalYield));
                }
            });

            await Teardown(pair, layers);
            await pair.CleanReturnAsync();
            return stamps;
        }
    }

    /// <summary>
    /// The rating maths, as the pure static calls the console and the command both go through: a table of cheap ore
    /// rates Poor, an expensive one rates VeryRich, and the same table on an unsanctioned world always scores higher.
    /// The bands come from the one shipped document, so this also proves the document loads.
    /// </summary>
    [Test]
    public async Task RatingMathsBucketsCorrectly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var bands = proto.Index<WFVeinRatingBandsPrototype>(SharedWFSurveySystem.DefaultBands);

            var poor = new WFVeinTablePrototype
            {
                Ores = { ["OreSteel"] = new WFVeinEntry { Weight = 1f, Value = 0.5f } },
                YieldRange = new Vector2(1000f, 2000f),
                UnsanctionedMultiplier = 2f,
            };

            var rich = new WFVeinTablePrototype
            {
                Ores = { ["OreUranium"] = new WFVeinEntry { Weight = 1f, Value = 9f } },
                YieldRange = new Vector2(3000f, 5000f),
                UnsanctionedMultiplier = 2f,
            };

            var sanctionedScore = SharedWFSurveySystem.Score(rich, true);
            var unsanctionedScore = SharedWFSurveySystem.Score(rich, false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SharedWFSurveySystem.Rate(SharedWFSurveySystem.Score(poor, true), bands),
                    Is.EqualTo(WFVeinRating.Poor), "A cheap, thin table does not rate Poor.");
                Assert.That(SharedWFSurveySystem.Rate(sanctionedScore, bands),
                    Is.EqualTo(WFVeinRating.VeryRich), "A rich table does not rate VeryRich.");
                Assert.That(unsanctionedScore, Is.GreaterThan(sanctionedScore),
                    "An unsanctioned world does not score above the same table sanctioned.");
                Assert.That(SharedWFSurveySystem.Score(new WFVeinTablePrototype(), true), Is.EqualTo(0f),
                    "An empty table scores something other than nothing.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The vein's sprite layer is the whole hiding mechanism: it ships invisible and the client flips it on per player.
    /// This lives here rather than in PlanetCrackerPrototypeTest because WFDeepVein deliberately stays out of that
    /// file's shared spawn array - it would delete itself on a FloorSteel grid with no planet network behind it.
    /// </summary>
    [Test]
    public async Task VeinSpriteLayerIsHiddenAndNamesARealState()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var clientEntMan = client.EntMan;

        var layers = await BuildTestGround(pair);
        var ground = layers[0];

        await LayTile(pair, ground, new Vector2i(5, 0), GrassTile);

        var vein = await SpawnVein(pair, ground, new Vector2(5.5f, 0.5f));

        // The client only ever receives what its own eye is near, so the session has to be standing next to it.
        await AttachViewer(pair, ground, Vector2.Zero);
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var uid = pair.ToClientUid(vein);

            Assert.That(clientEntMan.TryGetComponent(uid, out SpriteComponent? sprite), Is.True,
                "The vein never reached the client, so nothing could draw or examine it.");

            var sprites = clientEntMan.System<SpriteSystem>();

            Assert.That(sprites.LayerMapTryGet((uid, sprite), WFDeepVeinVisualLayers.Marker, out var index, false), Is.True,
                "The vein's sprite does not map the Marker layer both client systems drive.");

            var layer = sprite!.AllLayers.ElementAt(index);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(layer.Visible, Is.False,
                    "The vein's marker layer is visible to a player who has never surveyed it.");
                Assert.That(layer.RsiState.Name, Is.Not.Null, "The vein's marker layer names no RSI state.");
                Assert.That(layer.ActualRsi, Is.Not.Null, "The vein's marker layer resolves no RSI.");
                Assert.That(layer.ActualRsi!.TryGetState(layer.RsiState.Name!, out _), Is.True,
                    $"The vein names state '{layer.RsiState.Name}', which {VeinRsi} does not have.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Builds the grass-only test stack and returns its layers, ground first.</summary>
    public static async Task<List<EntityUid>> BuildTestGround(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var layers = new List<EntityUid>();

        await EnableFeature(pair);

        await server.WaitPost(() =>
        {
            var surface = proto.Index<WFPlanetSurfacePrototype>(TestSurface);
            var built = networks.BuildNetwork(surface, Vector2.Zero, "Testicus", null);

            Assert.That(built, Is.Not.Null, "The test planet network failed to build.");
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(built!.Value).Layers);
        });

        await server.WaitRunTicks(1);
        return layers;
    }

    /// <summary>Lays one named tile, because the shared fixture's LayTiles only ever lays deck plating.</summary>
    public static async Task LayTile(TestPair pair, EntityUid ground, Vector2i index, string tileId)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            maps.SetTile(ground, grid, index, new Tile(tileDefs[tileId].TileId));
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>Hand-spawns a vein and lets its MapInit handler run to completion, delete included.</summary>
    public static async Task<EntityUid> SpawnVein(TestPair pair, EntityUid ground, Vector2 pos)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var vein = EntityUid.Invalid;

        await server.WaitPost(() => vein = entMan.SpawnEntity(VeinProto, new EntityCoordinates(ground, pos)));
        await server.WaitRunTicks(2);
        return vein;
    }

    /// <summary>Every deep vein sitting on one ground layer.</summary>
    private static List<EntityUid> VeinsOn(IEntityManager entMan, EntityUid ground)
    {
        var found = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<WFDeepVeinComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid == ground)
                found.Add(uid);
        }

        return found;
    }

    /// <summary>Whether a vein landed inside BiomeSystem's 16-tile load area, which is all a marker pass can reach.</summary>
    private static bool InLoadArea(IEntityManager entMan, EntityUid ground, EntityUid vein)
    {
        var maps = entMan.System<SharedMapSystem>();
        var grid = entMan.GetComponent<MapGridComponent>(ground);
        var index = maps.TileIndicesFor(ground, grid, entMan.GetComponent<TransformComponent>(vein).Coordinates);

        return Math.Abs(index.X) <= LoadArea && Math.Abs(index.Y) <= LoadArea;
    }

    /// <summary>The content tile id under an entity.</summary>
    private static string TileUnder(IEntityManager entMan, ITileDefinitionManager tileDefs, EntityUid ground, EntityUid uid)
    {
        var maps = entMan.System<SharedMapSystem>();
        var grid = entMan.GetComponent<MapGridComponent>(ground);
        var index = maps.TileIndicesFor(ground, grid, entMan.GetComponent<TransformComponent>(uid).Coordinates);

        return tileDefs[maps.GetTileRef(ground, grid, index).Tile.TypeId].ID;
    }
}
