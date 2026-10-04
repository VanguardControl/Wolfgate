#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string StorytellerPrototypes = @"
- type: wfEncounter
  id: WFTestStoryRoundStart
  name: wf-encounter-name-convoy
  start: RoundStart
  weight: 0
  cost: 3
  ships:
  - key: lone
    vessel: WFDredger

- type: wfEncounter
  id: WFTestStoryIff
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: patrol
    vessel: WFDredger
    company: TSF
    warnRange: 800
    attackRange: 300
    zoneTargets: AtWar

- type: wfEncounter
  id: WFTestStoryDock
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: lead
    vessel: WFDredger
    side: convoy
    objectives:
    - kind: Dock
      target: berth
    - kind: Hold
      duration: 300
    - kind: Undock
    - kind: GoTo
      offset: 3000, 0
      range: 200
  - key: berth
    vessel: WFDredger
    offset: 300, 0
    side: convoy
";

    /// <summary>An encounter placed at round start takes no slot under the cap and nothing from the budget, round-long or not.</summary>
    [Test]
    public async Task RoundStartEncounterTakesNothingFromTheBudget()
    {
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WFEncounterSystem>();
            var count = system.ActiveCount();
            var cost = system.ActiveCost();
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestStoryRoundStart");
            Assert.That(system.TrySpawn(prototype, new MapCoordinates(new Vector2(33000, 33000), MapData.MapId), out var encounter), Is.True);
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).OffBudget, Is.True);
            Assert.That(system.ActiveCount(), Is.EqualTo(count));
            Assert.That(system.ActiveCost(), Is.EqualTo(cost));
            system.End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>A ship that cannot dock at a stop waits off it a short while in place of the call, then flies on.</summary>
    [Test]
    public async Task ShipThatCannotDockLoitersThenMovesOn()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestStoryDock");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(31000, 31000), MapData.MapId), out encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var lead = comp.Ships["lead"];
            var pilot = EntityUid.Invalid;
            var pilots = SEntMan.EntityQueryEnumerator<WFPilotDutyComponent, WFCrewComponent>();
            while (pilots.MoveNext(out var uid, out _, out var crew))
            {
                if (crew.Group == lead.Group)
                    pilot = uid;
            }

            Assert.That(pilot.IsValid(), Is.True, "The ship has a pilot.");
            var failed = new WFPilotDockFailedEvent(pilot, lead.Grid, comp.Ships["berth"].Grid);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref failed);
            Assert.That(Server.System<WFCrewObjectiveSystem>().QueueStatus(lead.Grid, lead.Group), Is.EqualTo("dock-failed"));
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            var objectives = Server.System<WFCrewObjectiveSystem>();
            var lead = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["lead"];
            var net = SEntMan.GetNetEntity(lead.Grid);
            var row = objectives.Snapshot().Single(crew => crew.Grid == net && crew.Group == lead.Group);
            Assert.That(objectives.QueueStatus(lead.Grid, lead.Group), Is.Not.EqualTo("dock-failed"));
            Assert.That(row.Objectives, Is.Not.Empty);
            Assert.That(row.Objectives[0].Kind, Is.EqualTo(WFCrewObjectiveKind.Hold), "It waits off the stop.");
            Assert.That(row.Objectives[0].Duration, Is.GreaterThan(0f).And.LessThanOrEqualTo(90f), "And not for the whole call.");
            Assert.That(row.Objectives.Any(item => item.Kind == WFCrewObjectiveKind.Dock), Is.False);
            Assert.That(row.Objectives.Any(item => item.Kind == WFCrewObjectiveKind.GoTo), Is.True, "The rest of its orders stand.");
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A patrol cannot tell whose a ship is with its IFF off: a neutral ship is left alone while it shows, challenged
    /// with warning shots in the warning zone once it hides, and fired on in the attack zone.
    /// </summary>
    [Test]
    public async Task PatrolChallengesAShipWithItsIffOff()
    {
        EntityUid encounter = default, patrol = default, intruder = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestStoryIff");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(35000, 35000), MapData.MapId), out encounter), Is.True);
            patrol = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"].Grid;

            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(35600, 35000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(intruder).CompanyName = "USSP";
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var alerts = Server.System<WFCrewAlertSystem>();
            Assert.That(alerts.IsWarningShot(patrol, intruder), Is.False, "A neutral ship showing its IFF is left alone.");
            Server.System<Content.Server.Shuttles.Systems.ShuttleSystem>().AddIFFFlag(intruder, Content.Shared.Shuttles.Components.IFFFlags.HideLabel);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"];
            Assert.That(Server.System<WFCrewAlertSystem>().IsWarningShot(patrol, intruder), Is.True, "Masked, it gets warning shots.");
            Assert.That(state.Engaged, Is.Empty, "But is not fired on in the warning zone.");
            Server.System<SharedTransformSystem>().SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(35150, 35000)));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"];
            Assert.That(state.Engaged.Contains(intruder), Is.True, "In the attack zone it is engaged.");
            Assert.That(Server.System<WFCrewAlertSystem>().IsWarningShot(patrol, intruder), Is.False, "And the shots are no longer warnings.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }
}
