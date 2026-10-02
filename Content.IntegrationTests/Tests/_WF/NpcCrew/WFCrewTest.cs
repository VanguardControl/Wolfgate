#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using System.Text;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Gravity;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

/// <summary>
/// NPC crew: a deckhand draws its holstered sidearm for a hostile and holsters it once the hostile is gone, the
/// planner puts a pilot at the helm, a radio officer beside it, a deckhand inside the airlock and the rest on deck, and
/// a pilot takes the helm, steers for his orders and lets go when he dies.
/// </summary>
[TestOf(typeof(WFCrewSystem))]
public sealed class WFCrewTest : InteractionTest
{
    private const string Hostile = "WFTestHostileMob";
    private const string TestHelm = "WFTestHelm";
    private const string Helm = "ComputerShuttle";
    private const string Dock = "AirlockShuttle";

    // A monster-faction human: the stock crew faction is hostile to SimpleHostile, so the deckhand fights it. Test
    // prototypes load after the faction system cached its table, so the factions themselves have to be real ones.
    // The helm counts as powered without an APC.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseMobHuman
  id: WFTestHostileMob
  components:
  - type: NpcFactionMember
    factions:
    - SimpleHostile

- type: entity
  parent: ComputerShuttle
  id: WFTestHelm
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>
    /// Off duty the sidearm sits in the belt. A hostile in view gets it drawn; the hostile going away gets it holstered.
    /// </summary>
    [Test]
    public async Task DeckhandDrawsForHostileAndHolstersAfter()
    {
        var inventory = Server.System<InventorySystem>();
        var hands = Server.System<SharedHandsSystem>();
        var crewSystem = Server.System<WFCrewSystem>();

        // A deck with gravity near the test player (NPCs sleep with no player within 32 tiles), the deckhand at its
        // centre, spawned the way the planner and the command do it: with a post, so guard duty keeps him near it.
        var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
        EntityUid crew = default;
        await Server.WaitPost(() =>
        {
            crew = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)), "test")!.Value;
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WFCrewComponent>(crew), "The crew component should be on the mob.");
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(crew).EntityName, Does.StartWith("Deckhand "),
                "The role title should go in front of the name.");
            Assert.That(Holstered(crew), "A deckhand should spawn with a sidearm in the belt.");
            Assert.That(hands.TryGetActiveItem(crew, out _), Is.False, "Hands should be empty off duty.");
        });

        // Two tiles from the post; the whole deck is inside his ten-tile vision.
        EntityUid hostile = default;
        await Server.WaitPost(() =>
        {
            hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(6.5f, 4.5f)));
        });

        await WaitUntil(() => hands.TryGetActiveItem(crew, out var held) && SEntMan.HasComponent<GunComponent>(held.Value),
            300, () => $"A deckhand should draw its sidearm for a hostile in view. {Describe(crew)}");

        await Server.WaitPost(() => SEntMan.DeleteEntity(hostile));

        await WaitUntil(() => !hands.TryGetActiveItem(crew, out _) && Holstered(crew),
            600, () => $"A deckhand should holster once the hostile is gone. {Describe(crew)}");

        bool Holstered(EntityUid uid)
        {
            return inventory.TryGetSlotEntity(uid, "belt", out var belt) && SEntMan.HasComponent<GunComponent>(belt.Value);
        }
    }

    /// <summary>
    /// A 7x7 deck with a helm and one airlock plans a pilot and a radio officer beside the helm, a deckhand just
    /// inside the airlock and two more spread over the deck.
    /// </summary>
    [Test]
    public async Task PlannerPlansHelmRadioDockAndDeck()
    {
        var planner = Server.System<WFCrewPlannerSystem>();

        var grid = await CreateDeck(new Vector2(40f, 40f), 7, gravity: false);
        await Server.WaitPost(() =>
        {
            // Helm in the middle; airlock on the bottom edge facing out (its local -Y), so inside is the tile above it.
            SEntMan.SpawnAtPosition(Helm, new EntityCoordinates(grid, new Vector2(3.5f, 3.5f)));
            SEntMan.SpawnAtPosition(Dock, new EntityCoordinates(grid, new Vector2(3.5f, 0.5f)));
        });
        await RunTicks(5);

        List<WFCrewPost> plan = new();
        await Server.WaitPost(() => plan = planner.Plan(grid, 2));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Count(p => p.Role == WFCrewRoles.Pilot), Is.EqualTo(1), "One pilot at the helm.");
            Assert.That(plan.Count(p => p.Role == WFCrewRoles.RadioOperator), Is.EqualTo(1), "One radio officer beside the helm.");
            Assert.That(plan.Count(p => p.Kind == WFCrewPostKind.Dock), Is.EqualTo(1), "One deckhand inside the airlock.");
            Assert.That(plan.Count(p => p.Kind == WFCrewPostKind.Deck), Is.EqualTo(2), "Two deckhands on deck.");
            Assert.That(plan.All(p => p.Coordinates.EntityId == grid), "Posts are grid-relative.");
        });

        var dock = plan.Single(p => p.Kind == WFCrewPostKind.Dock);
        Assert.That(dock.Coordinates.Position, Is.EqualTo(new Vector2(3.5f, 1.5f)), "The airlock post is the tile inside the airlock.");

        var helm = plan.Single(p => p.Kind == WFCrewPostKind.Helm);
        Assert.That((helm.Coordinates.Position - new Vector2(3.5f, 3.5f)).Length(), Is.EqualTo(1f).Within(0.01f),
            "The helm post is next to the console.");

        var deck = plan.Where(p => p.Kind == WFCrewPostKind.Deck).Select(p => p.Coordinates.Position).ToList();
        foreach (var post in deck)
        {
            foreach (var other in plan.Where(p => p.Coordinates.Position != post))
            {
                var d = post - other.Coordinates.Position;
                Assert.That(Math.Max(Math.Abs(d.X), Math.Abs(d.Y)), Is.GreaterThanOrEqualTo(3f).Within(0.01f),
                    "Deck posts keep their distance from every other post.");
            }
        }
    }

    /// <summary>
    /// A pilot beside a powered helm takes it and steers for his GoTo waypoint; killed, he lets go and the steering
    /// stops. The deck has no thrusters, so the grid itself never moves.
    /// </summary>
    [Test]
    public async Task PilotTakesHelmAndReleasesOnDeath()
    {
        var crewSystem = Server.System<WFCrewSystem>();
        var pilotSystem = Server.System<WFPilotDutySystem>();
        var mobState = Server.System<MobStateSystem>();

        var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
        EntityUid helm = default;
        EntityUid pilot = default;
        var waypoint = new EntityCoordinates(MapData.MapUid, new Vector2(206f, 0f));
        await Server.WaitPost(() =>
        {
            // Steering needs a shuttle under the pilot.
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            helm = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)));
            pilot = crewSystem.SpawnCrewman(WFCrewRoles.Pilot, new EntityCoordinates(deck, new Vector2(3.5f, 4.5f)), "test")!.Value;
            pilotSystem.GoTo(pilot, new List<EntityCoordinates> { waypoint });
        });

        await WaitUntil(() => SEntMan.TryGetComponent<PilotComponent>(pilot, out var attached) && attached.Console == helm
                               && SEntMan.TryGetComponent<ShipSteererComponent>(pilot, out var steerer)
                               && steerer.Mode == ShipSteeringMode.GoToRange
                               && SEntMan.GetComponent<WFPilotDutyComponent>(pilot).AtHelm,
            300, () => $"A pilot should take the helm and steer for his waypoint. {DescribePilot(pilot, helm)}");

        await Server.WaitAssertion(() =>
        {
            var duty = SEntMan.GetComponent<WFPilotDutyComponent>(pilot);
            var steerer = SEntMan.GetComponent<ShipSteererComponent>(pilot);
            Assert.That(SEntMan.GetComponent<WFCrewComponent>(pilot).Duty, Is.EqualTo(WFCrewDuties.Pilot), "The pilot role works the Pilot duty.");
            Assert.That(duty.Orders, Is.EqualTo(WFPilotOrder.GoTo), "Still flying the GoTo.");
            Assert.That(steerer.Coordinates, Is.EqualTo(waypoint), "Steering for the waypoint.");
            Assert.That(steerer.Range, Is.EqualTo(duty.ArrivalRange), "Arrival range is the GoTo range.");
        });

        await Server.WaitPost(() => mobState.ChangeMobState(pilot, MobState.Dead));

        await WaitUntil(() => !SEntMan.HasComponent<ShipSteererComponent>(pilot)
                               && !SEntMan.HasComponent<PilotComponent>(pilot)
                               && !SEntMan.GetComponent<WFPilotDutyComponent>(pilot).AtHelm,
            300, () => $"A dead pilot should let go of the helm and stop steering. {DescribePilot(pilot, helm)}");
    }

    /// <summary>A square of plating on the test map, optionally with gravity.</summary>
    private async Task<EntityUid> CreateDeck(Vector2 origin, int size, bool gravity)
    {
        var map = Server.System<SharedMapSystem>();
        var mapManager = Server.ResolveDependency<IMapManager>();
        var tileDefs = Server.ResolveDependency<ITileDefinitionManager>();

        EntityUid grid = default;
        await Server.WaitPost(() =>
        {
            var created = mapManager.CreateGrid(MapData.MapId);
            grid = created.Owner;
            var plating = new Tile(tileDefs["Plating"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < size; x++)
            {
                for (var y = 0; y < size; y++)
                {
                    tiles.Add((new Vector2i(x, y), plating));
                }
            }

            map.SetTiles(grid, created, tiles);
            Server.System<SharedTransformSystem>().SetCoordinates(grid, new EntityCoordinates(MapData.MapUid, origin));
            if (gravity)
                Server.System<GravitySystem>().EnableGravity(grid);
        });
        await RunTicks(5);
        return grid;
    }

    private async Task WaitUntil(Func<bool> condition, int maxTicks, Func<string> message)
    {
        var met = false;
        for (var waited = 0; waited < maxTicks && !met; waited += 10)
        {
            await RunTicks(10);
            await Server.WaitPost(() => met = condition());
        }

        if (met)
            return;

        var text = string.Empty;
        await Server.WaitPost(() => text = message());
        Assert.Fail(text);
    }

    /// <summary>What the crewman's AI is doing, for failure messages.</summary>
    private string Describe(EntityUid crew)
    {
        var npc = Server.System<NPCSystem>();
        var factions = Server.System<NpcFactionSystem>();
        var hands = Server.System<SharedHandsSystem>();
        var htn = SEntMan.GetComponent<HTNComponent>(crew);
        var sb = new StringBuilder();
        sb.Append($"awake={npc.IsAwake(crew, htn)} ");
        sb.Append($"plan={(htn.Plan == null ? "none" : htn.Plan.CurrentOperator.GetType().Name)} ");
        sb.Append($"target={(htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, SEntMan) ? target.ToString() : "none")} ");
        sb.Append($"hostiles={factions.GetNearbyHostiles(crew, 10f).Count()} ");
        sb.Append($"held={(hands.TryGetActiveItem(crew, out var held) ? SEntMan.ToPrettyString(held.Value).ToString() : "nothing")} ");
        var weapon = SEntMan.GetComponent<WFCrewWeaponComponent>(crew);
        sb.Append($"drawn={weapon.Drawn} slot={weapon.HolsterSlot ?? "-"} ");

        var member = SEntMan.GetComponent<NpcFactionMemberComponent>(crew);
        sb.Append($"factions=[{string.Join(",", member.Factions)}] hostileTo=[{string.Join(",", member.HostileFactions)}] ");
        var lookup = Server.System<EntityLookupSystem>();
        var xforms = Server.System<SharedTransformSystem>();
        var here = xforms.GetMapCoordinates(crew);
        foreach (var other in lookup.GetEntitiesInRange<NpcFactionMemberComponent>(here, 20f))
        {
            if (other.Owner == crew)
                continue;

            var there = xforms.GetMapCoordinates(other.Owner);
            sb.Append($"near:{SEntMan.ToPrettyString(other.Owner)}@{(there.Position - here.Position).Length():0.0}[{string.Join(",", other.Comp.Factions)}] ");
        }

        return sb.ToString();
    }

    /// <summary>What a pilot and his helm are doing, for failure messages.</summary>
    private string DescribePilot(EntityUid pilot, EntityUid helm)
    {
        var pilots = Server.System<WFPilotDutySystem>();
        var sb = new StringBuilder(Describe(pilot));
        var duty = SEntMan.GetComponent<WFPilotDutyComponent>(pilot);
        sb.Append($"mobState={SEntMan.GetComponent<MobStateComponent>(pilot).CurrentState} ");
        sb.Append($"orders={duty.Orders} atHelm={duty.AtHelm} waypoint={duty.WaypointIndex}/{duty.Waypoints.Count} ");
        sb.Append(SEntMan.TryGetComponent<PilotComponent>(pilot, out var attached)
            ? $"console={attached.Console?.ToString() ?? "null"} "
            : "console=none ");
        sb.Append(SEntMan.TryGetComponent<ShipSteererComponent>(pilot, out var steerer)
            ? $"steering={steerer.Mode}/{steerer.Status} to {steerer.Coordinates} range={steerer.Range} "
            : "steering=none ");
        sb.Append($"helm={helm} exists={SEntMan.EntityExists(helm)} ");
        if (SEntMan.EntityExists(helm))
        {
            sb.Append($"anchored={SEntMan.GetComponent<TransformComponent>(helm).Anchored} ");
            sb.Append($"powered={Server.System<PowerReceiverSystem>().IsPowered(helm)} ");
        }

        sb.Append($"found={(pilots.TryFindHelm(pilot, out var found) ? found.ToString() : "none")} ");
        sb.Append($"blackboardHelm={(SEntMan.GetComponent<HTNComponent>(pilot).Blackboard.TryGetValue<EntityUid>(WFPilotDutySystem.HelmKey, out var picked, SEntMan) ? picked.ToString() : "none")} ");
        return sb.ToString();
    }
}
