#nullable enable
using System.Numerics;
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
