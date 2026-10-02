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

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
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
