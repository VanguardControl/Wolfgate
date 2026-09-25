#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.PlanetCracker.Anchors;
using Content.Server._WF.PlanetCracker.Fissures;
using Content.Server.Decals;
using Content.Server.NPC.HTN;
using Content.Server.Parallax;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Fissures;
using Content.Shared.Decals;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>Site threats: ring schedule, pinning, spawn caps, the extraction surge and threat stamping.</summary>
[TestFixture]
[TestOf(typeof(WFFissureSpawnerSystem))]
public sealed class FissureTest
{
    /// <summary>A deterministic faction for exact counts; kept out of Resources so expeditions never roll it.</summary>
    [TestPrototypes]
    public const string Prototypes = @"
- type: salvageFaction
  id: WFTestFissureFaction
  cost: 1
  desc: salvage-faction-xenos
  groups:
  - entries:
    - id: NFMobXenoDrone
      amount: 1
";

    /// <summary>The deterministic faction the exact-count tests drive.</summary>
    private const string TestFaction = "WFTestFissureFaction";

    /// <summary>WFSurfaceAsclepiu's sanctioned faction.</summary>
    private const string AsclepiuFaction = "Xenos";

    /// <summary>The compound every stamped threat is re-rooted onto.</summary>
    private const string ThreatCompound = "WFFissureThreatCompound";

    /// <summary>The stage-one decal the pinning test's negative control is stamped with by hand.</summary>
    private const string FissureDecal = "WFFissure1";

    /// <summary>SharedBiomeSystem.ChunkSize, which is protected and therefore mirrored here.</summary>
    private const byte BiomeChunkSize = 8;

    /// <summary>A biome chunk origin outside the laid deck, so its tiles are real generated terrain.</summary>
    private static readonly Vector2i ControlChunk = new(64, 64);

    /// <summary>Drill length every armed test shortens the shipped five minutes to.</summary>
    private static readonly TimeSpan ShortDrill = TimeSpan.FromSeconds(10);

    /// <summary>Rings spread evenly over the drill from arm, and the faction resolves off the ground.</summary>
    [Test]
    public async Task RingsFollowTheDrillSchedule()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 16f, false);
        await ArmDrill(pair, site, ShortDrill);

        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var comp = Spawner(entMan, site.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Sanctioned, Is.True,
                    "The arm handler did not resolve WFSurfaceAsclepiu's sanctioned flag.");
                Assert.That(comp.Faction?.Id, Is.EqualTo(AsclepiuFaction),
                    "The arm handler did not resolve the world's salvage faction off the ground layer.");
                Assert.That(comp.RingsDone, Is.InRange(1, 3),
                    $"Three seconds into a ten second drill is {comp.RingsDone} rings, not the one to three the two second cadence allows.");
                Assert.That(comp.RingsDone, Is.LessThan(comp.RingCount),
                    "Every ring fired at once; the schedule is not being honoured at all.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(9f));

        await server.WaitAssertion(() =>
        {
            var comp = Spawner(entMan, site.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.RingsDone, Is.EqualTo(comp.RingCount),
                    "The full drill did not spread all of its rings.");
                Assert.That(comp.Decals, Has.Count.GreaterThanOrEqualTo(comp.RingCount),
                    "Five rings stamped fewer than five decals between them.");

                // Bounded, not exact: the shipped Xenos table can roll empty groups.
                Assert.That(comp.SpawnedTotal, Is.GreaterThan(0),
                    "Five rings over a faction-bearing world sent up nothing at all.");
                Assert.That(comp.SpawnedTotal, Is.LessThanOrEqualTo(comp.Cap),
                    "The anchor spawned past its cumulative cap.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Pinned fissure decals survive an unload, while an unpinned control does not.</summary>
    [Test]
    public async Task FissureDecalsArePinnedAndSurviveAnUnload()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var decals = server.System<DecalSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableFeature(pair);
        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 16f, false);
        await ArmDrill(pair, site, ShortDrill);
        await server.WaitRunTicks(pair.SecondsToTicks(12f));

        var fissures = new List<Vector2i>();
        var stamped = new List<uint>();
        var centre = Vector2.Zero;

        await server.WaitAssertion(() =>
        {
            var comp = Spawner(entMan, site.Anchors[0]);

            Assert.That(comp.Fissures, Is.Not.Empty, "Precondition: the drill opened any fissures at all.");

            fissures.AddRange(comp.Fissures);
            stamped.AddRange(comp.Decals);
            centre = comp.Centre;

            Assert.That(PinnedCount(pair, site.Ground, comp.Fissures), Is.EqualTo(comp.Fissures.Count),
                "Some fissure tiles were never pinned, so the biome is free to empty them and take their decals.");
        });

        var control = 0u;
        var controlIndex = Vector2i.Zero;

        await server.WaitAssertion(() =>
        {
            var biome = BiomeOf(entMan, site.Ground);
            var candidates = new List<Vector2i>();

            // Loaded by hand (no viewer); the pre-flight unload finds an index the unloader really empties.
            biomes.WfLoadChunk(biome, ControlChunk);
            biomes.WfUnloadChunk(biome, ControlChunk);

            for (var x = 0; x < BiomeChunkSize; x++)
            for (var y = 0; y < BiomeChunkSize; y++)
            {
                var index = ControlChunk + new Vector2i(x, y);

                if (biomes.WfIsPinned((site.Ground, biome.Comp1), index))
                    continue;

                if (maps.TryGetTileRef(site.Ground, biome.Comp2, index, out var emptied) && emptied.Tile.IsEmpty)
                    candidates.Add(index);
            }

            // Regenerates identically from index and seed.
            biomes.WfLoadChunk(biome, ControlChunk);

            foreach (var index in candidates)
            {
                if (!maps.TryGetTileRef(site.Ground, biome.Comp2, index, out var tile) || tile.Tile.IsEmpty)
                    continue;

                if (!decals.TryAddDecal(FissureDecal, new EntityCoordinates(site.Ground, index), out var id))
                    continue;

                control = id;
                controlIndex = index;
                break;
            }

            Assert.That(control, Is.Not.Zero,
                "The control decal could not be stamped on genuine unpinned biome ground, so the test proves nothing.");
        });

        // One callback, so nothing ticks in between.
        await server.WaitPost(() =>
        {
            var biome = BiomeOf(entMan, site.Ground);
            var origins = ChunkOrigins(fissures);
            origins.Add(ControlChunk);

            foreach (var origin in origins)
            {
                biomes.WfLoadChunk(biome, origin);
                biomes.WfUnloadChunk(biome, origin);
                biomes.WfLoadChunk(biome, origin);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var survivors = DecalsNear(pair, site.Ground, centre, 24f);
            var lost = stamped.Count(id => !survivors.ContainsKey(id));
            var controls = DecalsNear(pair, site.Ground, (Vector2)controlIndex, 4f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(lost, Is.Zero,
                    $"{lost} of {stamped.Count} pinned fissure decals did not survive the unload.");
                Assert.That(controls.ContainsKey(control), Is.False,
                    "The UNPINNED control decal survived the same unload, so this harness cannot tell a pin from no pin.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The spawn cap is cumulative per anchor; killing what is out does not re-open it.</summary>
    [Test]
    public async Task SpawnsAreCappedPerAnchor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 16f, false);

        await ArmDrill(pair, site, ShortDrill, comp =>
        {
            comp.Faction = new ProtoId<SalvageFactionPrototype>(TestFaction);
            comp.Cap = 4;
            comp.RingCount = 5;
            comp.MinMobs = 3;
            comp.MaxMobs = 3;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(12f));

        await server.WaitAssertion(() =>
        {
            var comp = Spawner(entMan, site.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.RingsDone, Is.EqualTo(5), "The drill did not spread all five rings.");
                Assert.That(comp.SpawnedTotal, Is.EqualTo(4),
                    "Five rings of three mobs each did not stop exactly at the cumulative cap of four.");
                Assert.That(comp.Decals, Has.Count.GreaterThan(4),
                    "The cap gated the decals too; it is supposed to gate mobs only.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>An unsanctioned crack takes 1.5x on both fissure and mob counts.</summary>
    [Test]
    public async Task UnsanctionedPlanetsSpawnMore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var sanctioned = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, sanctioned, 0f, 16f, false);
        await ArmDrill(pair, sanctioned, ShortDrill, Tune);
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var comp = Spawner(entMan, sanctioned.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Fissures, Has.Count.EqualTo(4), "A sanctioned ring did not open its four fissures.");
                Assert.That(comp.SpawnedTotal, Is.EqualTo(2), "A sanctioned ring did not send up its two mobs.");
            }
        });

        await Teardown(pair, sanctioned);

        var unsanctioned = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, unsanctioned, 0f, 16f, false);

        await ArmDrill(pair, unsanctioned, ShortDrill, comp =>
        {
            Tune(comp);
            comp.Sanctioned = false;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var comp = Spawner(entMan, unsanctioned.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Fissures, Has.Count.EqualTo(6),
                    "An unsanctioned ring did not take the 1.5x ceiling on its fissure count.");
                Assert.That(comp.SpawnedTotal, Is.EqualTo(3),
                    "An unsanctioned ring did not take the 1.5x ceiling on its mob count.");
            }
        });

        await Cleanup(pair, unsanctioned);
        return;

        static void Tune(WFFissureSpawnerComponent comp)
        {
            comp.Faction = new ProtoId<SalvageFactionPrototype>(TestFaction);
            comp.RingCount = 1;
            comp.Cap = 100;
            comp.MinMobs = 2;
            comp.MaxMobs = 2;
            comp.MinFissures = 4;
            comp.MaxFissures = 4;
        }
    }

    /// <summary>Only a running drill spreads rings; a locked or early-finished anchor stays quiet.</summary>
    [Test]
    public async Task ALockedAnchorIsQuiet()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();

        await EnableFeature(pair);
        var locked = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, locked, 0f, 16f, true);
        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            var comp = Spawner(entMan, locked.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Armed, Is.False, "An anchor that never drilled is armed.");
                Assert.That(comp.RingsDone, Is.Zero, "A locked anchor spread rings.");
                Assert.That(comp.SpawnedTotal, Is.Zero, "A locked anchor sent mobs up.");
                Assert.That(comp.Decals, Is.Empty, "A locked anchor split the ground open.");
            }
        });

        await Teardown(pair, locked);

        var drilling = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, drilling, 0f, 16f, false);
        await ArmDrill(pair, drilling, ShortDrill);
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        var before = 0;

        await server.WaitAssertion(() =>
        {
            before = Spawner(entMan, drilling.Anchors[0]).RingsDone;
            Assert.That(before, Is.GreaterThan(0), "Precondition: at least one ring fired before the drill was cut short.");
        });

        await server.WaitPost(() =>
        {
            foreach (var anchor in drilling.Anchors)
            {
                anchors.CompleteDrill((anchor, entMan.GetComponent<WFGravityAnchorComponent>(anchor)));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            var comp = Spawner(entMan, drilling.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Armed, Is.False, "The finished drill left its spawner armed.");
                Assert.That(comp.RingsDone, Is.EqualTo(before), "A locked anchor kept spreading rings.");
            }
        });

        await Cleanup(pair, drilling);
    }

    /// <summary>Dissolving the pair mid-drill stops the rings, though no drill-cancel event is raised.</summary>
    [Test]
    public async Task ADissolvedPairStopsTheRings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 16f, false);
        await ArmDrill(pair, site, ShortDrill);
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        var before = 0;

        await server.WaitAssertion(() =>
        {
            before = Spawner(entMan, site.Anchors[0]).RingsDone;
            Assert.That(before, Is.GreaterThan(0), "Precondition: at least one ring fired before the pair was broken.");
        });

        await server.WaitPost(() =>
        {
            var partner = site.Anchors[1];
            transform.Unanchor(partner, entMan.GetComponent<TransformComponent>(partner));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            var comp = Spawner(entMan, site.Anchors[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Armed, Is.False, "The surviving half stayed armed after the pair dissolved.");
                Assert.That(comp.RingsDone, Is.EqualTo(before), "The rings kept spreading after the drill was cancelled.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>The extraction surge spawns on the disc perimeter, on both the unarmed and the armed path.</summary>
    [Test]
    public async Task TheExtractionSurgeSpawnsOnThePerimeter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var unarmed = await BuildReadyToExtract(pair);
        await BeginCut(pair, unarmed);
        await CompleteCut(pair, unarmed);

        await server.WaitAssertion(() =>
        {
            var chunk = FindChunk(entMan);

            Assert.That(chunk, Is.Not.EqualTo(EntityUid.Invalid),
                "The cut produced no chunk, so the surge's `before` ordering broke the extraction itself.");

            var chunkComp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);
            var rim = RimIndices(entMan, unarmed.Ground, chunkComp.HoleCentre, chunkComp.Radius).ToHashSet();
            var disc = DiscIndices(entMan, unarmed.Ground, chunkComp.HoleCentre, chunkComp.Radius).ToHashSet();
            var solid = HoleTiles(entMan, unarmed.Ground, chunkComp.HoleCentre, chunkComp.Radius)
                .Count(entry => !entry.Tile.IsEmpty);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(solid, Is.Zero, $"{solid} disc tiles are still solid; the cut did not finish.");

                foreach (var anchor in unarmed.Anchors)
                {
                    var comp = Spawner(entMan, anchor);

                    Assert.That(comp.SurgeSpawned, Is.GreaterThan(0),
                        "An anchor that was never armed surged nothing; the surge is reading the arm handler's cache.");
                    Assert.That(comp.Fissures, Is.Not.Empty, "The surge stamped no fissures at all.");

                    // Asserted over the chosen tiles, since freshly spawned mobs may already have walked off them.
                    foreach (var index in comp.Fissures)
                    {
                        Assert.That(rim, Does.Contain(index),
                            $"The surge opened a fissure at {index}, which is not on the disc's perimeter.");
                        Assert.That(disc, Does.Not.Contain(index),
                            $"The surge opened a fissure at {index}, inside the disc, where the hole stamp destroys it.");
                    }

                    foreach (var mob in comp.Live)
                    {
                        Assert.That(entMan.EntityExists(mob), Is.True, "A surge threat was deleted by the extraction.");
                        Assert.That(entMan.GetComponent<TransformComponent>(mob).ParentUid, Is.Not.EqualTo(chunk),
                            "A surge threat was carried into orbit with the disc, so it was spawned inside the cut.");
                    }
                }
            }
        });

        await ReleaseSite(pair, unarmed);

        var cached = await BuildReadyToExtract(pair);
        await BeginCut(pair, cached);

        await server.WaitPost(() =>
        {
            foreach (var anchor in cached.Anchors)
            {
                var comp = Spawner(entMan, anchor);
                comp.Faction = new ProtoId<SalvageFactionPrototype>(TestFaction);
                comp.Sanctioned = false;
            }
        });

        await CompleteCut(pair, cached);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var anchor in cached.Anchors)
                {
                    var comp = Spawner(entMan, anchor);
                    var expected = (int)MathF.Ceiling(comp.SurgeMobs * comp.UnsanctionedMultiplier);

                    Assert.That(comp.SurgeSpawned, Is.EqualTo(expected),
                        "The surge did not take the cached faction and the unsanctioned 1.5x budget.");
                }
            }
        });

        await Cleanup(pair, cached);
    }

    /// <summary>Every live fissure mob is re-rooted onto the threat compound and targets the anchor.</summary>
    [Test]
    public async Task FissureMobsAreStampedAsSiteThreats()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var site = await BuildCrackerInOrbit(pair);
        await DeployPair(pair, site, 0f, 16f, false);

        await ArmDrill(pair, site, ShortDrill, comp =>
            comp.Faction = new ProtoId<SalvageFactionPrototype>(TestFaction));

        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var anchor = site.Anchors[0];
            var comp = Spawner(entMan, anchor);

            Assert.That(comp.Live, Is.Not.Empty, "The ring sent nothing up that could be stamped at all.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var mob in comp.Live)
                {
                    Assert.That(entMan.TryGetComponent(mob, out HTNComponent? htn), Is.True,
                        "An entity with no HTN reached Live, which only stamped threats may.");
                    Assert.That(entMan.HasComponent<MobStateComponent>(mob), Is.True,
                        "A non-mob reached Live; only a real mob may be re-rooted onto the fissure compound.");
                    Assert.That(htn!.RootTask.Task, Is.EqualTo(ThreatCompound),
                        "A fissure threat kept its stock root task, so it never goes for the anchor.");

                    // FactionExceptionComponent allows outside reads.
                    Assert.That(entMan.TryGetComponent(mob, out FactionExceptionComponent? exception), Is.True,
                        "A fissure threat carries no faction exception, so the anchor is not a target at all.");
                    Assert.That(exception!.Hostiles, Does.Contain(anchor),
                        "A fissure threat was never made hostile to the anchor that spawned it.");
                }
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>Arms an undrilled pair on a short drill, then applies the tune, which arming would overwrite.</summary>
    private static async Task ArmDrill(
        TestPair pair,
        CrackerSite site,
        TimeSpan duration,
        Action<WFFissureSpawnerComponent>? tune = null)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var anchors = server.System<WFGravityAnchorSystem>();

        await server.WaitAssertion(() =>
        {
            foreach (var anchor in site.Anchors)
            {
                var comp = entMan.GetComponent<WFGravityAnchorComponent>(anchor);
                comp.DrillDuration = duration;

                Assert.That(anchors.BeginDrill((anchor, comp)), Is.True,
                    $"Precondition: the anchor was Paired and started drilling; it was {comp.State}.");
            }

            if (tune is null)
                return;

            foreach (var anchor in site.Anchors)
            {
                tune(Spawner(entMan, anchor));
            }
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>One anchor's fissure spawner, which every anchor prototype carries.</summary>
    private static WFFissureSpawnerComponent Spawner(IEntityManager entMan, EntityUid anchor)
    {
        Assert.That(entMan.TryGetComponent(anchor, out WFFissureSpawnerComponent? comp), Is.True,
            "The deployed anchor carries no fissure spawner at all.");

        return comp!;
    }

    /// <summary>The ground layer as the chunk pin API wants it: the biome and the grid together.</summary>
    private static Entity<BiomeComponent, MapGridComponent> BiomeOf(IEntityManager entMan, EntityUid ground)
    {
        return new Entity<BiomeComponent, MapGridComponent>(
            ground,
            entMan.GetComponent<BiomeComponent>(ground),
            entMan.GetComponent<MapGridComponent>(ground));
    }

    /// <summary>Every 8-tile biome chunk origin a set of indices touches.</summary>
    private static HashSet<Vector2i> ChunkOrigins(IEnumerable<Vector2i> indices)
    {
        var origins = new HashSet<Vector2i>();

        foreach (var index in indices)
        {
            origins.Add(SharedMapSystem.GetChunkIndices(index, BiomeChunkSize) * BiomeChunkSize);
        }

        return origins;
    }

    /// <summary>Every decal near a point by id, via the decal system since DecalGridComponent is locked.</summary>
    private static Dictionary<uint, Decal> DecalsNear(TestPair pair, EntityUid ground, Vector2 centre, float span)
    {
        var decals = pair.Server.System<DecalSystem>();
        var found = new Dictionary<uint, Decal>();

        foreach (var (id, decal) in decals.GetDecalsIntersecting(ground, Box2.CenteredAround(centre, new Vector2(span, span))))
        {
            found[id] = decal;
        }

        return found;
    }

    /// <summary>Deletes any chunk and tears the site down without returning the pair, for a second site.</summary>
    private static async Task ReleaseSite(TestPair pair, CrackerSite site)
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
    }

    /// <summary>Releases the site and returns the pair; the last thing every test here does.</summary>
    private static async Task Cleanup(TestPair pair, CrackerSite site)
    {
        await ReleaseSite(pair, site);
        await pair.CleanReturnAsync();
    }
}
