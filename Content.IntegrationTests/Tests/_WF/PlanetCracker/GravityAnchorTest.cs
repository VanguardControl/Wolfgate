#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Content.IntegrationTests.Pair;
using Content.Server._WF.PlanetCracker.Anchors;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Repairable;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// F3 end to end on a real Asclepiu stack: where an anchor may be wrenched down, the nine-tile footprint at both the
/// attempt and the completion, the reservation that keeps the ground under it, the pairing band and its owner and grid
/// rules, the unattended drill, the damage flag, breakage and repair, and the two verbs.
/// </summary>
[TestFixture]
[TestOf(typeof(WFGravityAnchorSystem))]
public sealed class GravityAnchorTest
{
    private const string Anchor = "WFGravityAnchor";
    private const string Crate = "WFAnchorCrate";
    private const string Wall = "WallSolid";
    private const string WrenchProto = "Wrench";
    private const string Crowbar = "Crowbar";
    private const string GroundTile = "FloorSteel";
    private const string Blunt = "Blunt";

    /// <summary>WFGravityAnchorComponent.MinDistance.</summary>
    private const float MinBand = 16f;

    /// <summary>WFGravityAnchorComponent.MaxDistance.</summary>
    private const float MaxBand = 40f;

    /// <summary>WFGravityAnchorComponent.CutPadding.</summary>
    private const float CutPadding = 2f;

    /// <summary>Damage that clears the prototype's Breakage trigger without reaching its Destruction one.</summary>
    private const float BreakingDamage = 320f;

    /// <summary>Drill length the timing tests override onto their anchors.</summary>
    private static readonly TimeSpan ShortDrill = TimeSpan.FromSeconds(2);

    /// <summary>Reads the percentage out of the drill examine line.</summary>
    private static readonly Regex DrillPercent = new(@"(\d+)%");

    /// <summary>
    /// An anchor may only grip the biome-backed ground layer itself. The parked-shuttle case is the one a MapUid-only
    /// gate would wrongly allow, and it is exactly the transport cargo bay the rule exists to forbid.
    /// </summary>
    [Test]
    public async Task AnchoringIsRefusedOffAPlanetGround()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        var deck = await pair.CreateTestMap();
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(8, 8));
        var parked = await ParkHull(pair, stack[0], new Vector2(20f, 20f));

        await server.WaitAssertion(() =>
        {
            var tool = SpawnTool(entMan, WrenchProto, deck.GridCoords);

            var onDeck = entMan.SpawnEntity(Anchor, deck.GridCoords);
            var onAir = entMan.SpawnEntity(Anchor, new EntityCoordinates(stack[1], Vector2.Zero));
            var onHull = entMan.SpawnEntity(Anchor, new EntityCoordinates(parked, new Vector2(2.5f, 2.5f)));
            var onGround = entMan.SpawnEntity(Anchor, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Wrench(entMan, transform, onDeck, tool), Is.False,
                    "An anchor was wrenched down on a plain station grid.");
                Assert.That(Wrench(entMan, transform, onAir, tool), Is.False,
                    "An anchor was wrenched down on an air layer.");
                Assert.That(Wrench(entMan, transform, onHull, tool), Is.False,
                    "An anchor was wrenched down on a hull parked on the ground layer.");

                foreach (var refused in new[] { onDeck, onAir, onHull })
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(refused).Anchored, Is.False,
                        "A refused anchor ended up anchored anyway.");
                    Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(refused).State,
                        Is.EqualTo(WFAnchorState.Loose), "A refused anchor left the Loose state.");
                }

                Assert.That(Wrench(entMan, transform, onGround, tool), Is.True,
                    "An anchor could not be wrenched down on the planet ground layer itself.");
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(onGround).State,
                    Is.EqualTo(WFAnchorState.Deployed), "The deployed anchor is not in the Deployed state.");
            }
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>AnchorableSystem checks the one tile it registers; the other eight are this feature's own check.</summary>
    [Test]
    public async Task AnchoringIsRefusedWithoutAClearFootprint()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(8, 8));

        var wall = EntityUid.Invalid;

        await server.WaitPost(() =>
            wall = entMan.SpawnEntity(Wall, new EntityCoordinates(stack[0], new Vector2(1.5f, 1.5f))));

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(wall).Anchored, Is.True,
                "Precondition: the obstruction is anchored, or TileFree would not see it.");

            var tool = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            var anchor = entMan.SpawnEntity(Anchor, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));

            Assert.That(Wrench(entMan, transform, anchor, tool), Is.False,
                "A wall one tile diagonally out did not block the three by three footprint.");

            entMan.DeleteEntity(wall);

            Assert.That(Wrench(entMan, transform, anchor, tool), Is.True,
                "The footprint was still refused after the obstruction was removed.");
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// AnchorAttemptEvent fires before the eight-second do-after and OnAnchorComplete only re-checks the single tile
    /// the engine registers, so anything moved into the other eight during the delay has to be caught post-hoc.
    /// </summary>
    [Test]
    public async Task FootprintIsRecheckedWhenTheWrenchFinishes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(8, 8));

        // One tick throughout: the anchor is still a dynamic body here, and letting physics run while a wall overlaps
        // its three-by-three fixture would shove it off the tile the test is about.
        await server.WaitAssertion(() =>
        {
            var tool = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            var anchor = entMan.SpawnEntity(Anchor, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));

            var attempt = new AnchorAttemptEvent(tool, tool);
            entMan.EventBus.RaiseLocalEvent(anchor, attempt);

            Assert.That(attempt.Cancelled, Is.False, "Precondition: the footprint was clear when the wrench started.");

            // The obstruction arrives while the tool timer would still be running; WallSolid anchors as it initialises.
            var wall = entMan.SpawnEntity(Wall, new EntityCoordinates(stack[0], new Vector2(1.5f, 1.5f)));
            Assert.That(entMan.GetComponent<TransformComponent>(wall).Anchored, Is.True,
                "Precondition: the obstruction is anchored, or TileFree would not see it.");

            transform.AnchorEntity(anchor);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(anchor).Anchored, Is.False,
                    "The anchor stayed down over a footprint that had filled up during the delay.");
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(anchor).State,
                    Is.EqualTo(WFAnchorState.Loose), "The rejected anchor did not fall back to Loose.");
            }
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>Biome chunks unload roughly ten seconds after the last viewer leaves; a deployed anchor pins its own ground.</summary>
    [Test]
    public async Task DeployedAnchorReservesItsFootprint()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(8, 8));

        await server.WaitAssertion(() =>
        {
            var biome = entMan.GetComponent<BiomeComponent>(stack[0]);

            // Laying the tiles with SetTiles deliberately leaves ModifiedTiles alone, so anything found below was
            // pinned by the anchor rather than by the fixture.
            Assert.That(biome.ModifiedTiles.Values.Any(set => set.Contains(Vector2i.Zero)), Is.False,
                "Precondition: the ground under the anchor is not reserved yet.");

            var tool = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            var anchor = entMan.SpawnEntity(Anchor, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));

            Assert.That(Wrench(entMan, transform, anchor, tool), Is.True, "Precondition: the anchor deployed.");

            var grid = entMan.GetComponent<MapGridComponent>(stack[0]);

            using (Assert.EnterMultipleScope())
            {
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    var index = new Vector2i(dx, dy);

                    Assert.That(biome.ModifiedTiles.Values.Any(set => set.Contains(index)), Is.True,
                        $"Tile {index} of the footprint was not reserved against biome unload.");
                    Assert.That(maps.TryGetTileRef(stack[0], grid, index, out var tile) && !tile.Tile.IsEmpty, Is.True,
                        $"Tile {index} of the footprint is empty.");
                }

                // The bound has to be exact: a padded reserve box pins the ring at radius 2 as well, which is 25
                // tiles of biome per anchor instead of the nine the rig actually stands on.
                for (var dx = -2; dx <= 2; dx++)
                for (var dy = -2; dy <= 2; dy++)
                {
                    if (dx is > -2 and < 2 && dy is > -2 and < 2)
                        continue;

                    var index = new Vector2i(dx, dy);

                    Assert.That(biome.ModifiedTiles.Values.Any(set => set.Contains(index)), Is.False,
                        $"Tile {index} is outside the footprint and should not have been reserved.");
                }
            }
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>The band is what sets the cut radius, so both ends of it are load-bearing.</summary>
    [Test]
    public async Task PairingRespectsTheBand()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(56, 4));

        var log = server.System<WFAnchorTestEventSystem>();
        await server.WaitPost(() => log.Clear());

        foreach (var (distance, shouldPair) in new[] { (10f, false), (16f, true), (28f, true), (40f, true), (48f, false) })
        {
            var a = EntityUid.Invalid;
            var b = EntityUid.Invalid;
            log.Clear();

            await server.WaitAssertion(() =>
            {
                var tool = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
                a = entMan.SpawnEntity(Anchor, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
                b = entMan.SpawnEntity(Anchor, new EntityCoordinates(stack[0], new Vector2(distance + 0.5f, 0.5f)));

                Assert.That(Wrench(entMan, transform, a, tool), Is.True, $"Anchor A refused to deploy at {distance}.");
                Assert.That(Wrench(entMan, transform, b, tool), Is.True, $"Anchor B refused to deploy at {distance}.");

                var compA = entMan.GetComponent<WFGravityAnchorComponent>(a);
                var compB = entMan.GetComponent<WFGravityAnchorComponent>(b);

                using (Assert.EnterMultipleScope())
                {
                    if (shouldPair)
                    {
                        Assert.That(compA.State, Is.EqualTo(WFAnchorState.Paired), $"A did not pair at {distance} tiles.");
                        Assert.That(compB.State, Is.EqualTo(WFAnchorState.Paired), $"B did not pair at {distance} tiles.");
                        Assert.That(compA.Partner, Is.EqualTo(entMan.GetNetEntity(b)), "A points at the wrong partner.");
                        Assert.That(compB.Partner, Is.EqualTo(entMan.GetNetEntity(a)), "B points at the wrong partner.");
                        Assert.That(log.PairsFormed, Has.Count.EqualTo(1), $"Wrong number of pair events at {distance}.");
                        Assert.That(log.PairsFormed[0].Distance, Is.EqualTo(distance).Within(0.01f),
                            "The pair event reported the wrong distance.");
                    }
                    else
                    {
                        Assert.That(compA.State, Is.EqualTo(WFAnchorState.Deployed), $"A paired at {distance} tiles.");
                        Assert.That(compB.State, Is.EqualTo(WFAnchorState.Deployed), $"B paired at {distance} tiles.");
                        Assert.That(compA.Partner, Is.Null, "A took a partner outside the band.");
                        Assert.That(log.PairsFormed, Is.Empty, $"A pair formed at {distance} tiles.");
                    }
                }
            });

            await server.WaitPost(() =>
            {
                entMan.DeleteEntity(a);
                entMan.DeleteEntity(b);
            });

            await server.WaitRunTicks(1);
        }

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>Two crackers must never share a pair, and hand-spawned dev anchors with no owner must still work.</summary>
    [Test]
    public async Task PairingRespectsOwnership()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(32, 4));

        await server.WaitAssertion(() =>
        {
            var tool = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));

            // Two different owners: any two distinct net entities stand in for two hulls.
            var ownerA = entMan.GetNetEntity(stack[1]);
            var ownerB = entMan.GetNetEntity(stack[2]);

            var a = Deploy(entMan, transform, stack[0], tool, 0f, ownerA);
            var b = Deploy(entMan, transform, stack[0], tool, 24f, ownerB);

            Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(a).Partner, Is.Null,
                "Anchors owned by different crackers paired up.");

            entMan.DeleteEntity(b);

            var sameOwner = Deploy(entMan, transform, stack[0], tool, 24f, ownerA);
            Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(a).Partner,
                Is.EqualTo(entMan.GetNetEntity(sameOwner)), "Anchors owned by the same cracker did not pair.");

            entMan.DeleteEntity(a);
            entMan.DeleteEntity(sameOwner);

            var loneA = Deploy(entMan, transform, stack[0], tool, 0f, null);
            var loneB = Deploy(entMan, transform, stack[0], tool, 24f, null);

            Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(loneA).Partner,
                Is.EqualTo(entMan.GetNetEntity(loneB)), "Two unowned dev anchors did not pair.");
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>A pair is a feature of one ground grid; world distance alone is not enough.</summary>
    [Test]
    public async Task PairingRespectsTheGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        var other = await BuildStandalone(pair);

        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(4, 4));
        await LayTiles(pair, other[0], new Vector2i(20, -4), new Vector2i(28, 4));

        await server.WaitAssertion(() =>
        {
            var toolA = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            var toolB = SpawnTool(entMan, WrenchProto, new EntityCoordinates(other[0], new Vector2(24.5f, 0.5f)));

            var here = entMan.SpawnEntity(Anchor, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            var away = entMan.SpawnEntity(Anchor, new EntityCoordinates(other[0], new Vector2(24.5f, 0.5f)));

            Assert.That(Wrench(entMan, transform, here, toolA), Is.True, "Precondition: the first anchor deployed.");
            Assert.That(Wrench(entMan, transform, away, toolB), Is.True, "Precondition: the second anchor deployed.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(here).Partner, Is.Null,
                    "An anchor paired across two separate ground layers.");
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(away).Partner, Is.Null,
                    "An anchor paired across two separate ground layers.");
            }
        });

        await Teardown(pair, stack);
        await Teardown(pair, other);
        await pair.CleanReturnAsync();
    }

    /// <summary>The drill runs unattended and locks on its own deadline.</summary>
    [Test]
    public async Task DrillRunsAndLocks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var examine = server.System<ExamineSystemShared>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(32, 4));

        var log = server.System<WFAnchorTestEventSystem>();
        await server.WaitPost(() => log.Clear());

        var a = EntityUid.Invalid;
        var user = EntityUid.Invalid;
        var started = string.Empty;

        await server.WaitAssertion(() =>
        {
            user = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            a = Deploy(entMan, transform, stack[0], user, 0f, null);
            var b = Deploy(entMan, transform, stack[0], user, 24f, null);

            foreach (var uid in new[] { a, b })
            {
                entMan.GetComponent<WFGravityAnchorComponent>(uid).DrillDuration = ShortDrill;
            }

            Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(a).State, Is.EqualTo(WFAnchorState.Paired),
                "Precondition: the pair formed.");

            Assert.That(TryVerb(entMan, a, user, "wf-anchor-verb-drill"), Is.True, "The start-drill verb was refused.");

            Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(a).State, Is.EqualTo(WFAnchorState.Drilling),
                "The anchor did not start drilling.");
            Assert.That(log.DrillsStarted, Has.Count.EqualTo(1), "WFAnchorDrillStartedEvent was not raised once.");

            started = examine.GetExamineText(a, user).ToString();
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var running = examine.GetExamineText(a, user).ToString();
            var startPercent = Percent(started);
            var runPercent = Percent(running);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(startPercent, Is.Not.Null, "The examine line never reported drill progress.");
                Assert.That(runPercent, Is.GreaterThan(startPercent ?? 0),
                    "The drill examine percentage is not rising.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(a).State, Is.EqualTo(WFAnchorState.Locked),
                    "The drill never locked.");
                Assert.That(log.DrillsFinished, Has.Count.EqualTo(1), "WFAnchorDrillFinishedEvent was not raised once.");
            }
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// F3 only reports the damage condition. The design puts the fifty-percent pause on F4's crack timer, so the drill
    /// here has to finish on the deadline it was given.
    /// </summary>
    [Test]
    public async Task DamageSetsTheFlagAndRaisesTheEvent()
    {
        // Connected, because the networked damage flag has to be read back off the client half.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var damageable = server.System<DamageableSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(32, 4));

        var log = server.System<WFAnchorTestEventSystem>();
        await server.WaitPost(() => log.Clear());

        var a = EntityUid.Invalid;
        var netA = NetEntity.Invalid;

        await server.WaitAssertion(() =>
        {
            var user = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            a = Deploy(entMan, transform, stack[0], user, 0f, null);
            Deploy(entMan, transform, stack[0], user, 24f, null);
            netA = entMan.GetNetEntity(a);

            var comp = entMan.GetComponent<WFGravityAnchorComponent>(a);
            comp.DrillDuration = ShortDrill;

            Assert.That(TryVerb(entMan, a, user, "wf-anchor-verb-drill"), Is.True, "Precondition: the drill started.");

            // Half of the 300 Breakage threshold is the design's damaged line.
            damageable.TryChangeDamage(a, Damage(proto, comp.BreakDamage * comp.DamageFraction + 10f), true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Damaged, Is.True, "The anchor did not flag itself as damaged.");
                Assert.That(log.Damaged, Has.Count.EqualTo(1), "WFAnchorDamagedEvent was not raised.");
                Assert.That(log.Damaged[0].Damaged, Is.True, "WFAnchorDamagedEvent reported the wrong direction.");
                Assert.That(comp.State, Is.EqualTo(WFAnchorState.Drilling), "Damage stopped the drill.");
            }
        });

        await pair.RunTicksSync(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFGravityAnchorComponent>(a);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFAnchorState.Locked),
                    "A damaged drill did not lock on its original deadline; F3 has no drill pause.");
                Assert.That(comp.Damaged, Is.True, "The damage flag cleared itself.");
            }

            Assert.That(pair.Client.EntMan.TryGetEntity(netA, out var clientAnchor), Is.True,
                "The anchor never reached the client.");
            Assert.That(pair.Client.EntMan.GetComponent<WFGravityAnchorComponent>(clientAnchor!.Value).Damaged, Is.True,
                "The damage flag was not networked to the client.");
        });

        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFGravityAnchorComponent>(a);
            damageable.SetAllDamage(a, entMan.GetComponent<DamageableComponent>(a), 0);
            Assert.That(comp.Damaged, Is.False, "Healing did not clear the damage flag.");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(log.Damaged, Has.Count.EqualTo(2), "WFAnchorDamagedEvent did not fire again on healing.");
            Assert.That(log.Damaged[1].Damaged, Is.False, "The healing event reported the wrong direction.");
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// DamageChangedEvent carries only the total, so the component mirrors the prototype's Breakage threshold by hand.
    /// This is the guard on that mirror.
    /// </summary>
    [Test]
    public async Task BreakDamageMatchesThePrototype()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();
        var anchor = EntityUid.Invalid;

        await server.WaitPost(() => anchor = entMan.SpawnEntity(Anchor, map.GridCoords));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFGravityAnchorComponent>(anchor);
            var destructible = entMan.GetComponent<DestructibleComponent>(anchor);

            var breakage = destructible.Thresholds
                .Where(t => t.Behaviors.OfType<DoActsBehavior>().Any(b => b.HasAct(ThresholdActs.Breakage)))
                .Select(t => t.Trigger)
                .OfType<DamageTrigger>()
                .ToList();

            Assert.That(breakage, Has.Count.EqualTo(1), "The anchor should have exactly one Breakage threshold.");
            Assert.That(comp.BreakDamage, Is.EqualTo((float)breakage[0].Damage).Within(0.01f),
                "WFGravityAnchorComponent.BreakDamage no longer mirrors the prototype's Breakage trigger.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Breaking one half drops the pair; only a repair puts it back, and the repair has to be one D9 allows.</summary>
    [Test]
    public async Task BreakingDissolvesThePairAndNeedsARelock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var damageable = server.System<DamageableSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(32, 4));

        var log = server.System<WFAnchorTestEventSystem>();
        await server.WaitPost(() => log.Clear());

        await server.WaitAssertion(() =>
        {
            var user = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            var a = Deploy(entMan, transform, stack[0], user, 0f, null);
            var b = Deploy(entMan, transform, stack[0], user, 24f, null);

            var compA = entMan.GetComponent<WFGravityAnchorComponent>(a);
            var compB = entMan.GetComponent<WFGravityAnchorComponent>(b);

            Assert.That(compA.State, Is.EqualTo(WFAnchorState.Paired), "Precondition: the pair formed.");

            log.Clear();
            damageable.TryChangeDamage(a, Damage(proto, BreakingDamage), true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(compA.State, Is.EqualTo(WFAnchorState.Broken), "The anchor did not break.");
                Assert.That(log.Broken, Has.Count.EqualTo(1), "WFAnchorBrokenEvent was not raised.");
                Assert.That(log.PairsDissolved, Has.Count.EqualTo(1), "WFAnchorPairDissolvedEvent was not raised once.");
                Assert.That(compA.Partner, Is.Null, "The broken half kept its partner.");
                Assert.That(compB.Partner, Is.Null, "The survivor kept its partner.");
                Assert.That(compB.State, Is.EqualTo(WFAnchorState.Deployed), "The survivor was not demoted to Deployed.");
            }

            var repairable = entMan.GetComponent<RepairableComponent>(a);

            Assert.That(repairable.Qualities, Does.Contain("Welding"),
                "The anchor cannot be repaired with a welder; it kept BaseStructure's Applicating-only narrowing.");

            // The welder's do-after is RepairableSystem's; what F3 owns is what happens once it finishes.
            damageable.SetAllDamage(a, entMan.GetComponent<DamageableComponent>(a), 0);
            var repaired = new RepairedEvent((a, repairable), user);
            entMan.EventBus.RaiseLocalEvent(a, ref repaired);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(compA.State, Is.EqualTo(WFAnchorState.Paired), "The repaired anchor did not re-pair.");
                Assert.That(compB.State, Is.EqualTo(WFAnchorState.Paired), "The survivor did not re-pair.");
                Assert.That(compA.Partner, Is.EqualTo(entMan.GetNetEntity(b)), "The repaired anchor took no partner.");
            }
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>The survivor of a destroyed half must not be left pointing at a dead anchor.</summary>
    [Test]
    public async Task DestroyingDissolvesThePair()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(32, 4));

        var log = server.System<WFAnchorTestEventSystem>();
        await server.WaitPost(() => log.Clear());

        var a = EntityUid.Invalid;
        var b = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var user = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            a = Deploy(entMan, transform, stack[0], user, 0f, null);
            b = Deploy(entMan, transform, stack[0], user, 24f, null);

            var comp = entMan.GetComponent<WFGravityAnchorComponent>(a);
            comp.DrillDuration = ShortDrill;

            Assert.That(TryVerb(entMan, a, user, "wf-anchor-verb-drill"), Is.True, "Precondition: the drill started.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(a).State, Is.EqualTo(WFAnchorState.Locked),
                "Precondition: the pair locked.");
            log.Clear();
        });

        await server.WaitPost(() => entMan.QueueDeleteEntity(a));
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFGravityAnchorComponent>(b);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(log.Destroyed, Has.Count.EqualTo(1), "WFAnchorDestroyedEvent was not raised.");
                Assert.That(log.PairsDissolved, Has.Count.EqualTo(1), "WFAnchorPairDissolvedEvent was not raised.");
                Assert.That(comp.Partner, Is.Null, "The survivor still points at the destroyed anchor.");
                Assert.That(comp.State, Is.EqualTo(WFAnchorState.Deployed), "The survivor was not demoted to Deployed.");
            }
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Only a drilled-in anchor is refused. A paired one has to come back up, because pairing is automatic and the
    /// distance between the two is what sets the cut radius.
    /// </summary>
    [Test]
    public async Task UnwrenchingIsRefusedOnlyWhenArmed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(32, 4));

        var a = EntityUid.Invalid;
        var b = EntityUid.Invalid;
        var user = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            user = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            var lone = Deploy(entMan, transform, stack[0], user, 8f, null);

            Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(lone).State, Is.EqualTo(WFAnchorState.Deployed),
                "Precondition: the lone anchor is merely deployed.");
            Assert.That(Unwrench(entMan, transform, lone, user), Is.True, "A deployed anchor refused to come up.");
            entMan.DeleteEntity(lone);

            a = Deploy(entMan, transform, stack[0], user, 0f, null);
            b = Deploy(entMan, transform, stack[0], user, 24f, null);
            entMan.GetComponent<WFGravityAnchorComponent>(a).DrillDuration = ShortDrill;

            Assert.That(TryVerb(entMan, a, user, "wf-anchor-verb-drill"), Is.True, "Precondition: the drill started.");
            Assert.That(Unwrench(entMan, transform, a, user), Is.False, "A drilling anchor was unwrenched.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            var compA = entMan.GetComponent<WFGravityAnchorComponent>(a);

            Assert.That(compA.State, Is.EqualTo(WFAnchorState.Locked), "Precondition: the anchor locked.");
            Assert.That(Unwrench(entMan, transform, a, user), Is.False, "A locked anchor was unwrenched.");

            Assert.That(TryVerb(entMan, a, user, "wf-anchor-verb-off"), Is.True, "Precondition: the anchor switched off.");
            Assert.That(compA.State, Is.EqualTo(WFAnchorState.Off), "Precondition: the anchor is off.");
            Assert.That(Unwrench(entMan, transform, a, user), Is.False, "A switched-off anchor was unwrenched.");

            // Back to a plain pair, which the crew must be able to re-site.
            var compB = entMan.GetComponent<WFGravityAnchorComponent>(b);
            entMan.DeleteEntity(a);

            Assert.That(compB.State, Is.EqualTo(WFAnchorState.Deployed), "Precondition: the survivor was demoted.");

            var c = Deploy(entMan, transform, stack[0], user, 0f, null);
            Assert.That(compB.State, Is.EqualTo(WFAnchorState.Paired), "Precondition: a fresh pair formed.");

            Assert.That(Unwrench(entMan, transform, c, user), Is.True, "A paired anchor could not be re-sited.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(c).State, Is.EqualTo(WFAnchorState.Loose),
                    "The unwrenched half is not loose.");
                Assert.That(compB.State, Is.EqualTo(WFAnchorState.Deployed), "The survivor was not demoted again.");
                Assert.That(compB.Partner, Is.Null, "The survivor kept a partner that is no longer down.");
            }
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>The verb only offers itself on a locked anchor, and F7 gets to veto it through the broadcast attempt.</summary>
    [Test]
    public async Task SwitchOffIsGatedAndCancellable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(32, 4));

        var log = server.System<WFAnchorTestEventSystem>();
        await server.WaitPost(() => log.Clear());

        var a = EntityUid.Invalid;
        var user = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            user = SpawnTool(entMan, WrenchProto, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            a = Deploy(entMan, transform, stack[0], user, 0f, null);
            Deploy(entMan, transform, stack[0], user, 24f, null);
            entMan.GetComponent<WFGravityAnchorComponent>(a).DrillDuration = ShortDrill;

            Assert.That(IsVerbEnabled(entMan, a, user, "wf-anchor-verb-off"), Is.False,
                "Switch off is offered on a paired anchor.");

            Assert.That(TryVerb(entMan, a, user, "wf-anchor-verb-drill"), Is.True, "Precondition: the drill started.");

            Assert.That(IsVerbEnabled(entMan, a, user, "wf-anchor-verb-off"), Is.False,
                "Switch off is offered on a drilling anchor.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFGravityAnchorComponent>(a);
            Assert.That(comp.State, Is.EqualTo(WFAnchorState.Locked), "Precondition: the anchor locked.");

            // The F7 veto hook: the attempt is broadcast and cancellable, so any later system may refuse.
            log.VetoSwitchOff = true;

            Assert.That(TryVerb(entMan, a, user, "wf-anchor-verb-off"), Is.True, "The switch-off verb was not offered.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFAnchorState.Locked), "A cancelled attempt still switched the anchor off.");
                Assert.That(log.SwitchedOff, Is.Empty, "A cancelled attempt still raised WFAnchorSwitchedOffEvent.");
            }

            log.VetoSwitchOff = false;

            Assert.That(TryVerb(entMan, a, user, "wf-anchor-verb-off"), Is.True, "The switch-off verb was not offered.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.State, Is.EqualTo(WFAnchorState.Off), "The anchor did not switch off.");
                Assert.That(log.SwitchedOff, Has.Count.EqualTo(1), "WFAnchorSwitchedOffEvent was not raised once.");
            }
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>The crate obeys the same ground rule as the anchor it holds, and hands its owner on to what it spawns.</summary>
    [Test]
    public async Task CrateUnpacksOnlyOnAGroundLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        var deck = await pair.CreateTestMap();
        await LayTiles(pair, stack[0], new Vector2i(-4, -4), new Vector2i(8, 8));
        var parked = await ParkHull(pair, stack[0], new Vector2(20f, 20f));

        await server.WaitAssertion(() =>
        {
            var tool = SpawnTool(entMan, Crowbar, deck.GridCoords);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Pry(entMan, entMan.SpawnEntity(Crate, deck.GridCoords), tool), Is.False,
                    "A crate was pried open on a plain station grid.");
                Assert.That(Pry(entMan, entMan.SpawnEntity(Crate, new EntityCoordinates(stack[1], Vector2.Zero)), tool), Is.False,
                    "A crate was pried open on an air layer.");
                Assert.That(Pry(entMan, entMan.SpawnEntity(Crate, new EntityCoordinates(parked, new Vector2(2.5f, 2.5f))), tool), Is.False,
                    "A crate was pried open on a hull parked on the ground layer.");
            }
        });

        var crate = EntityUid.Invalid;
        var owner = NetEntity.Invalid;

        await server.WaitPost(() =>
        {
            crate = entMan.SpawnEntity(Crate, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            owner = entMan.GetNetEntity(parked);

            var comp = entMan.GetComponent<WFAnchorCrateComponent>(crate);
            comp.Cracker = owner;
            comp.Delay = 1f;
        });

        await server.WaitAssertion(() =>
        {
            var tool = SpawnTool(entMan, Crowbar, new EntityCoordinates(stack[0], new Vector2(0.5f, 0.5f)));
            Assert.That(Pry(entMan, crate, tool), Is.True, "A crate on clear planet ground refused to open.");
        });

        // The crate's own prying do-after, shortened above, plus slack for the deletion to land.
        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            var anchors = new List<EntityUid>();
            var query = entMan.AllEntityQueryEnumerator<WFGravityAnchorComponent, TransformComponent>();

            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == stack[0])
                    anchors.Add(uid);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.EntityExists(crate) && !entMan.IsQueuedForDeletion(crate), Is.False,
                    "The crate outlived being unpacked.");
                Assert.That(anchors, Has.Count.EqualTo(1), "Unpacking did not leave exactly one anchor behind.");
                Assert.That(entMan.GetComponent<WFGravityAnchorComponent>(anchors[0]).Cracker, Is.EqualTo(owner),
                    "The unpacked anchor did not inherit the crate's owner.");
            }
        });

        await Teardown(pair, stack);
        await pair.CleanReturnAsync();
    }

    /// <summary>Design D21 and the 20-to-44 tile cut diameter of design section 5.</summary>
    [Test]
    public void CutRadiusFollowsD21()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SharedWFGravityAnchorSystem.GetCutRadius(24f, CutPadding), Is.EqualTo(14f).Within(0.001f));
            Assert.That(SharedWFGravityAnchorSystem.GetCutRadius(MaxBand, CutPadding), Is.EqualTo(22f).Within(0.001f));
            Assert.That(SharedWFGravityAnchorSystem.GetCutRadius(MinBand, CutPadding), Is.EqualTo(10f).Within(0.001f));
            Assert.That(SharedWFGravityAnchorSystem.InBand(MinBand, MinBand, MaxBand), Is.True);
            Assert.That(SharedWFGravityAnchorSystem.InBand(MaxBand, MinBand, MaxBand), Is.True);
            Assert.That(SharedWFGravityAnchorSystem.InBand(MinBand - 1f, MinBand, MaxBand), Is.False);
            Assert.That(SharedWFGravityAnchorSystem.InBand(MaxBand + 1f, MinBand, MaxBand), Is.False);
        }
    }

    /// <summary>
    /// The wrench path with the tool timer taken out: AnchorableSystem.Valid raises AnchorAttemptEvent, and if nothing
    /// cancels it OnAnchorComplete calls SharedTransformSystem.AnchorEntity. Both halves run here. The do-after itself is
    /// skipped because AnchorableComponent.Delay is [Access]-locked to AnchorableSystem and an eight-second timer per
    /// anchoring would dominate the fixture.
    /// </summary>
    private static bool Wrench(IEntityManager entMan, SharedTransformSystem transform, EntityUid anchor, EntityUid tool)
    {
        var attempt = new AnchorAttemptEvent(tool, tool);
        entMan.EventBus.RaiseLocalEvent(anchor, attempt);

        if (attempt.Cancelled)
            return false;

        transform.AnchorEntity(anchor);
        return entMan.GetComponent<TransformComponent>(anchor).Anchored;
    }

    /// <summary>The unwrench half of the same path.</summary>
    private static bool Unwrench(IEntityManager entMan, SharedTransformSystem transform, EntityUid anchor, EntityUid tool)
    {
        var attempt = new UnanchorAttemptEvent(tool, tool);
        entMan.EventBus.RaiseLocalEvent(anchor, attempt);

        if (attempt.Cancelled)
            return false;

        transform.Unanchor(anchor, entMan.GetComponent<TransformComponent>(anchor));
        return !entMan.GetComponent<TransformComponent>(anchor).Anchored;
    }

    /// <summary>Spawns an anchor on the ground layer at the given X offset and wrenches it down.</summary>
    private static EntityUid Deploy(
        IEntityManager entMan,
        SharedTransformSystem transform,
        EntityUid ground,
        EntityUid tool,
        float x,
        NetEntity? cracker)
    {
        var anchor = entMan.SpawnEntity(Anchor, new EntityCoordinates(ground, new Vector2(x + 0.5f, 0.5f)));

        if (cracker is { } owner)
            entMan.GetComponent<WFGravityAnchorComponent>(anchor).Cracker = owner;

        Assert.That(Wrench(entMan, transform, anchor, tool), Is.True, $"The anchor at x={x} refused to deploy.");
        return anchor;
    }

    /// <summary>Starts the crate's own prying interaction; true when the tool use took.</summary>
    private static bool Pry(IEntityManager entMan, EntityUid crate, EntityUid tool)
    {
        var xform = entMan.GetComponent<TransformComponent>(crate);
        var ev = new InteractUsingEvent(tool, tool, crate, xform.Coordinates);
        entMan.EventBus.RaiseLocalEvent(crate, ev);
        return ev.Handled;
    }

    /// <summary>Runs the named alternative verb; false when it is not offered or is disabled.</summary>
    private static bool TryVerb(IEntityManager entMan, EntityUid target, EntityUid user, string locId)
    {
        if (FindVerb(entMan, target, user, locId) is not { Disabled: false } verb)
            return false;

        verb.Act?.Invoke();
        return true;
    }

    /// <summary>Whether the named alternative verb is offered and not greyed out.</summary>
    private static bool IsVerbEnabled(IEntityManager entMan, EntityUid target, EntityUid user, string locId)
    {
        return FindVerb(entMan, target, user, locId) is { Disabled: false };
    }

    /// <summary>
    /// The alternative verb whose text matches the given locale id, if the anchor offered one. force is on because the
    /// user here is the tool itself rather than a mob that passes the action blocker's complex-interaction check.
    /// </summary>
    private static Verb? FindVerb(IEntityManager entMan, EntityUid target, EntityUid user, string locId)
    {
        var verbs = entMan.System<SharedVerbSystem>().GetLocalVerbs(target, user, typeof(AlternativeVerb), true);
        var text = IoCManager.Resolve<ILocalizationManager>().GetString(locId);
        return verbs.FirstOrDefault(v => v.Text == text);
    }

    /// <summary>
    /// Spawns a tool that is also its own do-after user. SharedToolSystem only demands a hand when the tool and the
    /// user differ, so this needs no mob and no session - only the DoAfterComponent the user side looks up.
    /// </summary>
    private static EntityUid SpawnTool(IEntityManager entMan, string proto, EntityCoordinates coords)
    {
        var tool = entMan.SpawnEntity(proto, coords);
        entMan.EnsureComponent<DoAfterComponent>(tool);
        return tool;
    }

    /// <summary>Structural damage that the anchor's modifier set cannot soak, so the numbers land where they are aimed.</summary>
    private static DamageSpecifier Damage(IPrototypeManager proto, float amount)
    {
        return new DamageSpecifier(proto.Index<DamageTypePrototype>(Blunt), FixedPoint2.New(amount));
    }

    /// <summary>The percentage out of a drill examine line, or null when the line is absent.</summary>
    private static int? Percent(string examine)
    {
        var match = DrillPercent.Match(examine);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    /// <summary>A small hull parked on the ground layer: the cargo bay the ground rule has to refuse.</summary>
    private static async Task<EntityUid> ParkHull(TestPair pair, EntityUid ground, Vector2 position)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var transform = server.System<SharedTransformSystem>();
        var hull = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var mapId = entMan.GetComponent<MapComponent>(ground).MapId;
            var grid = mapMan.CreateGridEntity(mapId);
            var floor = new Tile(tileDefs[GroundTile].TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            maps.SetTiles(grid.Owner, grid.Comp, tiles);
            transform.SetLocalPosition(grid.Owner, position);
            hull = grid.Owner;
        });

        await server.WaitRunTicks(1);
        return hull;
    }
}

/// <summary>
/// Records every broadcast event the gravity anchor announces, and stands in for F7's switch-off veto. The event bus
/// locks its subscriptions once the server has started, so a test that wants these has to be a registered system.
/// </summary>
public sealed class WFAnchorTestEventSystem : EntitySystem
{
    /// <summary>Every pair that formed since the last Clear.</summary>
    public readonly List<WFAnchorPairFormedEvent> PairsFormed = new();

    /// <summary>Every pair that dissolved since the last Clear.</summary>
    public readonly List<WFAnchorPairDissolvedEvent> PairsDissolved = new();

    /// <summary>Every drill that started since the last Clear.</summary>
    public readonly List<WFAnchorDrillStartedEvent> DrillsStarted = new();

    /// <summary>Every drill that finished since the last Clear.</summary>
    public readonly List<WFAnchorDrillFinishedEvent> DrillsFinished = new();

    /// <summary>Every crossing of the damage threshold since the last Clear, in either direction.</summary>
    public readonly List<WFAnchorDamagedEvent> Damaged = new();

    /// <summary>Every anchor that broke since the last Clear.</summary>
    public readonly List<WFAnchorBrokenEvent> Broken = new();

    /// <summary>Every anchor that terminated since the last Clear.</summary>
    public readonly List<WFAnchorDestroyedEvent> Destroyed = new();

    /// <summary>Every anchor that switched off since the last Clear.</summary>
    public readonly List<WFAnchorSwitchedOffEvent> SwitchedOff = new();

    /// <summary>Every crack stage change since the last Clear.</summary>
    public readonly List<WFCrackStateChangedEvent> StateChanges = new();

    /// <summary>Every crack that finished cutting since the last Clear; the F5 extraction hook.</summary>
    public readonly List<WFCrackCompletedEvent> CracksCompleted = new();

    /// <summary>Every hull pushed into a fall since the last Clear; the F5 chunk hook.</summary>
    public readonly List<WFCrackerFallingEvent> Falling = new();

    /// <summary>Every disc cut free since the last Clear; the F6 hook.</summary>
    public readonly List<WFChunkExtractedEvent> ChunksExtracted = new();

    /// <summary>Every chunk pushed into transit since the last Clear; the F7 hook.</summary>
    public readonly List<WFChunkDroppedEvent> ChunksDropped = new();

    /// <summary>Every planet flagged cracked since the last Clear; the F9 hook.</summary>
    public readonly List<WFPlanetCrackedEvent> PlanetsCracked = new();

    /// <summary>Every switched-off anchor put back to Locked since the last Clear; the F7 lapsed-window edge.</summary>
    public readonly List<WFAnchorReArmedEvent> ReArmed = new();

    /// <summary>Every hull whose evacuation ran out since the last Clear; the F7 release hook.</summary>
    public readonly List<WFCrackerReleasingEvent> Releasing = new();

    /// <summary>Every chunk that settled on the ground layer since the last Clear; the F7 landing hook.</summary>
    public readonly List<WFChunkLandedEvent> ChunksLanded = new();

    /// <summary>While true, every switch-off attempt is refused, exactly as a later feature's own veto would.</summary>
    public bool VetoSwitchOff;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFAnchorPairFormedEvent>((ref WFAnchorPairFormedEvent ev) => PairsFormed.Add(ev));
        SubscribeLocalEvent<WFAnchorPairDissolvedEvent>((ref WFAnchorPairDissolvedEvent ev) => PairsDissolved.Add(ev));
        SubscribeLocalEvent<WFAnchorDrillStartedEvent>((ref WFAnchorDrillStartedEvent ev) => DrillsStarted.Add(ev));
        SubscribeLocalEvent<WFAnchorDrillFinishedEvent>((ref WFAnchorDrillFinishedEvent ev) => DrillsFinished.Add(ev));
        SubscribeLocalEvent<WFAnchorDamagedEvent>((ref WFAnchorDamagedEvent ev) => Damaged.Add(ev));
        SubscribeLocalEvent<WFAnchorBrokenEvent>((ref WFAnchorBrokenEvent ev) => Broken.Add(ev));
        SubscribeLocalEvent<WFAnchorDestroyedEvent>((ref WFAnchorDestroyedEvent ev) => Destroyed.Add(ev));
        SubscribeLocalEvent<WFAnchorSwitchedOffEvent>((ref WFAnchorSwitchedOffEvent ev) => SwitchedOff.Add(ev));
        SubscribeLocalEvent<WFAnchorSwitchOffAttemptEvent>(OnSwitchOffAttempt);

        // F4's three are broadcast [ByRefEvent] record structs too, so they belong on this recorder rather than on a
        // second system competing for the same broadcast subscriptions.
        SubscribeLocalEvent<WFCrackStateChangedEvent>((ref WFCrackStateChangedEvent ev) => StateChanges.Add(ev));
        SubscribeLocalEvent<WFCrackCompletedEvent>((ref WFCrackCompletedEvent ev) => CracksCompleted.Add(ev));
        SubscribeLocalEvent<WFCrackerFallingEvent>((ref WFCrackerFallingEvent ev) => Falling.Add(ev));

        // F5's three are the same shape again, and the bus locks its subscriptions once the server has started, so a
        // second recorder system competing for these broadcasts is not an option.
        SubscribeLocalEvent<WFChunkExtractedEvent>((ref WFChunkExtractedEvent ev) => ChunksExtracted.Add(ev));
        SubscribeLocalEvent<WFChunkDroppedEvent>((ref WFChunkDroppedEvent ev) => ChunksDropped.Add(ev));
        SubscribeLocalEvent<WFPlanetCrackedEvent>((ref WFPlanetCrackedEvent ev) => PlanetsCracked.Add(ev));

        // F7's three, on the same recorder for the same reason: the bus locks its subscriptions once the server has
        // started, so a second system competing for these broadcasts is not an option.
        SubscribeLocalEvent<WFAnchorReArmedEvent>((ref WFAnchorReArmedEvent ev) => ReArmed.Add(ev));
        SubscribeLocalEvent<WFCrackerReleasingEvent>((ref WFCrackerReleasingEvent ev) => Releasing.Add(ev));
        SubscribeLocalEvent<WFChunkLandedEvent>((ref WFChunkLandedEvent ev) => ChunksLanded.Add(ev));
    }

    /// <summary>Forgets everything recorded so far and lifts the veto.</summary>
    public void Clear()
    {
        PairsFormed.Clear();
        PairsDissolved.Clear();
        DrillsStarted.Clear();
        DrillsFinished.Clear();
        Damaged.Clear();
        Broken.Clear();
        Destroyed.Clear();
        SwitchedOff.Clear();
        StateChanges.Clear();
        CracksCompleted.Clear();
        Falling.Clear();
        ChunksExtracted.Clear();
        ChunksDropped.Clear();
        PlanetsCracked.Clear();
        ReArmed.Clear();
        Releasing.Clear();
        ChunksLanded.Clear();
        VetoSwitchOff = false;
    }

    /// <summary>The stand-in for F7's disconnect window.</summary>
    private void OnSwitchOffAttempt(WFAnchorSwitchOffAttemptEvent args)
    {
        if (!VetoSwitchOff)
            return;

        args.Reason = "test";
        args.Cancel();
    }
}
