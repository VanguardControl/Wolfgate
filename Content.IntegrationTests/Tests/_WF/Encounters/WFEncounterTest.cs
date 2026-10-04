#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Client._WF.Encounters;
using Content.Shared._WF.CCVar;
using Robust.Shared.Configuration;
using Content.Shared._Mono.CCVar;
using Content.Server.Gravity;
using Robust.Shared.Map.Components;
using System.Collections.Generic;
using Robust.Shared.Maths;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>
/// Encounters: a prototype's ships spawn crewed, in their sides, with their rules and orders; an encounter resolves
/// when no ship is left and ending one removes its ships and crews.
/// </summary>
[TestOf(typeof(WFEncounterSystem))]
public sealed class WFEncounterTest : InteractionTest
{
    private const string Convoy = "WFTestEncounterConvoy";

    // Spawned ships play sounds, and the client's room echo asserts on audio whose parent changes mid-setup.
    [SetUp]
    public async Task DisableRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, false));
    }

    [TearDown]
    public async Task RestoreRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, MonoCVars.AreaEchoEnabled.DefaultValue));
    }

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfEncounter
  id: WFTestEncounterConvoy
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: lead
    vessel: WFDredger
    side: convoy
    disengage: Deter
    disengageRange: 800
    skill: Green
  - key: escort
    vessel: WFDredger
    offset: 200, 0
    side: convoy
    captain: false
    objectives:
    - kind: Escort
      target: lead

- type: wfEncounter
  id: WFTestEncounterCheap
  name: wf-encounter-name-convoy
  category: Threat
  cost: 1
  hidden: true
  announcement: wf-encounter-announce-ambush
  minDistance: 700
  maxDistance: 900
  ships:
  - key: lone
    vessel: WFDredger
    objectives:
    - kind: Hold
      duration: 5

- type: wfEncounter
  id: WFTestEncounterZoned
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: guarded
    vessel: WFDredger
    warnRange: 500
    attackRange: 150

- type: wfEncounter
  id: WFTestEncounterCostly
  name: wf-encounter-name-convoy
  cost: 50
  minDistance: 700
  maxDistance: 900
  ships:
  - key: lone
    vessel: WFDredger
";

    [Test]
    public async Task EncounterSpawnsCrewedShipsAndEndsCleanly()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WFEncounterSystem>();
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(Convoy);
            Assert.That(system.TrySpawn(prototype, new MapCoordinates(new Vector2(600, 600), MapData.MapId), out encounter), Is.True);
            Assert.That(system.ActiveCount(), Is.EqualTo(1));
        });
        await RunTicks(5);

        EntityUid lead = default, escort = default;
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            lead = comp.Ships["lead"].Grid;
            escort = comp.Ships["escort"].Grid;
            Assert.That(SEntMan.GetComponent<WFEncounterGridComponent>(lead).Encounter, Is.EqualTo(encounter));

            var crews = Server.System<WFCrewObjectiveSystem>().Snapshot().ToDictionary(crew => SEntMan.GetEntity(crew.Grid));
            Assert.That(crews[lead].Alive, Is.GreaterThanOrEqualTo(2));
            Assert.That(crews[lead].Settings.Disengage, Is.EqualTo(WFCrewDisengage.Deter));
            Assert.That(crews[lead].Settings.DisengageRange, Is.EqualTo(800f));
            Assert.That(crews[lead].Settings.Skill, Is.EqualTo(WFCrewSkill.Green));
            Assert.That(crews[escort].Settings.Skill, Is.EqualTo(WFCrewSkill.Veteran));
            Assert.That(crews[escort].Objectives.Single().Kind, Is.EqualTo(WFCrewObjectiveKind.Escort));
            Assert.That(crews[escort].Objectives.Single().Target, Is.EqualTo(SEntMan.GetNetEntity(lead)));
            Assert.That(crews[lead].Settings.Battlegroup, Is.Not.Empty);
            Assert.That(crews[lead].Settings.Battlegroup, Is.EqualTo(crews[escort].Settings.Battlegroup));
            Assert.That(Server.System<WFCrewEscortSystem>().AreInFormation(lead, escort), Is.True);

            var system = Server.System<WFEncounterSystem>();
            system.Resolve(encounter, WFEncounterResolution.Expired);
            Assert.That(comp.Resolution, Is.EqualTo(WFEncounterResolution.Expired));
            Assert.That(system.ActiveCount(), Is.Zero, "A resolved encounter no longer counts against the cap.");
            Assert.That(SEntMan.Deleted(lead), Is.False, "Resolved ships stay until players have left them.");
            system.End(encounter);
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(lead) && SEntMan.Deleted(escort) && SEntMan.Deleted(encounter), Is.True);
            Assert.That(SEntMan.EntityQuery<WFCrewComponent>().Any(), Is.False, "Ending an encounter removes its crews.");
        });
    }

    /// <summary>The admin window's requests start, list and end encounters and set the scheduler; the window builds.</summary>
    [Test]
    public async Task EncounterAdminControlsEncountersAndScheduler()
    {
        await Server.WaitAssertion(() =>
        {
            var admin = Server.System<WFEncounterAdminSystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            var cap = config.GetCVar(EncountersCVars.MaxActive);

            Assert.That(admin.Handle(new WFEncounterAdminRequest
            {
                Action = WFEncounterAdminAction.Scheduler, IntervalMin = 5, IntervalMax = 60, MaxActive = 3,
            }, ServerSession), Is.Not.Null, "An interval under 30 seconds is refused.");
            Assert.That(config.GetCVar(EncountersCVars.MaxActive), Is.EqualTo(cap));
            Assert.That(admin.Handle(new WFEncounterAdminRequest
            {
                Action = WFEncounterAdminAction.Scheduler, IntervalMin = 60, IntervalMax = 120, MaxActive = 3, Paused = true,
            }, ServerSession), Is.Null);
            var state = admin.BuildState();
            Assert.That(state.MaxActive, Is.EqualTo(3));
            Assert.That(state.Paused, Is.True);
            Assert.That(state.Enabled, Is.False);
            Assert.That(state.NextIn, Is.LessThan(0f), "A disabled scheduler has no next attempt.");

            Assert.That(admin.Handle(new WFEncounterAdminRequest { Action = WFEncounterAdminAction.Spawn, Prototype = "Nope" }, ServerSession),
                Is.Not.Null);
            Assert.That(admin.Handle(new WFEncounterAdminRequest
            {
                Action = WFEncounterAdminAction.Spawn, Prototype = Convoy, Distance = 800,
            }, ServerSession), Is.Null);
            state = admin.BuildState();
            Assert.That(state.Encounters.Single().Ships, Has.Count.EqualTo(2));
            Assert.That(state.Encounters.Single().Ships.All(ship => ship.Exists && ship.Crew > 0), Is.True);
            Assert.That(state.Prototypes.Single(prototype => prototype.Id == Convoy).Running, Is.True);
            Assert.That(Server.System<WFEncounterSystem>().IsRunning(Convoy), Is.True);

            Assert.That(admin.Handle(new WFEncounterAdminRequest
            {
                Action = WFEncounterAdminAction.End, Target = state.Encounters.Single().Uid,
            }, ServerSession), Is.Null);
            Assert.That(Server.System<WFEncounterSystem>().IsRunning(Convoy), Is.False);

            config.SetCVar(EncountersCVars.MaxActive, cap);
            config.SetCVar(EncountersCVars.IntervalMin, EncountersCVars.IntervalMin.DefaultValue);
            config.SetCVar(EncountersCVars.IntervalMax, EncountersCVars.IntervalMax.DefaultValue);
            Server.System<WFEncounterSchedulerSystem>().Paused = false;
        });
        await RunTicks(10);
        await Server.WaitAssertion(() => Assert.That(Server.System<WFEncounterAdminSystem>().BuildState().Encounters, Is.Empty));

        await Client.WaitAssertion(() =>
        {
            var window = new WFEncounterWindow();
            window.OpenCentered();
            window.Close();
        });
    }

    /// <summary>Every encounter prototype spawns all its ships crewed and with valid orders, and ends cleanly.</summary>
    [Test]
    public async Task EveryEncounterPrototypeSpawnsAndEnds()
    {
        EntityUid from = default, to = default;
        await Server.WaitPost(() =>
        {
            // Stand-ins for the stations that Station and Route placements hand to the orders.
            var maps = Server.ResolveDependency<IMapManager>();
            from = maps.CreateGridEntity(MapData.MapId).Owner;
            to = maps.CreateGridEntity(MapData.MapId).Owner;
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(from, new EntityCoordinates(MapData.MapUid, new Vector2(3000, 0)));
            transform.SetCoordinates(to, new EntityCoordinates(MapData.MapUid, new Vector2(3000, 3000)));
        });
        var ids = Server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<WFEncounterPrototype>()
            .Where(prototype => !prototype.ID.StartsWith("WFTest")).Select(prototype => prototype.ID).ToList();
        Assert.That(ids, Is.Not.Empty);
        var x = 3500f;
        foreach (var id in ids)
        {
            EntityUid encounter = default;
            var origin = new MapCoordinates(new Vector2(x, 1500), MapData.MapId);
            x += 1500f;
            await Server.WaitAssertion(() =>
            {
                var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(id);
                Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, origin, out encounter, null, new[] { from, to }), Is.True, id);
                var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
                Assert.That(comp.Ships, Has.Count.EqualTo(prototype.Ships.Count), id);
                var crews = Server.System<WFCrewObjectiveSystem>().Snapshot();
                foreach (var ship in comp.Ships.Values)
                {
                    Assert.That(crews.Any(crew => crew.Group == ship.Group && crew.Alive > 0), Is.True, $"{id}: {ship.Group} has no crew");
                }
            });
            await RunTicks(5);
            await Server.WaitPost(() => Server.System<WFEncounterSystem>().End(encounter));
            await RunTicks(5);
        }

        await Server.WaitPost(() =>
        {
            SEntMan.DeleteEntity(from);
            SEntMan.DeleteEntity(to);
        });
    }

    /// <summary>The storyteller keeps to its budget and to one of a kind; a hidden encounter announces itself when revealed.</summary>
    [Test]
    public async Task StorytellerKeepsToBudgetAndRevealsHiddenEncounters()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            var scheduler = Server.System<WFEncounterSchedulerSystem>();
            var system = Server.System<WFEncounterSystem>();
            config.SetCVar(EncountersCVars.Preset, "WFEncounterPresetQuiet");
            Assert.That(scheduler.Preset, Is.Not.Null);
            Assert.That(scheduler.Budget(), Is.GreaterThanOrEqualTo(2).And.LessThan(50));
            Assert.That(scheduler.StartRound(), Is.Zero, "No stations on a test map, so nothing can be placed beside one.");

            // Only the cheap test encounter fits the budget; the shipped ones need stations or more players.
            Assert.That(scheduler.TrySchedule(out encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            Assert.That(comp.Prototype.Id, Is.EqualTo("WFTestEncounterCheap"));
            Assert.That(system.ActiveCost(), Is.EqualTo(1));
            Assert.That(scheduler.TrySchedule(out _), Is.False, "It is running, and nothing else fits.");

            Assert.That(comp.Hidden, Is.True);
            Assert.That(comp.Announcement, Is.Not.Null, "A hidden encounter keeps its announcement back.");
            system.Reveal(encounter);
            Assert.That(comp.Hidden, Is.False);
            Assert.That(comp.Announcement, Is.Null);

            system.Resolve(encounter, WFEncounterResolution.Completed);
            Assert.That(comp.JumpAt, Is.Not.Null, "A transient encounter jumps out once its orders are flown.");
            system.End(encounter);
            config.SetCVar(EncountersCVars.Preset, EncountersCVars.Preset.DefaultValue);
        });
        await RunTicks(10);
    }

    /// <summary>A player ship is warned inside the warning zone and counts as an attacker inside the attack zone.</summary>
    [Test]
    public async Task ZonesWarnThenTreatIntrudersAsAttackers()
    {
        EntityUid encounter = default, ship = default, intruder = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterZoned");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(5000, 5000), MapData.MapId), out encounter), Is.True);
            ship = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"].Grid;

            // The player's ship: a bare grid with the test player standing on it, 300 m off.
            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(5300, 5000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(state.Warned.ContainsKey(intruder), Is.True, "Inside the warning zone the intruder is warned.");
            Assert.That(state.Engaged, Is.Empty);
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(ship, state.Group), Is.Empty);
            Server.System<SharedTransformSystem>().SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(5100, 5000)));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(state.Engaged.Contains(intruder), Is.True);
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(ship, state.Group), Does.Contain(intruder),
                "Inside the attack zone the intruder is an attacker.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }

    /// <summary>Every body and loadout of every crew profile makes a living, dressed crewman with his role's kit.</summary>
    [Test]
    public async Task EveryCrewProfileBodyAndLoadoutSpawns()
    {
        var spawned = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var crew = Server.System<WFCrewSystem>();
            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var post = new EntityCoordinates(grid.Owner, new Vector2(0.5f));
            foreach (var profile in prototypes.EnumeratePrototypes<WFCrewProfilePrototype>())
            {
                Assert.That(profile.Bodies, Is.Not.Empty, profile.ID);
                foreach (var body in profile.Bodies.Distinct())
                {
                    var hand = crew.SpawnCrewman(WFCrewRoles.Deckhand, post, "bodies", profile.Loadouts[WFCrewRoles.Deckhand][0], body);
                    Assert.That(hand, Is.Not.Null, $"{profile.ID}: {body}");
                    SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(hand!.Value).Enabled = false;
                    Assert.That(SEntMan.HasComponent<WFCrewRepairComponent>(hand.Value), Is.True, $"{body} got no deckhand kit");
                    spawned.Add(hand.Value);
                }

                foreach (var (role, loadouts) in profile.Loadouts)
                {
                    foreach (var loadout in loadouts.Distinct())
                    {
                        var member = crew.SpawnCrewman(role, post, "bodies", loadout, profile.Bodies[0]);
                        Assert.That(member, Is.Not.Null, $"{profile.ID}: {role} in {loadout}");
                        SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(member!.Value).Enabled = false;
                        Assert.That(Server.System<Content.Shared.Inventory.InventorySystem>().TryGetSlotEntity(member.Value, "jumpsuit", out _),
                            Is.True, $"{loadout} left {role} undressed");
                        spawned.Add(member.Value);
                    }
                }
            }
        });
        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            foreach (var uid in spawned)
            {
                Assert.That(Server.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(uid), Is.True, SEntMan.ToPrettyString(uid).ToString());
                SEntMan.DeleteEntity(uid);
            }
        });
    }
}
