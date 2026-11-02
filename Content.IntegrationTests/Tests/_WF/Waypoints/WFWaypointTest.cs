#nullable enable annotations

using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.IntegrationTests.Tests._WF.EngineCompat;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Waypoints;
using Content.Client.Shuttles.UI;
using Content.Server._WF.Waypoints;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Waypoints;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Waypoints;

/// <summary>Checks waypoints reach every radar on the grid and a helm's panel really engages the autopilot.</summary>
[TestFixture]
public sealed class WFWaypointTest
{
    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }

    [Test]
    public async Task ServerKeepsTheListWithinItsLimits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var system = entities.System<WFWaypointSystem>();
            var scanner = entities.SpawnEntity("ComputerRadar", map.GridCoords);
            var floating = entities.SpawnEntity(null, new MapCoordinates(new Vector2(500, 500), map.MapId));

            Assert.That(system.TryAdd(floating, "Lost", Vector2.One), Is.False, "No grid means nowhere to keep it.");
            Assert.That(system.TryAdd(scanner, "Bad", new Vector2(float.NaN, 0)), Is.False);
            Assert.That(entities.HasComponent<WFWaypointsComponent>(map.Grid.Owner), Is.False);

            Assert.That(system.TryAdd(scanner, "  Rally ", new Vector2(10, 20)), Is.True);
            Assert.That(system.TryAdd(scanner, "", new Vector2(30, 40)), Is.True);
            var stored = entities.GetComponent<WFWaypointsComponent>(map.Grid.Owner).Waypoints;
            Assert.That(stored[0].Name, Is.EqualTo("Rally"));
            Assert.That(stored[1].Name, Is.EqualTo("WP-2"));
            Assert.That(stored[0].Map, Is.EqualTo(entities.GetNetEntity(map.MapUid)));

            Assert.That(system.TryRemove(scanner, stored[0].Id), Is.True);
            Assert.That(system.TryRemove(scanner, 999), Is.False);
            Assert.That(stored.Select(waypoint => waypoint.Name), Is.EqualTo(new[] { "WP-2" }));

            while (stored.Count < WFWaypointRules.MaxWaypoints)
                Assert.That(system.TryAdd(scanner, "", Vector2.Zero), Is.True);
            Assert.That(system.TryAdd(scanner, "", Vector2.Zero), Is.False, "The list has a cap.");

            entities.DeleteEntity(scanner);
            entities.DeleteEntity(floating);
            entities.RemoveComponent<WFWaypointsComponent>(map.Grid.Owner);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RadarCursorReadsTheWorldPositionUnderIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        await pair.RunTicksSync(5);
        var clientEntities = pair.Client.EntMan;
        await pair.Client.WaitAssertion(() =>
        {
            var grid = clientEntities.GetEntity(pair.Server.EntMan.GetNetEntity(map.Grid.Owner));
            using var radar = new ShuttleNavControl { WfFitInstrument = true, SetSize = new Vector2(400, 400), WorldRange = 40 };
            radar.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400, 400)));
            radar.SetMatrix(new EntityCoordinates(grid, new Vector2(3, 4)), Angle.Zero);
            Assert.That(radar.WfCockpitTryGetWorldToView(out var worldToView, out _), Is.True);

            var world = new Vector2(10, -6);
            var view = Vector2.Transform(world, worldToView) / radar.UIScale;
            var read = radar.WfCursorWorldPosition(view);
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.Value.X, Is.EqualTo(world.X).Within(0.01f));
            Assert.That(read.Value.Y, Is.EqualTo(world.Y).Within(0.01f));

            Assert.That(radar.WfScannerWorldPosition()!.Value.X, Is.EqualTo(3f).Within(0.01f),
                "A radar with no console reads the point it is centred on.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HelmPanelAddsDeletesAndFliesToWaypoints()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        EntityUid console = default, actor = default;
        await pair.Server.WaitAssertion(() =>
        {
            console = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            // An unpowered helm's planner would fail the move and clear the target before it can be read.
            entities.GetComponent<HTNComponent>(console).Enabled = false;
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            entities.System<SharedUserInterfaceSystem>().OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(30);

        WFWaypointPanel Panel() => Descendants(pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot)
            .OfType<ShuttleConsoleWindow>().Last().FindControl<NavScreen>("NavContainer")
            .FindControl<ShuttleNavControl>("NavRadar").Parent!.Children.OfType<WFWaypointPanel>().Single();

        T Find<T>(string name) where T : Control => Descendants(Panel()).OfType<T>().Single(control => control.Name == name);

        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Panel().Visible, Is.False, "The panel starts closed.");
            var navRadar = Panel().Parent!;
            var key = navRadar.Children.OfType<Button>().Single(button => button.Name == "WfWaypointKey");
            Assert.That(key.Visible, Is.True, "A helm on a grid offers waypoints.");
            WFButtonTestInput.Toggle(key, true);
            Assert.That(Panel().Visible, Is.True, "The key opens the panel.");
        });
        await pair.RunTicksSync(5);

        await pair.Client.WaitAssertion(() =>
        {
            Find<LineEdit>("WfWaypointName").Text = "Rally";
            Find<LineEdit>("WfWaypointX").Text = "not a number";
            Find<LineEdit>("WfWaypointY").Text = "20";
            Press(Find<Button>("WfWaypointAdd"));
            Assert.That(Find<Label>("WfWaypointStatus").Visible, Is.True, "A bad coordinate is reported.");

            Find<LineEdit>("WfWaypointX").Text = "10.5";
            Press(Find<Button>("WfWaypointAdd"));
        });
        await pair.RunTicksSync(30);

        await pair.Server.WaitAssertion(() =>
        {
            var stored = entities.GetComponent<WFWaypointsComponent>(map.Grid.Owner).Waypoints;
            Assert.That(stored.Select(waypoint => (waypoint.Name, waypoint.Position)),
                Is.EqualTo(new[] { ("Rally", new Vector2(10.5f, 20)) }));
        });

        await pair.Client.WaitAssertion(() =>
        {
            var grid = pair.Client.EntMan.GetEntity(entities.GetNetEntity(map.Grid.Owner));
            Assert.That(pair.Client.EntMan.GetComponent<WFWaypointsComponent>(grid).Waypoints, Has.Count.EqualTo(1),
                "The list reaches the client.");
            Press(Find<Button>("WfWaypointFly1"));
        });
        await pair.RunTicksSync(30);

        await pair.Server.WaitAssertion(() =>
        {
            var helm = entities.GetComponent<ShuttleConsoleComponent>(console);
            var blackboard = entities.GetComponent<HTNComponent>(console).Blackboard;
            var targetSet = blackboard.TryGetValue<EntityCoordinates>(helm.AutopilotTargetKey, out var target, entities);
            Assert.That(targetSet, Is.True, "Go sets the autopilot's target.");
            var world = entities.System<SharedTransformSystem>().ToMapCoordinates(target);
            Assert.That(world.MapId, Is.EqualTo(map.MapId));
            Assert.That(world.Position, Is.EqualTo(new Vector2(10.5f, 20)));
            Assert.That(blackboard.TryGetValue<Angle>(helm.AutopilotRotationKey, out var heading, entities), Is.True);
            var shipRotation = entities.System<SharedTransformSystem>().GetWorldRotation(map.Grid.Owner);
            Assert.That(heading.Theta, Is.EqualTo((shipRotation + Math.PI).Theta).Within(0.001), "The ship keeps its heading.");
        });

        await pair.Client.WaitAssertion(() => Press(Find<Button>("WfWaypointRemove1")));
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WFWaypointsComponent>(map.Grid.Owner).Waypoints, Is.Empty);
            entities.System<SharedUserInterfaceSystem>().CloseUi(console, ShuttleConsoleUiKey.Key, actor);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entities.DeleteEntity(console);
            entities.DeleteEntity(actor);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MassScannerManagesWaypointsWithoutAutopilot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        EntityUid scanner = default, actor = default;
        await pair.Server.WaitAssertion(() =>
        {
            scanner = entities.SpawnEntity("ComputerRadar", map.GridCoords);
            actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            entities.System<WFWaypointSystem>().TryAdd(scanner, "Rally", new Vector2(1, 2));
            entities.System<SharedUserInterfaceSystem>().OpenUi(scanner, RadarConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(30);

        await pair.Client.WaitAssertion(() =>
        {
            var window = Descendants(pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot)
                .OfType<RadarConsoleWindow>().Last();
            var layers = window.FindControl<ShuttleNavControl>("RadarScreen").Parent!;
            WFButtonTestInput.Toggle(layers.Children.OfType<Button>().Single(button => button.Name == "WfWaypointKey"), true);
        });
        await pair.RunTicksSync(5);

        await pair.Client.WaitAssertion(() =>
        {
            var window = Descendants(pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot)
                .OfType<RadarConsoleWindow>().Last();
            var panel = window.FindControl<ShuttleNavControl>("RadarScreen").Parent!.Children.OfType<WFWaypointPanel>().Single();
            Button Button(string name) => Descendants(panel).OfType<Button>().Single(control => control.Name == name);
            Assert.That(Button("WfWaypointFly").Visible, Is.False, "A mass scanner has no autopilot to send.");
            Assert.That(Button("WfWaypointFly1").Visible, Is.False);
            Press(Button("WfWaypointRemove1"));
        });
        await pair.RunTicksSync(30);

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WFWaypointsComponent>(map.Grid.Owner).Waypoints, Is.Empty,
                "A mass scanner can delete from the list.");
            entities.System<SharedUserInterfaceSystem>().CloseUi(scanner, RadarConsoleUiKey.Key, actor);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entities.DeleteEntity(scanner);
            entities.DeleteEntity(actor);
        });
        await pair.CleanReturnAsync();
    }

    private static void Press(BaseButton button) => WFButtonTestInput.Click(button);
}
