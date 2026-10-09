using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.Cockpit;
using Content.Client.Shuttles.UI;
using Content.Server._Mono.NPC.HTN;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared._WF.ShipShields;
using Content.Shared.NPC;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Checks cockpit lamps distinguish actual operations from requested or stale settings.</summary>
[TestFixture]
public sealed class WFCockpitStatusTest
{
    [Test]
    public void StatusMappingDoesNotInventReadinessOrDeployment()
    {
        Assert.That(WFCockpitStatusReading.Ftl(FTLState.Invalid).State, Is.EqualTo(WFCockpitLampState.Unavailable));
        Assert.That(WFCockpitStatusReading.Ftl(FTLState.Available).State, Is.EqualTo(WFCockpitLampState.Good));
        Assert.That(WFCockpitStatusReading.Ftl(FTLState.Travelling).State, Is.EqualTo(WFCockpitLampState.Active));
        Assert.That(WFCockpitStatusReading.Ftl(FTLState.Cooldown).State, Is.EqualTo(WFCockpitLampState.Caution));
        Assert.That(WFCockpitStatusReading.Autopilot(null).State, Is.EqualTo(WFCockpitLampState.Unavailable));
        Assert.That(WFCockpitStatusReading.Dampening(InertiaDampeningMode.Station, false).State, Is.EqualTo(WFCockpitLampState.Unavailable));
        Assert.That(WFCockpitStatusReading.Dampening(InertiaDampeningMode.Dampen, false).State, Is.EqualTo(WFCockpitLampState.Good));
        Assert.That(WFCockpitStatusReading.Dampening(InertiaDampeningMode.Dampen, true).State, Is.EqualTo(WFCockpitLampState.Off));
        Assert.That(WFCockpitStatusReading.Dampening(InertiaDampeningMode.Anchor, true).State, Is.EqualTo(WFCockpitLampState.Good));
        Assert.That(WFCockpitStatusReading.Docked(null).State, Is.EqualTo(WFCockpitLampState.Unavailable));
        Assert.That(WFCockpitStatusReading.Docked(false).State, Is.EqualTo(WFCockpitLampState.Off));
        Assert.That(WFCockpitStatusReading.Docked(true).State, Is.EqualTo(WFCockpitLampState.Good));
        var shield = new WFShipShieldShuntState(true, false, 0.6f, 0, 0, MathF.PI);
        Assert.That(WFCockpitStatusReading.Shield(shield).State, Is.EqualTo(WFCockpitLampState.Caution),
            "Enabling the emitter is not proof that its field has deployed.");
        shield.Enabled = false;
        Assert.That(WFCockpitStatusReading.Shield(shield).State, Is.EqualTo(WFCockpitLampState.Off));
        shield.Active = true;
        Assert.That(WFCockpitStatusReading.Shield(shield).State, Is.EqualTo(WFCockpitLampState.Good),
            "Until it disappears, an existing field remains the authoritative deployed state.");
        shield.Available = false;
        Assert.That(WFCockpitStatusReading.Shield(shield).State, Is.EqualTo(WFCockpitLampState.Unavailable));
    }

    [Test]
    public async Task AutopilotReportsRealSteeringAndRefreshesWithoutReplacingNavigation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var console = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            var actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            var lateActor = entities.SpawnEntity("MobHuman", map.GridCoords);
            var helms = entities.System<ShuttleConsoleSystem>();
            var htn = entities.GetComponent<HTNComponent>(console);
            var helm = entities.GetComponent<ShuttleConsoleComponent>(console);
            var receiver = entities.GetComponent<ApcPowerReceiverComponent>(console);
            var transform = entities.GetComponent<TransformComponent>(console);
            receiver.Powered = true;
            htn.Enabled = true;
            entities.EnsureComponent<ActiveNPCComponent>(console);
            entities.EnsureComponent<ShuttleComponent>(map.Grid.Owner);
            var inputs = entities.EnsureComponent<PilotedShuttleComponent>(map.Grid.Owner).InputSources;
            Assert.That(transform.Anchored, Is.True);
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False);
            htn.Blackboard.SetValue(helm.AutopilotTargetKey, new EntityCoordinates(map.Grid.Owner, new Vector2(50, 0)));
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False, "A selected or stale target must not light the autopilot lamp.");
            var steering = entities.EnsureComponent<ShipSteererComponent>(console);
            steering.Status = ShipSteeringStatus.Moving;
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False, "Steering must be registered as an actual ship input source.");
            inputs.Add(console);
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.True);
            var ui = entities.System<SharedUserInterfaceSystem>();
            Assert.That(ui.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var initial), Is.True);
            Assert.That(initial!.CockpitAutopilotActive, Is.False, "The cached startup snapshot predates active steering.");
            var snapshots = (Dictionary<EntityUid, bool?>) typeof(ShuttleConsoleSystem)
                .GetField("_wfCockpitAutopilotStates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(helms)!;
            var viewers = (Dictionary<EntityUid, HashSet<EntityUid>>) typeof(ShuttleConsoleSystem)
                .GetField("_wfCockpitStatusViewers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(helms)!;
            var shields = (Dictionary<EntityUid, WFShipShieldShuntState>) typeof(ShuttleConsoleSystem)
                .GetField("_wfShieldHelmStates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(helms)!;
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.True, "Opening the helm must not reset autopilot steering.");
            Assert.That(viewers[console], Does.Contain(actor));
            helms.Update(0.3f);
            Assert.That(snapshots[console], Is.True);
            Assert.That(viewers, Is.Empty);
            Assert.That(shields.ContainsKey(console), Is.True, "The existing viewer already received the shield snapshot.");
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, lateActor);
            Assert.That(shields.ContainsKey(console), Is.False, "Every new or reopened viewer must trigger a shield-only refresh.");
            Assert.That(viewers[console], Is.EquivalentTo(new[] { lateActor }), "A later viewer needs a fresh targeted lamp update despite unchanged activity.");
            Assert.That(snapshots[console], Is.True);
            helms.Update(0.3f);
            Assert.That(viewers, Is.Empty, "The targeted refresh is serviced on the next status tick.");
            Assert.That(shields.ContainsKey(console), Is.True, "The shield updater must service the new viewer on its next tick.");
            Assert.That(ui.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var afterJoin), Is.True);
            Assert.That(afterJoin, Is.SameAs(initial), "Joining must not replace another viewer's navigation payload.");
            Assert.That(afterJoin!.CockpitAutopilotActive, Is.False, "The late viewer must be refreshed independently of the stale cached snapshot.");
            ui.CloseUi(console, ShuttleConsoleUiKey.Key, lateActor);
            Assert.That(shields.ContainsKey(console), Is.True, "The existing viewer already received the shield snapshot.");
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, lateActor);
            Assert.That(shields.ContainsKey(console), Is.False, "Every new or reopened viewer must trigger a shield-only refresh.");
            Assert.That(viewers[console], Does.Contain(lateActor), "Closing and reopening between ticks still requires a refresh.");
            helms.Update(0.3f);
            Assert.That(viewers, Is.Empty);
            ui.CloseUi(console, ShuttleConsoleUiKey.Key, lateActor);
            Assert.That(shields.ContainsKey(console), Is.True, "The existing viewer already received the shield snapshot.");
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, lateActor);
            Assert.That(shields.ContainsKey(console), Is.False, "Every new or reopened viewer must trigger a shield-only refresh.");
            ui.CloseUi(console, ShuttleConsoleUiKey.Key, lateActor);
            helms.Update(0.3f);
            Assert.That(viewers, Is.Empty, "Closing before the scheduled update must not retain the viewer.");
            steering.Status = ShipSteeringStatus.InRange;
            helms.Update(0.3f);
            Assert.That(snapshots[console], Is.False, "Arrival must turn the lamp off while navigation stays open.");
            Assert.That(ui.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var afterArrival), Is.True);
            Assert.That(afterArrival, Is.SameAs(initial), "Status-only updates must preserve the live navigation payload.");
            steering.Status = ShipSteeringStatus.Moving;
            htn.Enabled = false;
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False, "Disabled planning must not advertise a stale steering component.");
            htn.Enabled = true;
            receiver.Powered = false;
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False);
            receiver.Powered = true;
            inputs.Remove(console);
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False);
            entities.RemoveComponent<ShipSteererComponent>(console);
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False);
            Assert.That(helms.GetWfCockpitAutopilotStatus(actor), Is.Null, "A console without autopilot support must remain unavailable.");
            ui.CloseUi(console, ShuttleConsoleUiKey.Key, actor);
            helms.Update(0.3f);
            Assert.That(snapshots.ContainsKey(console), Is.False, "Closed helms must release status tracking.");
            Assert.That(shields.ContainsKey(console), Is.False, "Closed helms must also release shield status tracking.");
            entities.DeleteEntity(console);
            entities.DeleteEntity(actor);
            entities.DeleteEntity(lateActor);
        });
        await pair.Client.WaitAssertion(() =>
        {
            using var console = new ShuttleConsoleWindow();
            using var bank = console.WfCockpitStatus();
            var lamp = bank.Children.OfType<WFCockpitStatusLamp>().Single(control => control.Name == "wf-cockpit-status-autopilot");
            var navigation = console.FindControl<NavScreen>("NavContainer");
            Assert.That(lamp.Reading.State, Is.EqualTo(WFCockpitLampState.Unavailable));
            console.WfUpdateCockpitAutopilot(true);
            Assert.That(lamp.Reading.State, Is.EqualTo(WFCockpitLampState.Active));
            Assert.That(navigation.Visible, Is.True, "Autopilot telemetry must not open the strategic MFD.");
            console.WfUpdateCockpitAutopilot(false);
            Assert.That(lamp.Reading.State, Is.EqualTo(WFCockpitLampState.Off));
            Assert.That(navigation.Visible, Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
