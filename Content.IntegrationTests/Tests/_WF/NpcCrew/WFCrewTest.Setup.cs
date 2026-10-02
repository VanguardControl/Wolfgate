#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    [TestPrototypes]
    private const string GunneryPrototype = @"
- type: entity
  parent: ComputerGunneryConsole
  id: WFTestGunnery
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>A gunner walks to the console, targets only an attacker, and stops on console loss.</summary>
    [Test]
    public async Task GunnerOperatesConsoleUntilLost()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        var attacker = await CreateDeck(new Vector2(25, 0), 3, gravity: true);
        EntityUid gunner = default, console = default;
        await Server.WaitPost(() =>
        {
            console = SEntMan.SpawnAtPosition("WFTestGunnery", new EntityCoordinates(deck, new Vector2(3.5f)));
            gunner = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Gunner,
                new EntityCoordinates(deck, new Vector2(1.5f, 3.5f)), "guns")!.Value;
        });
        await WaitUntil(() => SEntMan.GetComponent<WFGunnerDutyComponent>(gunner).AtConsole,
            600, () => Describe(gunner));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<ShipTargetingComponent>(gunner), Is.False);
            var ev = new WFCrewHullHitEvent(deck, attacker);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref ev, true);
        });
        await WaitUntil(() => SEntMan.HasComponent<ShipTargetingComponent>(gunner), 120, () => Describe(gunner));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ShipTargetingComponent>(gunner).Target.EntityId, Is.EqualTo(attacker));
            SEntMan.DeleteEntity(console);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<ShipTargetingComponent>(gunner), Is.False));
    }

    /// <summary>Encounter and admin spawning reject invalid plans atomically and use the selected real loadout.</summary>
    [Test]
    public async Task CrewSetupValidatesAndSpawnsMission()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var setup = Server.System<WFCrewSetupSystem>();
            var mission = new WFCrewMission { Group = "setup", Callsign = "Test crew", Order = WFPilotOrder.GoTo,
                Destination = new Vector2(100, 100) };
            var posts = new List<WFCrewSetupPost>
            {
                new() { Role = "WFCrewDeckhand", Loadout = "WFCrewMarineGear", Position = new Vector2(1.5f) },
                new() { Role = "WFCrewPilot", Position = new Vector2(float.NaN, 2.5f) },
            };
            Assert.That(setup.TrySpawn(deck, posts, mission, out var rejected), Is.False);
            Assert.That(rejected, Is.Empty);
            posts[1].Position = new Vector2(2.5f);
            Assert.That(setup.TrySpawn(deck, posts, mission, out var spawned), Is.True);
            Assert.That(spawned, Has.Count.EqualTo(2));
            foreach (var uid in spawned)
                SEntMan.GetComponent<HTNComponent>(uid).Enabled = false;
            var inventory = Server.System<InventorySystem>();
            Assert.That(inventory.TryGetSlotEntity(spawned[0], "back", out var rifle), Is.True);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(rifle!.Value).EntityPrototype!.ID, Is.EqualTo("WeaponRifleLecter"));
            var pilot = SEntMan.GetComponent<WFPilotDutyComponent>(spawned[1]);
            Assert.That(pilot.Orders, Is.EqualTo(WFPilotOrder.GoTo));
            Assert.That(pilot.Waypoints[0].Position, Is.EqualTo(mission.Destination));
            mission.Order = WFPilotOrder.Hold;
            Assert.That(setup.TryApplyMission(deck, mission), Is.True);
            Assert.That(pilot.Orders, Is.EqualTo(WFPilotOrder.Hold));
            mission.Order = WFPilotOrder.Follow;
            mission.Target = null;
            Assert.That(setup.TryApplyMission(deck, mission), Is.False);
            Assert.That(pilot.Orders, Is.EqualTo(WFPilotOrder.Hold));
        });
    }
}
