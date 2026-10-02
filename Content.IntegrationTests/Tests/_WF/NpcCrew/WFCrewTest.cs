#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

/// <summary>
/// NPC crew: a deckhand draws its holstered sidearm for a hostile and holsters it once the hostile is gone, and the
/// planner puts a pilot at the helm, a radio officer beside it, a deckhand inside the airlock and the rest on deck.
/// </summary>
[TestOf(typeof(WFCrewSystem))]
public sealed class WFCrewTest : InteractionTest
{
    private const string Deckhand = "WFTestCrewDeckhand";
    private const string Hostile = "WFTestHostileMob";
    private const string Helm = "ComputerShuttle";
    private const string Dock = "AirlockShuttle";

    [TestPrototypes]
    private const string Prototypes = @"
- type: npcFaction
  id: WFTestCrew
  hostile:
  - WFTestHostile

- type: npcFaction
  id: WFTestHostile
  hostile:
  - WFTestCrew

- type: entity
  parent: WFMobCrewDeckhand
  id: WFTestCrewDeckhand
  components:
  - type: NpcFactionMember
    factions:
    - WFTestCrew

- type: entity
  parent: BaseMobHuman
  id: WFTestHostileMob
  components:
  - type: NpcFactionMember
    factions:
    - WFTestHostile
";

    /// <summary>
    /// Off duty the sidearm sits in the belt. A hostile in view gets it drawn; the hostile going away gets it holstered.
    /// </summary>
    [Test]
    public async Task DeckhandDrawsForHostileAndHolstersAfter()
    {
        var inventory = Server.System<InventorySystem>();
        var hands = Server.System<SharedHandsSystem>();

        var crew = ToServer(await SpawnTarget(Deckhand));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WFCrewComponent>(crew), "The crew component should be on the mob.");
            Assert.That(Holstered(crew), "A deckhand should spawn with a sidearm in the belt.");
            Assert.That(hands.TryGetActiveItem(crew, out _), Is.False, "Hands should be empty off duty.");
        });

        EntityUid hostile = default;
        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords).Offset(new Vector2(2f, 0f));
            hostile = SEntMan.SpawnAtPosition(Hostile, coords);
        });

        await WaitUntil(() => hands.TryGetActiveItem(crew, out var held) && SEntMan.HasComponent<GunComponent>(held.Value),
            300, "A deckhand should draw its sidearm for a hostile in view.");

        await Server.WaitPost(() => SEntMan.DeleteEntity(hostile));

        await WaitUntil(() => !hands.TryGetActiveItem(crew, out _) && Holstered(crew),
            600, "A deckhand should holster once the hostile is gone.");

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
        var map = Server.System<SharedMapSystem>();
        var planner = Server.System<WFCrewPlannerSystem>();
        var mapManager = Server.ResolveDependency<IMapManager>();
        var tileDefs = Server.ResolveDependency<ITileDefinitionManager>();

        EntityUid grid = default;
        await Server.WaitPost(() =>
        {
            var created = mapManager.CreateGrid(MapData.MapId);
            grid = created.Owner;
            var plating = new Tile(tileDefs["Plating"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 7; x++)
            {
                for (var y = 0; y < 7; y++)
                {
                    tiles.Add((new Vector2i(x, y), plating));
                }
            }

            map.SetTiles(grid, created, tiles);
            Server.System<SharedTransformSystem>().SetCoordinates(grid, new EntityCoordinates(MapData.MapUid, new Vector2(40f, 40f)));

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

    private async Task WaitUntil(Func<bool> condition, int maxTicks, string message)
    {
        var met = false;
        for (var waited = 0; waited < maxTicks && !met; waited += 10)
        {
            await RunTicks(10);
            await Server.WaitPost(() => met = condition());
        }

        Assert.That(met, message);
    }
}
