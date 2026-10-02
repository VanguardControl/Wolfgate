#nullable enable
using System.Numerics;
using System.Reflection;
using Robust.Client.UserInterface.Controls;
using Content.Client._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Content.Server.Power.Components;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>The actual Dredger hull can physically mate with the actual Drillsite without FTL.</summary>
    [Test]
    public async Task DredgerDocksAtDrillsite()
    {
        EntityUid ship = default, site = default, pilot = default, helm = default;
        await Server.WaitAssertion(() =>
        {
            var loader = Server.System<MapLoaderSystem>();
            Assert.That(loader.TryLoadGrid(MapData.MapId, new ResPath("/SharedMaps/_WF/Shipyard/Shuttles/dredger.yml"), out var loadedShip,
                offset: new Vector2(2000, 2200)), Is.True);
            Assert.That(loader.TryLoadGrid(MapData.MapId, new ResPath("/Maps/_Mono/POI/derelictdrillsite.yml"), out var loadedSite,
                offset: new Vector2(2000, 2000)), Is.True);
            ship = loadedShip!.Value.Owner;
            site = loadedSite!.Value.Owner;
        });
        await RunTicks(120);
        await Server.WaitAssertion(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var transform))
            {
                if (transform.GridUid == ship) { helm = uid; break; }
            }
            Assert.That(helm.IsValid(), Is.True);
            var power = SEntMan.EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
            while (power.MoveNext(out _, out var receiver, out var transform))
            {
                if (transform.GridUid == ship)
                    receiver.NeedsPower = false;
            }
            var thrusters = SEntMan.EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
            while (thrusters.MoveNext(out var uid, out var thrust, out var transform))
            {
                if (transform.GridUid == ship)
                    Server.System<ThrusterSystem>().EnableThruster(uid, thrust);
            }
            var planner = Server.System<WFCrewPlannerSystem>();
            var post = planner.Plan(ship).Find(entry => entry.Role == WFCrewRoles.Pilot);
            Assert.That(post.Coordinates.IsValid(SEntMan), Is.True, "Dredger has a safe helm post.");
            pilot = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Pilot, post.Coordinates, "dredger")!.Value;
            Assert.That(Server.System<WFPilotDutySystem>().TryPlanDock(ship, site, 40, out _), Is.True, "A pair of hull ports fits.");
            Server.System<WFPilotDutySystem>().Dock(pilot, site);
        });
        await WaitUntil(() => Server.System<DockingSystem>().AreGridsDocked(ship, site), 18000,
            () => $"{DescribePilot(pilot, helm)} phase={SEntMan.GetComponent<WFPilotDutyComponent>(pilot).DockPhase} attempts={SEntMan.GetComponent<WFPilotDutyComponent>(pilot).DockAttempts} position={SEntMan.GetComponent<TransformComponent>(ship).LocalPosition} angle={SEntMan.GetComponent<TransformComponent>(ship).LocalRotation} velocity={SEntMan.GetComponent<PhysicsComponent>(ship).LinearVelocity} turn={SEntMan.GetComponent<PhysicsComponent>(ship).AngularVelocity}");
    }

    [TestPrototypes]
    private const string FlightPrototypes = @"
- type: entity
  parent: Thruster
  id: WFTestCrewThruster
  components:
  - type: ApcPowerReceiver
    needsPower: false
  - type: Thruster
    requireSpace: false
- type: entity
  parent: Gyroscope
  id: WFTestCrewGyroscope
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>The real client controls can construct and release their network subscriptions.</summary>
    [Test]
    public async Task CrewSetupWindowConstructs()
    {
        await Client.WaitAssertion(() =>
        {
            var window = new WFCrewSetupWindow();
            window.OpenCentered();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(WFCrewSetupWindow);
            var receive = type.GetMethod("Receive", flags)!;
            var order = (OptionButton) type.GetField("_order", flags)!.GetValue(window)!;
            var target = (OptionButton) type.GetField("_target", flags)!.GetValue(window)!;
            var targetLine = (BoxContainer) type.GetField("_targetLine", flags)!.GetValue(window)!;
            var source = new WFCrewSetupGrid(new NetEntity(101), "Ship");
            var destination = new WFCrewSetupGrid(new NetEntity(102), "Station");
            receive.Invoke(window, new object[] { new WFCrewSetupResponse
                { Action = WFCrewSetupAction.List, Grids = new() { source, destination } } });
            order.SelectId((int) WFPilotOrder.Dock);
            type.GetMethod("UpdateOrderFields", flags)!.Invoke(window, null);
            Assert.That(targetLine.Visible, Is.True);
            Assert.That(target.ItemCount, Is.EqualTo(2), "Placeholder and destination; own ship is excluded.");
            target.SelectId(1);
            receive.Invoke(window, new object[] { new WFCrewSetupResponse
                { Action = WFCrewSetupAction.List, Grids = new() { destination, source } } });
            Assert.That(target.SelectedId, Is.EqualTo(0), "Selection survives a reordered grid refresh.");
            window.Close();
            window.Dispose();
        });
        await RunTicks(5);
    }

    /// <summary>A pilot uses real thrusters to dock without the FTL fallback.</summary>
    [Test]
    public async Task PilotDocksWithThrusters()
    {
        var (deck, target, ownDock, _) = await CreateDockingPair(gravity: true);
        EntityUid pilot = default, helm = default;
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            helm = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(3.5f, 3.5f)));
            var transform = Server.System<SharedTransformSystem>();
            for (var index = 0; index < 4; index++)
            {
                var thruster = SEntMan.SpawnAtPosition("WFTestCrewThruster", new EntityCoordinates(deck, new Vector2(1.5f + index, 5.5f)));
                transform.SetLocalRotation(thruster, Angle.FromDegrees(index * 90));
                var thrust = SEntMan.GetComponent<ThrusterComponent>(thruster);
                Server.System<ThrusterSystem>().EnableThruster(thruster, thrust);
            }
            var gyro = SEntMan.SpawnAtPosition("WFTestCrewGyroscope", new EntityCoordinates(deck, new Vector2(5.5f, 4.5f)));
            Server.System<ThrusterSystem>().EnableThruster(gyro, SEntMan.GetComponent<ThrusterComponent>(gyro));
            pilot = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Pilot,
                new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "flight")!.Value;
            SEntMan.GetComponent<HTNComponent>(pilot).SleepPlayerCheckRangeOverride = 1000;
            var duty = SEntMan.GetComponent<WFPilotDutyComponent>(pilot);
            duty.DockStandoff = 15;
            Server.System<WFPilotDutySystem>().Dock(pilot, target);
        });
        await WaitUntil(() => SEntMan.GetComponent<DockingComponent>(ownDock).Docked, 9000,
            () => $"Thruster docking did not complete: {DescribePilot(pilot, helm)} pos={SEntMan.GetComponent<TransformComponent>(deck).LocalPosition} velocity={SEntMan.GetComponent<PhysicsComponent>(deck).LinearVelocity}");
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(pilot).Orders, Is.EqualTo(WFPilotOrder.Hold)));
    }
}
