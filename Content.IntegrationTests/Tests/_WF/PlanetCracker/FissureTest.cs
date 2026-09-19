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

/// <summary>
/// Site threats end to end: that an armed drill spreads its rings on the schedule the drill's own length sets, that
/// every fissure tile is pinned so its decal outlives a biome unload, that the cumulative per-anchor cap really is
/// cumulative, that an unsanctioned world gets the 1.5x of both counts, that a locked or dissolved pair goes quiet,
/// that the extraction surge lands on the disc's perimeter on the unarmed path production actually takes, and that
/// every mob that climbs out is re-rooted onto the anchor.
/// </summary>
[TestFixture]
[TestOf(typeof(WFFissureSpawnerSystem))]
public sealed class FissureTest
{
    /// <summary>
    /// A faction that cannot roll a surprise. The shipped Xenos table has a group whose only entry is
    /// `NFMobXenoDrone amount: 0 maxAmount: 2` (Resources/Prototypes/Procedural/salvage_factions.yml:17-21), which
    /// EntitySpawnEntry.GetAmount resolves to random.Next(0, 2) and therefore rolls NOTHING about half the time it is
    /// drawn, and another whose only entity (WeaponTurretXeno) is a structure with no MobStateComponent and so is never
    /// stamped and never joins Live. One group, one entry, one fixed amount makes every exact-count assertion below
    /// deterministic.
    /// It lives here and NOT in Resources on purpose: Content.Shared/Salvage/SharedSalvageSystem.cs:102 picks an
    /// expedition's faction with GetMod, which enumerates EVERY salvageFaction prototype, so a shipped test faction
    /// would quietly pollute real expedition generation for the whole round.
    /// </summary>
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

    /// <summary>What WFSurfaceAsclepiu names as its sanctioned faction; the resolution assertion's expected value.</summary>
    private const string AsclepiuFaction = "Xenos";

    /// <summary>The compound every stamped threat is re-rooted onto.</summary>
    private const string ThreatCompound = "WFFissureThreatCompound";

    /// <summary>The stage-one decal the pinning test's negative control is stamped with by hand.</summary>
    private const string FissureDecal = "WFFissure1";

    /// <summary>SharedBiomeSystem.ChunkSize, which is protected and therefore mirrored here.</summary>
    private const byte BiomeChunkSize = 8;

    /// <summary>
    /// A biome chunk origin far outside the fixture's hand-laid deck rectangle of (-16,-16)..(32,16), so the tiles in
    /// it are genuine generated terrain and an unload really does empty them.
    /// </summary>
    private static readonly Vector2i ControlChunk = new(64, 64);

    /// <summary>Drill length every armed test shortens the shipped five minutes to.</summary>
    private static readonly TimeSpan ShortDrill = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The rings walk on the drill's own clock: RingCount of them over DrillDuration, the first due the moment the
    /// drill starts. At a ten second drill the cadence is two seconds against the system's 1 Hz sweep, so the five
    /// rings are due at arm+0/2/4/6/8 and the anchor's own lock sweep lands at arm+10.
    /// No tune, so this is also the resolution test: nothing but the arm handler's walk off the ground layer can put
    /// Xenos into the component.
    /// </summary>
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

                // Bounded, never exact: this test runs against the SHIPPED Xenos table, two of whose groups can
                // legitimately roll an empty spawn list, so any exact or lower-bounded mob count here would be a
                // coin flip. The deterministic [TestPrototypes] faction is what the counting tests use instead.
                Assert.That(comp.SpawnedTotal, Is.GreaterThan(0),
                    "Five rings over a faction-bearing world sent up nothing at all.");
                Assert.That(comp.SpawnedTotal, Is.LessThanOrEqualTo(comp.Cap),
                    "The anchor spawned past its cumulative cap.");
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>
    /// D19's real precondition: every fissure tile is pinned against the biome, so the decal on it is never wiped.
    /// PinnedCount is the PRIMARY assertion. The unload/reload cycle on its own proves nothing here and the negative
    /// control is what makes it honest: the fixture lays hand-made deck over (-16,-16)..(32,16), which covers every
    /// tile inside the maximum ring radius of eight, and UnloadTiles only empties an index whose grid tile EQUALS the
    /// biome tile for it (Content.Server/Parallax/BiomeSystem.ChunkLoader.cs:287-294) while LoadTiles skips any
    /// non-empty index (:85-86) - so a laid floor lands in `modified` and survives whether or not the pin ever ran.
    /// A decal stamped on genuine, UNPINNED biome ground dying in the same harness is the other half of the proof.
    /// </summary>
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

            // Generated by hand, because nothing on this map has a viewer and the biome loader never runs on its own.
            // The load/unload pre-flight is what makes the control honest: an index is only a usable control if the
            // unloader really does empty it, and several do not - UnloadEntities pins the tile under any biome entity
            // it could not cleanly delete (BiomeSystem.ChunkLoader.cs:233-274) and UnloadTiles pins anything still
            // carrying an anchored entity, so picking the first generated tile would be a coin flip.
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

            // Regenerated identically: the biome tile of an index is a pure function of the index and the seed.
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

        // One callback for all three passes over every touched chunk: nothing may tick in between.
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

    /// <summary>
    /// The cap is CUMULATIVE and per anchor: once an anchor has spawned its budget the rings keep splitting the ground
    /// cosmetically but send nothing else up, and killing what is already out cannot re-open it.
    /// </summary>
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

    /// <summary>
    /// An unsanctioned crack takes the 1.5x ceiling on BOTH counts: more ground split open and more of what lives
    /// under it. One ring apiece so the arithmetic is exact rather than a sum over five rolls.
    /// </summary>
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

    /// <summary>
    /// Only a RUNNING drill spreads rings. An anchor walked straight to Locked never arms at all, and one whose drill
    /// is finished early stops where it stood: the sweep re-checks the anchor's state before it looks at the schedule.
    /// </summary>
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

    /// <summary>
    /// The cancel with no cancel event: unanchoring one half runs Dissolve -> Demote, which drops Drilling back to
    /// Deployed and zeroes DrillEnd while raising only WFAnchorPairDissolvedEvent. Both the event handler and the
    /// sweep's own state re-check have to stop the rings.
    /// </summary>
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

    /// <summary>
    /// The surge, on both paths.
    /// Part A is the one production actually takes: nothing that reaches extraction raises WFAnchorDrillStartedEvent,
    /// because BuildReadyToExtract and `wfcracker complete drill` both go Paired -> Locked through CompleteDrill. A
    /// surge that read its ground, centre and faction off the arm handler's cache would spawn NOTHING here and would
    /// sort the rim by distance to the grid origin for both anchors.
    /// Part B drives the cached fast path and the unsanctioned budget.
    /// </summary>
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

                    // Asserted over the tiles the surge CHOSE, not over where its mobs are standing now. A freshly
                    // spawned HTN NPC is awake until NPCSystem's own sweep gets round to sleeping it, and these are
                    // stamped hostile to the anchor, so within the first second some of them have already walked a
                    // tile or two off the ring they came out of. The chosen set is what MoveRiders sees at extraction
                    // and is therefore the honest subject of "outside the disc".
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

    /// <summary>
    /// Every mob that climbs out is a SITE threat, not just another hostile: it is re-rooted onto the fissure compound
    /// and the anchor is written into its faction exceptions, which are the two halves the targeting needs.
    /// Asserted over Live rather than SpawnedTotal because an entry that is not a mob (WeaponTurretXeno,
    /// salvage_factions.yml:22-25 - it has an HTNComponent but no MobStateComponent, so the stamp deliberately leaves
    /// its TurretCompound alone) increments the latter without ever joining the former.
    /// There is deliberately NO "the anchor takes damage" test here. NPCSystem.CheckPlayerDistancesAndPauseNPCs
    /// (Content.Server/NPC/Systems/NPCSystem.cs:174-230) sleeps every HTN NPC with no live player within
    /// npc.player_pause_distance, measured with EntityCoordinates.TryDistance, which fails across maps - and a headless
    /// fixture has nobody standing on the ground layer at all, so every fissure mob is asleep and such a test would
    /// fail deterministically with no bug present.
    /// </summary>
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

                    // Reading Hostiles is legal from here: FactionExceptionComponent's [Access] leaves
                    // OtherDefaultPermissions at Read.
                    Assert.That(entMan.TryGetComponent(mob, out FactionExceptionComponent? exception), Is.True,
                        "A fissure threat carries no faction exception, so the anchor is not a target at all.");
                    Assert.That(exception!.Hostiles, Does.Contain(anchor),
                        "A fissure threat was never made hostile to the anchor that spawned it.");
                }
            }
        });

        await Cleanup(pair, site);
    }

    /// <summary>
    /// Arms both halves of a deployed pair on a shortened drill and only THEN applies the tune.
    /// The order is load-bearing: OnDrillStarted writes Faction and Sanctioned at arm, so a tune applied first would be
    /// silently overwritten by whatever the world resolves to. The site must have been deployed with drill: false, or
    /// the anchors are already Locked and BeginDrill refuses them.
    /// </summary>
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

    /// <summary>Every biome chunk origin a set of indices touches; the biome's own chunks are 8 tiles, not 16.</summary>
    private static HashSet<Vector2i> ChunkOrigins(IEnumerable<Vector2i> indices)
    {
        var origins = new HashSet<Vector2i>();

        foreach (var index in indices)
        {
            origins.Add(SharedMapSystem.GetChunkIndices(index, BiomeChunkSize) * BiomeChunkSize);
        }

        return origins;
    }

    /// <summary>
    /// Every decal on the ground near a point, by id.
    /// Read through the decal system's own query: DecalGridComponent is access-locked to that system, and even a
    /// dictionary lookup on its index counts as an Execute the analyzer refuses.
    /// </summary>
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

    /// <summary>
    /// Deletes the cut chunk if there is one and tears the site down, WITHOUT returning the pair.
    /// A test that builds a second site has to use this for the first one: CleanReturnAsync ends the pair's test
    /// context, and the next BuildCrackerInOrbit then dies on the first line the server logs.
    /// </summary>
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
