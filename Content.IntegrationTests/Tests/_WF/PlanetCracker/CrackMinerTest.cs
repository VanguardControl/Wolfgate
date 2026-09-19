#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.PlanetCracker.Mining;
using Content.Server.PowerCell;
using Content.Server.Stack;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Mining;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mining;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// F6's crack miner end to end: where it may be wrenched down and where it may not, how fast it cuts and what it drops,
/// how a seam runs out, what a cell buys and what happens when it empties, the two cells the slot has to refuse, and the
/// sprite faces the whole machine is read through.
/// Almost everything here runs on a FAKE chunk marker laid on the planet's own ground grid by
/// <see cref="PlanetCrackerFixture.BuildMinerSite"/>, deliberately, so a break in F5's extraction cannot masquerade as a
/// break in F6. <see cref="RidesUpAndMinesOnARealChunk"/> is the one test that goes through a real cut.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCrackMinerSystem))]
public sealed class CrackMinerTest
{
    /// <summary>
    /// A deep vein whose whitelist is the deck plating every planet cracker fixture lays by hand.
    /// WFDeepVein's shipped AllowedTiles are { FloorPlanetGrass, FloorPlanetDirt } and WFDeepVeinSystem.OnMapInit
    /// QueueDels anything sitting off them, so the stock prototype cannot be hand-placed on a fixture site at all.
    /// Everything else - the ore roll, the yield, the rate, Remaining - is inherited and is still stamped from the
    /// ground layer's own vein table.
    /// </summary>
    [TestPrototypes]
    public const string Prototypes = @"
- type: entity
  id: WFCrackMinerTestVein
  parent: WFDeepVein
  categories: [ HideSpawnMenu ]
  components:
  - type: WFDeepVein
    allowedTiles: [ FloorSteel ]
";

    /// <summary>A self-recharging cell, which the slot has to refuse or the miner becomes an infinite power source.</summary>
    private const string SelfChargingCell = "PowerCellMicroreactor";

    /// <summary>The tile the fixture site's seam sits on.</summary>
    private static readonly Vector2i VeinTile = new(4, 4);

    /// <summary>A tile of the same site with nothing under it.</summary>
    private static readonly Vector2i BareTile = new(7, 4);

    /// <summary>Where the second seam goes for the move test; clear of the first and still hand-laid deck.</summary>
    private static readonly Vector2i SecondVeinTile = new(4, 8);

    /// <summary>The miner's own RSI, as mining.yml names it.</summary>
    private const string MinerRsi = "_WF/PlanetCracker/Structures/crack_miner.rsi";

    /// <summary>Every state the miner's sprite layers and its visualiser table name between them.</summary>
    private static readonly string[] MinerStates =
    {
        "idle",
        "mining",
        "exhausted",
        "broken",
        "mining-unshaded",
    };

    /// <summary>Blunt, which StructuralMetallic passes through at a coefficient of one and a flat ten off the top.</summary>
    private const string Blunt = "Blunt";

    /// <summary>Enough blunt in one blow to clear the Breakage threshold at 200 and stay well under Destruction at 400.</summary>
    private const float BreakingDamage = 250f;

    /// <summary>The two ores the move test pins the seams to, so the switch is visible in what lands on the ground.</summary>
    private const string FirstOre = "OreSteel";

    private const string SecondOre = "OreGold";

    /// <summary>
    /// The chunk half of the gate. A miner standing on a grid that is not a cut chunk - here the planet's own ground,
    /// with no marker on it - is refused before the wrench's do-after ever starts.
    /// </summary>
    [Test]
    public async Task RefusesToAnchorOffAChunk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        // Deliberately NOT BuildMinerSite: this site never gets the fake chunk marker.
        var site = await BuildCrackerInOrbit(pair);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFPlanetChunkComponent>(site.Ground), Is.False,
                "Precondition: the planet's ground layer is not a chunk.");

            var miner = entMan.SpawnEntity(MinerProto, Centre(site.Ground, VeinTile));
            var attempt = new AnchorAttemptEvent(miner, miner);

            entMan.EventBus.RaiseLocalEvent(miner, attempt);

            Assert.That(attempt.Cancelled, Is.True,
                "A miner was allowed to wrench down on a grid that is not a cut chunk.");
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>The seam half of the gate: the right grid, the wrong tile.</summary>
    [Test]
    public async Task RefusesToAnchorWithoutAVein()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var (site, _) = await BuildMinerSite(pair);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFPlanetChunkComponent>(site.Ground), Is.True,
                "Precondition: the site's ground grid wears the chunk marker.");

            var miner = entMan.SpawnEntity(MinerProto, Centre(site.Ground, BareTile));
            var attempt = new AnchorAttemptEvent(miner, miner);

            entMan.EventBus.RaiseLocalEvent(miner, attempt);

            Assert.That(attempt.Cancelled, Is.True,
                "A miner was allowed to wrench down on a chunk tile with no seam under it.");
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>And the case both halves exist for.</summary>
    [Test]
    public async Task AnchorsOverAVein()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        var (site, _) = await BuildMinerSite(pair);

        await server.WaitAssertion(() =>
        {
            var miner = entMan.SpawnEntity(MinerProto, Centre(site.Ground, VeinTile));
            var attempt = new AnchorAttemptEvent(miner, miner);

            entMan.EventBus.RaiseLocalEvent(miner, attempt);

            Assert.That(attempt.Cancelled, Is.False,
                "The gate refused a miner standing on a chunk tile with a seam under it.");

            transform.AnchorEntity(miner);

            Assert.That(entMan.GetComponent<TransformComponent>(miner).Anchored, Is.True,
                "The miner did not stay down over its seam.");
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Straight off the prototype, with no site at all. Both overrides are load-bearing and both are one line of YAML:
    /// without `anchored: false` every spawned miner would arrive already anchored and skip the gate entirely, and
    /// without `bodyType: Dynamic` the design's "unwrench it and move it to the next seam" would be unreachable.
    /// </summary>
    [Test]
    public async Task SpawnsUnanchoredAndDynamic()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var miner = entMan.SpawnEntity(MinerProto, new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(miner).Anchored, Is.False,
                    "The miner spawned anchored, so it would never raise AnchorAttemptEvent at all.");
                Assert.That(entMan.GetComponent<PhysicsComponent>(miner).BodyType, Is.EqualTo(BodyType.Dynamic),
                    "The miner spawned as a static body, which no player can move.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The player-reachable move path. PullingSystem refuses a static body outright, so a code-driven SetCoordinates
    /// move would hide exactly the trap this exists to catch.
    /// </summary>
    [Test]
    public async Task CanBePulledWhenUnanchored()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        var (site, _) = await BuildMinerSite(pair);
        var miner = await PlaceMiner(pair, site.Ground, VeinTile);

        await server.WaitAssertion(() =>
        {
            transform.Unanchor(miner, entMan.GetComponent<TransformComponent>(miner));

            var pulling = server.System<PullingSystem>();
            var player = entMan.SpawnEntity(ViewerProto, Centre(site.Ground, VeinTile + new Vector2i(1, 0)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<PhysicsComponent>(miner).BodyType, Is.EqualTo(BodyType.Dynamic),
                    "An unwrenched miner is still a static body, so nothing can drag it anywhere.");
                Assert.That(pulling.CanPull(player, miner), Is.True, "Nobody may pull an unwrenched miner.");
                Assert.That(pulling.TryStartPull(player, miner), Is.True, "The pull was refused outright.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A minute of cutting at the seam's own rate. Rate is ore per MINUTE, so a 150 seam under a one-second tick has to
    /// give up 150 units in sixty seconds - the fractional carry is what makes that exact for any rate - and every unit
    /// that left the seam has to be either on the ground or still in the miner's buffer.
    /// </summary>
    [Test]
    public async Task MinesAtTheVeinRate()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var (site, vein) = await BuildMinerSite(pair);
        var miner = await PlaceMiner(pair, site.Ground, VeinTile);
        var before = 0;
        var rate = 0f;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);

            before = comp.Remaining;
            rate = comp.Rate;

            Assert.That(rate, Is.GreaterThan(0f), "Precondition: the seam has a rate to cut at.");
            Assert.That(before, Is.GreaterThan((int)rate * 2),
                "Precondition: the seam is deep enough that a minute of cutting cannot exhaust it.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(60f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);
            var minerComp = entMan.GetComponent<WFCrackMinerComponent>(miner);
            var ore = OreOnGrid(entMan, site.Ground, OreEntity(pair, comp));
            var produced = ore + minerComp.Buffer;

            using (Assert.EnterMultipleScope())
            {
                // One batch of slack: sixty seconds of ticks cannot be aligned exactly on the miner's own beat.
                Assert.That(produced, Is.EqualTo((int)rate).Within(minerComp.BatchSize),
                    $"A minute over a {rate}/min seam produced {produced} units.");
                Assert.That(before - comp.Remaining, Is.EqualTo(produced),
                    "What the seam lost and what the miner made are different numbers.");
                Assert.That(ore, Is.GreaterThan(0), "A whole minute of cutting dropped nothing on the ground at all.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// What lands on the ground is the seam's own ore, as stacks, in the single-count form. Spawning the "Full" parent
    /// of an ore entity instead would hand out fifty units apiece, because StackComponent.Count defaults to 50 here.
    /// </summary>
    [Test]
    public async Task OutputIsStacksOfTheVeinsOre()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var (site, vein) = await BuildMinerSite(pair);

        await PlaceMiner(pair, site.Ground, VeinTile);
        await server.WaitRunTicks(pair.SecondsToTicks(25f));

        await server.WaitAssertion(() =>
        {
            var stacks = server.System<StackSystem>();
            var expected = OreEntity(pair, entMan.GetComponent<WFDeepVeinComponent>(vein));
            var dropped = Dropped(entMan, site.Ground, expected);

            Assert.That(dropped, Is.Not.Empty, "Nothing was dropped, so there is nothing here to check.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var uid in dropped)
                {
                    Assert.That(entMan.TryGetComponent(uid, out StackComponent? stack), Is.True,
                        $"{entMan.ToPrettyString(uid)} was dropped without a StackComponent.");
                    Assert.That(stack!.Count, Is.GreaterThan(0), "A zero-count stack was dropped.");
                    Assert.That(stack.Count, Is.LessThanOrEqualTo(stacks.GetMaxCount(uid)),
                        $"{entMan.ToPrettyString(uid)} holds more than its stack prototype's maximum.");
                }

                // Anything else of the miner's making would show up here as a second stackable id on the same grid.
                Assert.That(StackIds(entMan, site.Ground), Is.EquivalentTo(new[] { expected }),
                    "The miner dropped something other than the seam's own single-count ore entity.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Depletion, never deletion: the seam runs down to nothing, gives up exactly what it had left, wears the exhausted
    /// face and is still there afterwards. Deleting it would dangle a NetEntity id in every surveyed player's set.
    /// </summary>
    [Test]
    public async Task ExhaustsAndStops()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        const int left = 30;

        var (site, vein) = await BuildMinerSite(pair);
        var miner = await PlaceMiner(pair, site.Ground, VeinTile);

        // Remaining is a plain [DataField] with no [Access], which is the whole reason it can be pinned here. The
        // miner's own buffer is zeroed in the same breath so the count below is exactly what this seam gave up.
        await server.WaitPost(() =>
        {
            var minerComp = entMan.GetComponent<WFCrackMinerComponent>(miner);

            minerComp.Buffer = 0;
            minerComp.BufferedOre = null;
            minerComp.Carry = 0f;

            entMan.GetComponent<WFDeepVeinComponent>(vein).Remaining = left;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(20f));

        var ore = 0;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);

            ore = OreOnGrid(entMan, site.Ground, OreEntity(pair, comp));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.EntityExists(vein), Is.True,
                    "The spent seam was deleted; every surveyed player's Revealed set now holds a dangling id.");
                Assert.That(comp.Remaining, Is.Zero, "The seam did not drain to nothing.");
                Assert.That(ore, Is.EqualTo(left), "A seam with thirty units left gave up something other than thirty.");
                Assert.That(entMan.GetComponent<WFCrackMinerComponent>(miner).Buffer, Is.Zero,
                    "The last partial batch was never flushed.");
                Assert.That(State(pair, miner), Is.EqualTo(WFCrackMinerState.Exhausted),
                    "The miner over a spent seam is not wearing the exhausted face.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
            Assert.That(OreOnGrid(entMan, site.Ground, OreEntity(pair, entMan.GetComponent<WFDeepVeinComponent>(vein))),
                Is.EqualTo(ore), "A spent seam went on producing."));

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The cell is the whole power story (design D15): no APC, no cable, one charge taken per unit of work. Five joules
    /// buys five ticks and the output stops on the same tick the cell empties.
    /// </summary>
    [Test]
    public async Task StopsWhenTheCellEmpties()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        const float joules = 5f;

        var (site, vein) = await BuildMinerSite(pair);
        var miner = await PlaceMiner(pair, site.Ground, VeinTile, MinerEmptyProto);
        var before = 0;

        await server.WaitAssertion(() =>
        {
            before = entMan.GetComponent<WFDeepVeinComponent>(vein).Remaining;

            Assert.That(entMan.GetComponent<WFDeepVeinComponent>(vein).TotalYield, Is.EqualTo(before),
                "Precondition: a miner with no cell in its bay had already cut something.");
        });

        await SeatCell(pair, miner, joules);
        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);
            var minerComp = entMan.GetComponent<WFCrackMinerComponent>(miner);
            var cut = before - comp.Remaining;
            var ticks = (int)(joules / minerComp.DrawRate);
            var expected = (int)(comp.Rate * (float)minerComp.Interval.TotalSeconds / 60f * ticks);

            Assert.That(server.System<PowerCellSystem>().TryGetBatteryFromSlot(miner, out var battery), Is.True,
                "The seated cell is no longer in the bay.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(battery!.CurrentCharge, Is.EqualTo(0f).Within(0.001f),
                    "Ten seconds of one-joule draws did not empty a five-joule cell.");
                Assert.That(cut, Is.EqualTo(expected).Within(1),
                    $"A {joules} J cell bought something other than {ticks} ticks of cutting.");
                Assert.That(State(pair, miner), Is.EqualTo(WFCrackMinerState.Idle),
                    "A miner with a flat cell is not idle.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>The other half of D15: swap the flat cell for a live one and the machine picks up where it left off.</summary>
    [Test]
    public async Task ResumesOnCellSwap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var (site, vein) = await BuildMinerSite(pair);
        var miner = await PlaceMiner(pair, site.Ground, VeinTile, MinerEmptyProto);

        await SeatCell(pair, miner, 5f);
        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        var stalled = 0;

        await server.WaitAssertion(() =>
        {
            stalled = entMan.GetComponent<WFDeepVeinComponent>(vein).Remaining;

            Assert.That(State(pair, miner), Is.EqualTo(WFCrackMinerState.Idle),
                "Precondition: the miner stalled on a flat cell.");
        });

        // The helper ejects the flat one first; a silent refusal here would read as a miner that simply never restarted.
        await SeatCell(pair, miner, 1000f);
        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<WFDeepVeinComponent>(vein).Remaining, Is.LessThan(stalled),
                    "A fresh cell did not restart the cutting.");
                Assert.That(State(pair, miner), Is.EqualTo(WFCrackMinerState.Mining),
                    "The miner did not go back to cutting on a fresh cell.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The refusal lives on the slot, not on the tick. A PowerCellMicroreactor self-recharges at twelve joules a second
    /// against the miner's one, so a miner that accepted one could never run out of power and the whole swap loop -
    /// and every number in the power table - would be dead.
    /// </summary>
    [Test]
    public async Task RefusesASelfRechargingCell()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var (site, vein) = await BuildMinerSite(pair);
        var miner = await PlaceMiner(pair, site.Ground, VeinTile, MinerEmptyProto);
        var before = 0;

        await server.WaitAssertion(() =>
        {
            var slots = server.System<ItemSlotsSystem>();
            var cell = entMan.SpawnEntity(SelfChargingCell, entMan.GetComponent<TransformComponent>(miner).Coordinates);

            before = entMan.GetComponent<WFDeepVeinComponent>(vein).Remaining;

            Assert.That(slots.TryInsert(miner, CellSlot, cell, null), Is.False,
                "The bay took a self-recharging cell, which would make the miner an infinite power source.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Remaining, Is.EqualTo(before), "A miner with no cell in its bay cut rock anyway.");
                Assert.That(OreOnGrid(entMan, site.Ground, OreEntity(pair, comp)), Is.Zero,
                    "A miner with no cell in its bay dropped ore anyway.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// AutoGenerateComponentPause repairs the timer across a pause; it does not stop the work. The Update loop's own
    /// Paused check is what does, and without it a miner on a paused map would burn its cell and drain its seam for the
    /// whole pause and then stall for exactly as long afterwards.
    /// </summary>
    [Test]
    public async Task PausedChunkDoesNotMine()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        var (site, vein) = await BuildMinerSite(pair);
        var miner = await PlaceMiner(pair, site.Ground, VeinTile);

        // Two seconds of running first, so the assertion is about the pause and not about a miner that never started.
        await server.WaitRunTicks(pair.SecondsToTicks(2f));
        await server.WaitPost(() => maps.SetPaused(new Entity<MapComponent?>(site.Ground, null), true));

        var remaining = 0;
        var ore = 0;
        var charge = 0f;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);

            remaining = comp.Remaining;
            ore = OreOnGrid(entMan, site.Ground, OreEntity(pair, comp));
            charge = Charge(pair, miner);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.TotalYield - remaining, Is.GreaterThan(0),
                    "Precondition: the miner was actually running before the pause.");
                Assert.That(charge, Is.GreaterThan(0f), "Precondition: the miner still has charge left to burn.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Remaining, Is.EqualTo(remaining), "A paused miner went on draining its seam.");
                Assert.That(OreOnGrid(entMan, site.Ground, OreEntity(pair, comp)), Is.EqualTo(ore),
                    "A paused miner went on dropping ore.");
                Assert.That(Charge(pair, miner), Is.EqualTo(charge).Within(0.001f),
                    "A paused miner went on burning charge.");
            }
        });

        await server.WaitPost(() => maps.SetPaused(new Entity<MapComponent?>(site.Ground, null), false));

        // One Interval plus a tick of slack: the unpause hands NextTick its pause back, it does not owe a burst.
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFDeepVeinComponent>(vein).Remaining, Is.LessThan(remaining),
                "The miner did not pick up again within an interval of the unpause."));

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// D15's move, driven the way a player would: unwrench, drag it a few tiles, wrench it down on the next seam. The
    /// two seams' ores are pinned apart first, so the switch is visible in what lands on the ground and not only in a
    /// counter.
    /// </summary>
    [Test]
    public async Task MovesToAnotherVein()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        var (site, first) = await BuildMinerSite(pair);
        var second = await PlaceVein(pair, site.Ground, SecondVeinTile);

        var firstOre = string.Empty;
        var secondOre = string.Empty;

        // The roll is deterministic from the seed and the tile index, so two tiles can legitimately land on the same
        // ore. Ore is a plain stamp MapInit has already finished with, so it is simply written apart here.
        await server.WaitAssertion(() =>
        {
            var a = entMan.GetComponent<WFDeepVeinComponent>(first);
            var b = entMan.GetComponent<WFDeepVeinComponent>(second);

            a.Ore = FirstOre;
            b.Ore = SecondOre;
            entMan.Dirty(first, a);
            entMan.Dirty(second, b);

            firstOre = OreEntity(pair, a);
            secondOre = OreEntity(pair, b);
        });

        var miner = await PlaceMiner(pair, site.Ground, VeinTile);

        await server.WaitRunTicks(pair.SecondsToTicks(12f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(OreOnGrid(entMan, site.Ground, firstOre), Is.GreaterThan(0),
                    "Precondition: the miner was cutting the first seam.");
                Assert.That(OreOnGrid(entMan, site.Ground, secondOre), Is.Zero,
                    "Precondition: nothing had touched the second seam yet.");
            }
        });

        var firstLeft = 0;
        var secondLeft = 0;

        // The unwrench and the reading are one post: a tick between them is a tick the miner is still cutting in.
        await server.WaitPost(() =>
        {
            transform.Unanchor(miner, entMan.GetComponent<TransformComponent>(miner));
            transform.SetCoordinates(miner, Centre(site.Ground, SecondVeinTile));

            firstLeft = entMan.GetComponent<WFDeepVeinComponent>(first).Remaining;
            secondLeft = entMan.GetComponent<WFDeepVeinComponent>(second).Remaining;
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var attempt = new AnchorAttemptEvent(miner, miner);
            entMan.EventBus.RaiseLocalEvent(miner, attempt);

            Assert.That(attempt.Cancelled, Is.False, "The gate refused the second seam's own tile.");

            transform.AnchorEntity(miner);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(12f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(OreOnGrid(entMan, site.Ground, secondOre), Is.GreaterThan(0),
                    "The moved miner never produced the second seam's ore.");
                Assert.That(entMan.GetComponent<WFDeepVeinComponent>(second).Remaining, Is.LessThan(secondLeft),
                    "The second seam never lost anything.");
                Assert.That(entMan.GetComponent<WFDeepVeinComponent>(first).Remaining, Is.EqualTo(firstLeft),
                    "The abandoned seam went on draining after the miner left it.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A chunk pushed into transit is on its way to the ground with sixty seconds of evacuation alarm behind it, and F7
    /// deletes the grid after the crash. The miner has to stop before any of that rather than spray stacks onto a
    /// falling disc - and it has to drop the batch it is already holding on the way out.
    /// </summary>
    [Test]
    public async Task StopsWhenTheChunkDrops()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        var (site, vein) = await BuildMinerSite(pair);
        var miner = await PlaceMiner(pair, site.Ground, VeinTile);

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        var remaining = 0;
        var expected = 0;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);

            Assert.That(State(pair, miner), Is.EqualTo(WFCrackMinerState.Mining),
                "Precondition: the miner was cutting before the chunk dropped.");

            // Only the flag: a real DropChunk would take the whole grid into transit and the fixture site with it.
            entMan.GetComponent<WFPlanetChunkComponent>(site.Ground).Dropped = true;

            remaining = comp.Remaining;
            expected = OreOnGrid(entMan, site.Ground, OreEntity(pair, comp))
                + entMan.GetComponent<WFCrackMinerComponent>(miner).Buffer;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(State(pair, miner), Is.EqualTo(WFCrackMinerState.Idle),
                    "A miner on a falling chunk did not fall back to idle.");
                Assert.That(comp.Remaining, Is.EqualTo(remaining), "A miner on a falling chunk went on cutting.");
                Assert.That(OreOnGrid(entMan, site.Ground, OreEntity(pair, comp)), Is.EqualTo(expected),
                    "A miner on a falling chunk dropped more than the batch it was already holding.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every face the sprite table names, driven through the machine rather than written onto it. Broken is reached the
    /// only way a player can reach it, through the Destructible Breakage threshold mining.yml spells out at 200.
    /// </summary>
    [Test]
    public async Task VisualStateKeys()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        var (site, vein) = await BuildMinerSite(pair);
        var miner = EntityUid.Invalid;

        await server.WaitPost(() => miner = entMan.SpawnEntity(MinerProto, Centre(site.Ground, VeinTile)));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
            Assert.That(State(pair, miner), Is.EqualTo(WFCrackMinerState.Idle),
                "A loose miner is not wearing the idle face."));

        await server.WaitPost(() => server.System<SharedTransformSystem>().AnchorEntity(miner));
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
            Assert.That(State(pair, miner), Is.EqualTo(WFCrackMinerState.Mining),
                "A wrenched-down miner over a live seam is not wearing the cutting face."));

        await server.WaitPost(() => entMan.GetComponent<WFDeepVeinComponent>(vein).Remaining = 0);
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
            Assert.That(State(pair, miner), Is.EqualTo(WFCrackMinerState.Exhausted),
                "A miner over a spent seam is not wearing the exhausted face."));

        await server.WaitPost(() => server.System<DamageableSystem>()
            .TryChangeDamage(miner, Damage(proto, BreakingDamage), true));

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.EntityExists(miner), Is.True,
                    "The breaking blow destroyed the miner outright, so the Breakage threshold is unreachable.");
                Assert.That(State(pair, miner), Is.EqualTo(WFCrackMinerState.Broken),
                    "A broken miner is not wearing the wrecked face.");
            }
        });

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The RSI half. A state name the generated meta.json does not carry only shows up as a missing sprite at runtime,
    /// and the GenericVisualizer table names four of these as bare strings that nothing else walks.
    /// </summary>
    [Test]
    public async Task EveryMinerStateHasAnRsiState()
    {
        // Connected, because the sprite layers only exist on the client half.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var client = pair.Client;
        var clientEntMan = client.EntMan;

        var map = await pair.CreateTestMap();
        var miner = EntityUid.Invalid;

        await server.WaitPost(() => miner = entMan.SpawnEntity(MinerProto,
            new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f))));

        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var uid = pair.ToClientUid(miner);

            Assert.That(clientEntMan.TryGetComponent(uid, out SpriteComponent? sprite), Is.True,
                "The miner never reached the client with a sprite.");

            var rsi = sprite!.AllLayers.Select(layer => layer.ActualRsi).FirstOrDefault(actual => actual != null);

            Assert.That(rsi, Is.Not.Null, $"The miner's sprite resolves no RSI at all; it should be {MinerRsi}.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var state in MinerStates)
                {
                    Assert.That(rsi!.TryGetState(state, out _), Is.True,
                        $"crack_miner.rsi has no '{state}' state, which the prototype or the visualiser names.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The one test that goes through F5. A seam laid inside the cut circle has to ride up still anchored and still on
    /// the disc, and a miner wrenched onto its chunk tile has to find it there and cut it.
    /// </summary>
    [Test]
    public async Task RidesUpAndMinesOnARealChunk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        // The pair below goes down at x=0 and x=16, which is a circle of radius 10 about (8.5, 0.5); this tile is its
        // centre, and DiscIndices is asserted against below rather than trusted.
        var tile = new Vector2i(8, 0);

        var site = await BuildCrackerInOrbit(pair);
        var vein = await PlaceVein(pair, site.Ground, tile);

        await EnlargeBerth(pair, site, new Vector2i(24, 24), 20f);
        await DeployPair(pair, site, 0f, 16f, true);
        await AlignHull(pair, site, Vector2.Zero);
        await Energise(pair, site.Cracker);
        await BeginCut(pair, site);
        await CompleteCut(pair, site);

        var chunk = EntityUid.Invalid;
        var coords = EntityCoordinates.Invalid;

        await server.WaitAssertion(() =>
        {
            chunk = FindChunk(entMan);

            Assert.That(chunk, Is.Not.EqualTo(EntityUid.Invalid), "The completed cut produced no chunk at all.");

            var comp = entMan.GetComponent<WFPlanetChunkComponent>(chunk);

            Assert.That(DiscIndices(entMan, site.Ground, comp.HoleCentre, comp.Radius), Does.Contain(tile),
                "Precondition: the seam was laid inside the cut circle.");

            var xform = entMan.GetComponent<TransformComponent>(vein);
            coords = xform.Coordinates;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.GridUid, Is.EqualTo(chunk), "The seam was left behind on the ground map.");
                Assert.That(xform.Anchored, Is.True, "The seam rode up loose instead of anchored to the chunk.");
            }
        });

        var miner = EntityUid.Invalid;

        await server.WaitPost(() => miner = entMan.SpawnEntity(MinerProto, coords));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var attempt = new AnchorAttemptEvent(miner, miner);
            entMan.EventBus.RaiseLocalEvent(miner, attempt);

            Assert.That(attempt.Cancelled, Is.False, "The gate refused a real chunk tile with a real seam under it.");

            server.System<SharedTransformSystem>().AnchorEntity(miner);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(15f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFDeepVeinComponent>(vein);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Remaining, Is.LessThan(comp.TotalYield), "The seam on the chunk was never cut.");
                Assert.That(OreOnGrid(entMan, chunk, OreEntity(pair, comp)), Is.GreaterThan(0),
                    "Nothing was dropped on the chunk the miner is standing on.");
            }
        });

        // The chunk goes before the stack it is hanging over.
        await server.WaitPost(() =>
        {
            if (entMan.EntityExists(chunk))
                entMan.DeleteEntity(chunk);
        });

        await server.WaitRunTicks(1);

        await Teardown(pair, site);
        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns a miner on one tile of a grid and wrenches it down, gate and all.</summary>
    private static async Task<EntityUid> PlaceMiner(
        TestPair pair,
        EntityUid grid,
        Vector2i tile,
        string proto = MinerProto)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var miner = EntityUid.Invalid;

        await server.WaitPost(() => miner = entMan.SpawnEntity(proto, Centre(grid, tile)));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var attempt = new AnchorAttemptEvent(miner, miner);
            entMan.EventBus.RaiseLocalEvent(miner, attempt);

            Assert.That(attempt.Cancelled, Is.False, $"The miner refused to wrench down on {tile}.");

            transform.AnchorEntity(miner);

            Assert.That(entMan.GetComponent<TransformComponent>(miner).Anchored, Is.True,
                $"The miner did not stay anchored on {tile}.");
        });

        await server.WaitRunTicks(1);
        return miner;
    }

    /// <summary>Lays one more test seam on a site, on the same recipe BuildMinerSite uses for its own.</summary>
    private static async Task<EntityUid> PlaceVein(TestPair pair, EntityUid ground, Vector2i tile)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var vein = EntityUid.Invalid;

        await LayTiles(pair, ground, tile - new Vector2i(1, 1), tile + new Vector2i(1, 1));

        await server.WaitPost(() => vein = entMan.SpawnEntity(VeinProto, Centre(ground, tile)));
        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            if (entMan.EntityExists(vein) && !entMan.GetComponent<TransformComponent>(vein).Anchored)
                transform.AnchorEntity(vein);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(vein), Is.True, $"The test seam on {tile} deleted itself.");
            Assert.That(entMan.GetComponent<TransformComponent>(vein).Anchored, Is.True,
                $"The test seam on {tile} is not anchored.");
        });

        return vein;
    }

    /// <summary>The centre of one tile of a grid, which is where anything on the snap grid has to be spawned.</summary>
    private static EntityCoordinates Centre(EntityUid grid, Vector2i tile)
    {
        return new EntityCoordinates(grid, new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
    }

    /// <summary>
    /// The state the miner's sprite is actually driven from, read off the appearance rather than off the component:
    /// SetState is the only writer of either, so this pins the appearance push at the same time.
    /// Must be called from inside a server thread callback.
    /// </summary>
    private static WFCrackMinerState State(TestPair pair, EntityUid miner)
    {
        var appearance = pair.Server.EntMan.System<SharedAppearanceSystem>();

        Assert.That(appearance.TryGetData(miner, WFCrackMinerVisuals.State, out WFCrackMinerState state), Is.True,
            "The miner carries no appearance data for its own state key.");

        return state;
    }

    /// <summary>What is left in the miner's bay; zero when there is nothing in it. Server thread only.</summary>
    private static float Charge(TestPair pair, EntityUid miner)
    {
        return pair.Server.System<PowerCellSystem>().TryGetBatteryFromSlot(miner, out var battery)
            ? battery.CurrentCharge
            : 0f;
    }

    /// <summary>What a seam's rolled ore actually spawns as; always the single-count form. Server thread only.</summary>
    private static string OreEntity(TestPair pair, WFDeepVeinComponent vein)
    {
        var ore = pair.Server.ResolveDependency<IPrototypeManager>().Index<OrePrototype>(vein.Ore);

        Assert.That(ore.OreEntity, Is.Not.Null, $"{vein.Ore.Id} names no ore entity to spawn.");
        return ore.OreEntity!.Value.Id;
    }

    /// <summary>Every loose stack of one ore entity lying on a grid.</summary>
    private static List<EntityUid> Dropped(IEntityManager entMan, EntityUid grid, string oreEntity)
    {
        return Children(entMan, grid)
            .Where(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == oreEntity)
            .ToList();
    }

    /// <summary>Every distinct stackable prototype id lying on a grid, which is what a wrong spawn would show up in.</summary>
    private static HashSet<string> StackIds(IEntityManager entMan, EntityUid grid)
    {
        var found = new HashSet<string>();

        foreach (var uid in Children(entMan, grid))
        {
            if (!entMan.HasComponent<StackComponent>(uid))
                continue;

            if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID is { } id)
                found.Add(id);
        }

        return found;
    }

    /// <summary>One blunt blow of a given size, which StructuralMetallic takes a flat ten off.</summary>
    private static DamageSpecifier Damage(IPrototypeManager proto, float amount)
    {
        return new DamageSpecifier(proto.Index<DamageTypePrototype>(Blunt), FixedPoint2.New(amount));
    }
}
