#nullable enable
using System.Collections.Generic;
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
  id: WFTestStoryRival
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: rival
    vessel: WFDredger
    company: PDV

- type: wfEncounter
  id: WFTestStoryHulk
  name: wf-encounter-name-hulk
  start: Manual
  ships:
  - key: hulk
    vessel: WFDredger
    distress: false
    derelict:
      dwellers: 3
      dwellerPool: [ WFMobDweller, WFMobDwellerHooded ]
      king: WFMobDwellerKing
      debris: 5
      debrisPool: [ ShardGlass ]

- type: wfEncounter
  id: WFTestStoryLeash
  name: wf-encounter-name-convoy
  start: Manual
  leash: 1000
  ships:
  - key: one
    vessel: WFDredger
    side: one
    objectives:
    - kind: Attack
      target: two
  - key: two
    vessel: WFDredger
    side: two
    offset: 400, 0
    objectives:
    - kind: Attack
      target: one

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

    /// <summary>An encounter ship inside another's zone, of a company that zone minds, makes enemies of the two.</summary>
    [Test]
    public async Task EncounterShipsInEachOthersZonesTurnHostile()
    {
        EntityUid first = default, second = default;
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WFEncounterSystem>();
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            Assert.That(system.TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestStoryIff"), new MapCoordinates(new Vector2(37000, 37000), MapData.MapId), out first), Is.True);
            Assert.That(system.TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestStoryRival"), new MapCoordinates(new Vector2(37500, 37000), MapData.MapId), out second), Is.True);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var alerts = Server.System<WFCrewAlertSystem>();
            var patrol = SEntMan.GetComponent<WFEncounterComponent>(first).Ships["patrol"];
            var rival = SEntMan.GetComponent<WFEncounterComponent>(second).Ships["rival"];
            Assert.That(alerts.GetHostileShips(patrol.Grid, patrol.Group), Does.Contain(rival.Grid), "The patrol takes the enemy ship in its zone for a threat.");
            Assert.That(alerts.GetHostileShips(rival.Grid, rival.Group), Does.Contain(patrol.Grid), "And is one to it in turn.");
            Server.System<WFEncounterSystem>().End(first);
            Server.System<WFEncounterSystem>().End(second);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A hulk has no crew and stays in its encounter all the same. Its chief leaves the ship's papers when he dies, a
    /// claimed ship is worth a fraction of its parts, and once released it outlives the encounter.
    /// </summary>
    [Test]
    public async Task DwellerHulkIsClaimedAndKept()
    {
        EntityUid encounter = default, hulk = default, king = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestStoryHulk");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(39000, 39000), MapData.MapId), out encounter), Is.True);
            hulk = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hulk"].Grid;
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).Resolution, Is.Null, "A hulk with no crew is not a destroyed ship.");
            var crew = SEntMan.EntityQueryEnumerator<Content.Server._WF.NpcCrew.Components.WFCrewComponent, TransformComponent>();
            while (crew.MoveNext(out _, out _, out var xform))
                Assert.That(xform.GridUid, Is.Not.EqualTo(hulk), "Nobody crews a hulk.");

            var dwellers = 0;
            var mobs = SEntMan.EntityQueryEnumerator<Content.Shared.NPC.Components.NpcFactionMemberComponent, TransformComponent>();
            while (mobs.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != hulk)
                    continue;
                dwellers++;
                if (SEntMan.HasComponent<Content.Server._WF.Encounters.Components.WFSalvageClaimDropComponent>(uid))
                    king = uid;
            }
            Assert.That(dwellers, Is.EqualTo(4), "Three dwellers and their king.");
            Assert.That(king, Is.Not.EqualTo(default(EntityUid)));
            Server.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(king, Content.Shared.Mobs.MobState.Dead);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var found = false;
            var claims = SEntMan.EntityQueryEnumerator<Content.Server._WF.Encounters.Components.WFSalvageClaimComponent>();
            while (claims.MoveNext(out var uid, out var claim))
            {
                if (claim.Ship != hulk)
                    continue;
                found = true;
                Assert.That(Server.System<WFSalvageClaimSystem>().TryClaim((uid, claim), SEntMan.GetEntity(Player), out var reason), Is.False);
                Assert.That(reason, Is.EqualTo("wf-salvage-claim-not-aboard"));
            }
            Assert.That(found, Is.True, "The king leaves the ship's papers.");

            var pricing = Server.System<Content.Server.Cargo.Systems.PricingSystem>();
            var worth = pricing.AppraiseGrid(hulk);
            Assert.That(worth, Is.GreaterThan(0));
            SEntMan.EnsureComponent<Content.Server._WF.Encounters.Components.WFSalvagedShipComponent>(hulk);
            Assert.That(pricing.AppraiseGrid(hulk), Is.EqualTo(worth * 0.25).Within(1.0), "A claimed hulk sells for a quarter.");

            Server.System<WFEncounterSystem>().Release(hulk);
            Assert.That(SEntMan.HasComponent<WFEncounterGridComponent>(hulk), Is.False);
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).Resolution, Is.EqualTo(WFEncounterResolution.Completed));
        });
        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(hulk), Is.True, "A claimed ship is not cleaned up with its encounter.");
            SEntMan.DeleteEntity(hulk);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// Every hull an encounter may pick loads and takes a crew. One that can't would fail the spawn each time it is
    /// drawn, and the encounter would only ever be seen in its other hulls.
    /// </summary>
    [Test]
    public async Task EveryEncounterHullSpawnsAndTakesACrew()
    {
        var hulls = new SortedSet<string>();
        var derelicts = new HashSet<string>();
        await Server.WaitPost(() =>
        {
            foreach (var prototype in Server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<WFEncounterPrototype>())
            {
                if (prototype.ID.StartsWith("WFTest"))
                    continue;

                foreach (var ship in prototype.Ships)
                {
                    var named = ship.Vessels.Select(vessel => vessel.Id).ToList();
                    if (ship.Vessel is { } single)
                        named.Add(single.Id);
                    hulls.UnionWith(named);
                    if (ship.Derelict != null)
                        derelicts.UnionWith(named);
                }
            }
        });

        var failures = new List<string>();
        var x = 60000f;
        foreach (var hull in hulls)
        {
            x += 600f;
            var at = new Vector2(x, 60000f);
            await Server.WaitPost(() =>
            {
                var prototypes = Server.ResolveDependency<IPrototypeManager>();
                if (!prototypes.TryIndex<Content.Shared._NF.Shipyard.Prototypes.VesselPrototype>(hull, out var vessel)
                    || !Server.System<Content.Server._WF.Administration.Systems.AdminVesselSpawnSystem>()
                        .TrySpawnVessel(vessel, MapData.MapId, at, null, out var grid))
                {
                    failures.Add($"{hull}: does not load");
                    return;
                }

                // A hulk needs free deck, not posts.
                var crewable = Server.System<WFCrewSetupSystem>().Plan(grid.Value, 1, false).Count > 0;
                var deck = Server.System<WFCrewPlannerSystem>().HoldTiles(grid.Value, 4).Count > 0;
                if (!crewable && !derelicts.Contains(hull) || !deck)
                    failures.Add($"{hull}: {(deck ? "no crew posts" : "no free deck")}");
                SEntMan.DeleteEntity(grid.Value);
            });
            await RunTicks(2);
        }

        Assert.That(failures, Is.Empty, string.Join("; ", failures));
    }

    /// <summary>A ship that strays past its encounter's leash breaks off and flies back before it takes up its fight again.</summary>
    [Test]
    public async Task StrayingShipIsCalledBack()
    {
        EntityUid encounter = default, stray = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestStoryLeash");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(45000, 45000), MapData.MapId), out encounter), Is.True);
            stray = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["one"].Grid;
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(First(), Is.EqualTo(WFCrewObjectiveKind.Attack), "Inside the leash it fights.");
            Server.System<SharedTransformSystem>().SetCoordinates(stray, new EntityCoordinates(MapData.MapUid, new Vector2(48000, 45000)));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(First(), Is.EqualTo(WFCrewObjectiveKind.GoTo), "Past it, it is sent back first.");
            Assert.That(Orders(), Has.Count.EqualTo(2), "And its fight is still waiting for it.");
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);

        List<WFCrewObjective> Orders()
        {
            var net = SEntMan.GetNetEntity(stray);
            return Server.System<WFCrewObjectiveSystem>().Snapshot().First(crew => crew.Grid == net).Objectives;
        }

        WFCrewObjectiveKind First() => Orders()[0].Kind;
    }
}
