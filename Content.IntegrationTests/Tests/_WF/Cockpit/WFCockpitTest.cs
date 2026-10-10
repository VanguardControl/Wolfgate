#nullable enable annotations

using Content.Shared._Mono.FireControl;
using Content.Shared.Shuttles.BUIStates;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.Client._WF.ShipShields;
using Content.Shared.CCVar;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.Info;
using Content.Client._WF.Shuttles.UI;
using Content.Client.UserInterface.Systems.Inventory;
using Content.Client.UserInterface.Systems.Inventory.Widgets;
using Content.Client.UserInterface.Systems.Actions.Widgets;
using Content.Client.UserInterface.Systems.Alerts.Controls;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Client.UserInterface.Systems.Ghost.Widgets;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Cockpit;
using Content.Shared._WF.Shuttles;
using Content.Shared._WF.ShipShields;
using Content.Shared.Buckle;
using Content.Shared.Inventory;
using Content.Shared.Buckle.Components;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Exercises seated entry, live HUD reparenting and automatic restoration after unbuckling.</summary>
public sealed class WFCockpitTest : InteractionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: InteractionTestMob
  id: WFCockpitTestMob
  components:
  - type: ContentEye
  - type: Sprite
    sprite: Mobs/Species/Human/parts.rsi
    layers:
    - state: torso_m
    - map: [""jumpsuit""]
  - type: Buckle
  - type: Alerts
  - type: Inventory
  - type: InventorySlots
  - type: ContainerContainer
";
    protected override string PlayerPrototype => "WFCockpitTestMob";

    [Test]
    public async Task SeatedCockpitRestoresHudAndBindings()
    {
        EntityUid console = default;
        EntityUid chair = default;
        EntityUid uniform = default;
        await Server.WaitAssertion(() =>
        {
            console = SEntMan.SpawnEntity("ComputerShuttle", MapData.GridCoords);
            chair = SEntMan.SpawnEntity("Chair", MapData.GridCoords);
            uniform = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", MapData.GridCoords);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(SPlayer, uniform, "jumpsuit"), Is.True);
            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            SEntMan.System<ShuttleConsoleSystem>().AddPilot(console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(console));
            var cockpit = SEntMan.System<SharedWFCockpitSystem>();
            Assert.That(cockpit.CanEnter(SPlayer, console), Is.False, "Standing pilots cannot enter cockpit mode.");
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True);
            Assert.That(cockpit.CanEnter(SPlayer, console), Is.True);
            Assert.That(cockpit.CanEnter(SPlayer, chair), Is.False, "Another console cannot borrow this piloting session.");
            SEntMan.RemoveComponent<WFCockpitSeatComponent>(chair);
            Assert.That(cockpit.CanEnter(SPlayer, console), Is.False, "A bare strap without seat classification must remain insufficient.");
            SEntMan.AddComponent<WFCockpitSeatComponent>(chair);
        });
        await RunTicks(10);
        ShuttleConsoleWindow window = default!;
        InGameScreen screen = default!;
        Control[] normalHud = default!;
        ChatBox chat = default!;
        MainViewport viewport = default!;
        Control viewportParent = default!;
        Control chatParent = default!;
        InventoryGui inventory = default!;
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
        var originalLayout = settings.GetCVar(CCVars.UILayout);
        await Client.WaitAssertion(() => settings.SetCVar(CCVars.UILayout, "Default"));
        await RunTicks(3);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            screen = (InGameScreen) ui.ActiveScreen!;
            inventory = screen.GetWidget<InventoryGui>()!;
            Assert.That(inventory.InventoryButton.Visible, Is.True, "The fixture must exercise a populated character inventory.");
            if (!inventory.InventoryHotbar.Visible)
                ui.GetUIController<InventoryUIController>().ToggleInventoryBar();
            Assert.That(inventory.InventoryHotbar.TryGetButton("jumpsuit", out var clothing), Is.True);
            Assert.That(clothing!.Entity, Is.EqualTo(ToClient(SEntMan.GetNetEntity(uniform))));
            Layout(screen, new Vector2(1130, 636));
            Assert.That(inventory.InventoryHotbar.Height, Is.GreaterThan(150), "The open clothing grid must contribute to the resize case.");
            normalHud = screen.Children.ToArray();
            chat = screen.ChatBox;
            viewport = screen.GetWidget<MainViewport>()!;
            viewportParent = viewport.Parent!;
            chatParent = chat.Parent!;
            var alerts = screen.GetWidget<AlertsUI>()!;
            var alertsParent = alerts.Parent;
            var votes = Named<BoxContainer>(screen, "VoteMenu");
            var votesParent = votes.Parent;
            var speech = (LayoutContainer) typeof(ChatUIController).GetField("_speechBubbleRoot", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(ui.GetUIController<ChatUIController>())!;
            var speechParent = speech.Parent;
            chat.ChatInput.Input.Text = "unfinished cockpit transmission";
            window = new ShuttleConsoleWindow();
            var gunMessages = new List<BoundUserInterfaceMessage>();
            window.WfCockpitGunneryCommand += gunMessages.Add;
            window.WfSetCockpitConsole(ToClient(SEntMan.GetNetEntity(console)));
            window.UpdateShieldShuntSnapshot(new WFShipShieldShuntState(true, true, 0.76f, MathF.PI / 2f, 0.65f, MathF.PI / 2f));
            var cameraRequests = new List<(ShuttleCameraView View, float Zoom, bool LowLight)>();
            window.ShipCameraRequested += (view, zoom, lowLight) => cameraRequests.Add((view, zoom, lowLight));
            window.OpenCentered();
            var controller = ui.GetUIController<WFCockpitUIController>();
            var plots = Descendants(window).OfType<MapGridControl>().ToArray();
            var originalPlotControls = plots.Select(plot => plot.WfCockpitControls).ToArray();
            var networkPorts = Named<GridContainer>(window, "NetworkPortsBox");
            var originalNetworkLayout = (networkPorts.LimitedDimension, networkPorts.Rows, networkPorts.Columns);
            Assert.That(controller.Enter(window), Is.True);
            Assert.That(cameraRequests.Single(), Is.EqualTo((ShuttleCameraView.External, 1.5f, false)),
                "Cockpit entry must request the external camera through the normal helm binding.");
            Assert.That(window.Visible, Is.False);
            Assert.That(screen.GetWidget<MainViewport>(), Is.SameAs(viewport));
            Assert.That(screen.GetWidget<ChatBox>() ?? screen.GetWidget<ResizableChatBox>(), Is.SameAs(chat));
            Assert.That(chat.ChatInput.Input.Text, Is.EqualTo("unfinished cockpit transmission"));
            Assert.That(normalHud.Where(control => control != chat && control != viewport && control is not AlertsUI)
                .All(control => !control.VisibleInTree), Is.True, "Character HUD controls must stay hidden.");
            var hud = screen.Children.OfType<WFCockpitView>().Single();
            Assert.That(alerts.VisibleInTree, Is.True, "Crew status alerts must remain available in cockpit.");
            Assert.That(votes.VisibleInTree, Is.True, "Live votes must not be hidden with character hotkeys.");
            Assert.That(speech.VisibleInTree, Is.True, "Speech bubbles must continue updating in cockpit.");
            Assert.That(speech.Parent!.Name, Is.EqualTo("CockpitSpeechBubbles"));
            var recentWindows = ui.GetUIController<CloseRecentWindowUIController>();
            using (var popup = new DefaultWindow())
            {
                popup.OpenCentered();
                recentWindows.SetMostRecentlyInteractedWindow(window);
                Assert.That(recentWindows.HasClosableWindow(), Is.True);
                recentWindows.CloseMostRecentWindow();
                Assert.That(popup.IsOpen, Is.False, "Escape must close a real popup behind the hidden helm entry.");
                Assert.That(window.IsOpen, Is.True);
                Assert.That(controller.Active, Is.True, "Escape must not close the hidden helm that supplies cockpit controls.");
                Assert.That(recentWindows.HasClosableWindow(), Is.False, "The hidden helm alone is not an Escape-closeable window.");
            }
            Layout(hud, new Vector2(1130, 636));
            var crewAlerts = Named<ScrollContainer>(hud, "CockpitCrewAlerts");
            var alertControls = alerts.AlertContainer.Children.OfType<AlertControl>().ToArray();
            Assert.That(alertControls, Is.Not.Empty, "The buckled pilot must have a live alert for the bank to show.");
            Assert.That(crewAlerts.Width, Is.GreaterThan(0), "The docked alert bank must not collapse to nothing.");
            Assert.That(crewAlerts.Height, Is.GreaterThan(0), "The docked alert bank must not collapse to nothing.");
            Assert.That(crewAlerts.GlobalPosition.X, Is.GreaterThanOrEqualTo(viewport.GlobalPosition.X - 1));
            Assert.That(crewAlerts.GlobalPosition.Y, Is.GreaterThanOrEqualTo(viewport.GlobalPosition.Y - 1));
            Assert.That(Right(crewAlerts), Is.LessThanOrEqualTo(Right(viewport) + 1));
            Assert.That(Bottom(crewAlerts), Is.LessThanOrEqualTo(Bottom(viewport) + 1));
            Assert.That(alertControls[0].Height, Is.GreaterThan(0), "A docked alert must be drawn and clickable.");
            Assert.That(alertControls[0].GlobalPosition.Y, Is.GreaterThanOrEqualTo(crewAlerts.GlobalPosition.Y - 1));
            Assert.That(Bottom(alertControls[0]), Is.LessThanOrEqualTo(Bottom(crewAlerts) + 1),
                "The first alert must lie inside the bank's clip rectangle.");
            Assert.That(inventory.InventoryHotbar.Visible, Is.True, "Cockpit entry must retain the open clothing panel's state.");
            Assert.That(inventory.InventoryHotbar.VisibleInTree, Is.False);
            var actions = screen.GetWidget<ActionsBar>()!;
            Assert.That(actions.ActionsContainer.Rows, Is.GreaterThanOrEqualTo(1),
                "A world viewport shorter than the retained inventory must retain a usable action-grid row limit.");
            Assert.That(float.IsFinite(viewport.Height) && viewport.Height > 0, Is.True,
                "Resizing with open clothing must complete with a valid world viewport.");
            Assert.That(viewport.Height, Is.LessThan(inventory.Height + 40),
                "The fixture must actually exercise the small-viewport/open-inventory resize case.");
            var navigation = Named<NavScreen>(window, "NavContainer");
            var velocity = Named<WFVelocityVectorInstrument>(hud, "CockpitVelocity");
            Assert.That(velocity.Reading, Is.Null, "An unbound helm must not invent a stopped velocity sample.");
            var ship = ToClient(SEntMan.GetNetEntity(MapData.Grid.Owner));
            var physics = CEntMan.System<SharedPhysicsSystem>();
            var transform = CEntMan.System<SharedTransformSystem>();
            var body = CEntMan.GetComponent<PhysicsComponent>(ship);
            var originalBodyType = body.BodyType;
            var originalVelocity = body.LinearVelocity;
            var originalCenter = body.LocalCenter;
            var originalRotation = transform.GetWorldRotation(ship);
            try
            {
                navigation.SetShuttle(ship);
                physics.SetBodyType(ship, BodyType.Dynamic);
                var rotation = Angle.FromDegrees(61);
                transform.SetWorldRotation(ship, rotation);
                physics.SetLinearVelocity(ship, rotation.RotateVec(new Vector2(3, -4)));
                var reading = velocity.Reading;
                Assert.That(reading, Is.Not.Null);
                Assert.That(reading!.Value.Speed, Is.EqualTo(5).Within(0.0001f));
                Assert.That(reading.Value.ScreenDirection!.Value.X, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(reading.Value.ScreenDirection.Value.Y, Is.EqualTo(0.8f).Within(0.0001f),
                    "The live NavScreen source must report aft/starboard drift in the ship bow frame.");
                physics.SetLinearVelocity(ship, Vector2.Zero);
                Assert.That(velocity.Reading!.Value.ScreenDirection, Is.Null, "The same live instrument must clear its pointer when stopped.");
                physics.SetLocalCenter(ship, body, new Vector2(3, 2));
                navigation.WfCockpitRefresh();
                var center = Vector2.Transform(body.LocalCenter, transform.GetWorldMatrix(ship));
                Assert.That(navigation.FindControl<Label>("GridPosition").Text,
                    Does.Contain($"{center.X:0.0}").And.Contain($"{center.Y:0.0}"),
                    "The position readout must report the centre of mass, as the windowed helm does.");
            }
            finally
            {
                physics.SetLocalCenter(ship, body, originalCenter);
                physics.SetLinearVelocity(ship, originalVelocity);
                physics.SetBodyType(ship, originalBodyType);
                transform.SetWorldRotation(ship, originalRotation);
            }
            Assert.That(networkPorts.Columns, Is.EqualTo(2), "Auxiliary device buttons must fit the compact MFD.");
            Assert.That(plots, Has.Length.GreaterThanOrEqualTo(5));
            Assert.That(plots.All(plot => plot.WfCockpitControls), Is.True,
                "Navigation, hull, strategic, docking and access plots must share cockpit interactions.");
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                AssertFuelBinding(Named<ShipScreen>(hud, "ShipContainer"), hud);
                foreach (var size in new[] { new Vector2(1130, 636), new Vector2(1600, 900) })
                {
                    var expand = Named<Button>(hud, "CockpitMfdExpand");
                    var mfd = Named<Control>(hud, "CockpitMfd");
                    var collapsedWidth = 0f;
                    foreach (var expanded in new[] { false, true })
                    {
                        Toggle(expand, expanded);
                        foreach (var page in new[] { "wf-cockpit-nav", "wf-cockpit-ship", "wf-cockpit-map", "wf-cockpit-dock", "wf-cockpit-access", "wf-cockpit-shield-page", "wf-cockpit-systems", "wf-cockpit-alarms" })
                        {
                            hud.SelectPage(page);
                            Layout(hud, size);
                            AssertCockpitLayout(hud, viewport, chat, ui, size, theme, page);
                            AssertLabelFits(expand, ui, $"{theme}, {size}, expanded={expanded}");
                            if (page == "wf-cockpit-access")
                            {
                                var accessLayout = Named<Control>(hud, "WfAccessLayout");
                                var accessPlot = Named<Control>(hud, "WfAccessPlot");
                                var accessDetails = Named<Control>(hud, "WfAccessDetails");
                                var doorMap = Named<MapGridControl>(hud, "DoorMap");
                                Assert.That(Ancestors(doorMap, accessPlot).OfType<WFScreenBezel>(), Is.Not.Empty,
                                    "The access diagram must retain the same instrument bezel as the other plots in both themes.");
                                Assert.That(Right(accessPlot), Is.LessThanOrEqualTo(Right(accessLayout) + 1));
                                Assert.That(Bottom(accessPlot), Is.LessThanOrEqualTo(Bottom(accessLayout) + 1));
                                if (expanded && size.X == 1600)
                                {
                                    Assert.That(accessDetails.GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(accessPlot)),
                                        "An expanded access MFD must use its width for controls beside the diagram.");
                                    Assert.That(accessPlot.Width, Is.GreaterThan(accessDetails.Width),
                                        "The door diagram must be the largest panel in an expanded access MFD.");
                                }
                                else
                                {
                                    Assert.That(accessDetails.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(accessPlot)),
                                        "Compact access MFDs must keep their controls below the fixed diagram.");
                                    Assert.That(accessPlot.Height, Is.GreaterThan(accessDetails.Height * 1.4f),
                                        "Compact access MFDs must give the door diagram most of their height.");
                                }
                            }
                            if (page == "wf-cockpit-ship")
                            {
                                var hullLayout = Named<WFCockpitShipLayout>(hud, "CockpitShipLayout");
                                var hullPlot = hullLayout.Children.First();
                                var hullDetails = hullLayout.Children.Last();
                                if (expanded && size.X == 1600)
                                {
                                    Assert.That(hullDetails.Position.X, Is.GreaterThanOrEqualTo(hullPlot.Position.X + hullPlot.Width),
                                        "An expanded ship MFD must use its width for details beside the plot.");
                                    Assert.That(hullPlot.Width / (hullPlot.Width + hullDetails.Width), Is.InRange(0.45f, 0.55f),
                                        "Expanded ship MFDs must give the hull plot about half of their width.");
                                }
                                else
                                {
                                    Assert.That(hullDetails.Position.Y, Is.GreaterThanOrEqualTo(hullPlot.Position.Y + hullPlot.Height),
                                        "Compact ship MFDs must stack details below the readable hull plot.");
                                }
                                var context = $"{theme}, {size}, expanded={expanded}, ship details={hullDetails.Size}";
                                var telemetry = Named<GridContainer>(hud, "CockpitShipTelemetry");
                                Assert.That(telemetry.Columns, Is.EqualTo(2), context);
                                var gauges = telemetry.Children.OfType<WFGlassGauge>().ToArray();
                                Assert.That(gauges, Has.Length.EqualTo(6), context);
                                foreach (var gauge in gauges)
                                {
                                    Assert.That(gauge.GlobalPosition.X, Is.GreaterThanOrEqualTo(hullDetails.GlobalPosition.X - 1), context);
                                    Assert.That(Right(gauge), Is.LessThanOrEqualTo(Right(hullDetails) + 1), context);
                                }
                                for (var index = 0; index < gauges.Length; index += 2)
                                {
                                    Assert.That(gauges[index + 1].GlobalPosition.Y, Is.EqualTo(gauges[index].GlobalPosition.Y).Within(1), context);
                                    Assert.That(gauges[index + 1].GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(gauges[index]) - 1), context);
                                    if (index > 0)
                                        Assert.That(gauges[index].GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(gauges[index - 2]) - 1), context);
                                }
                                var overlays = Named<GridContainer>(hud, "CockpitShipOverlays");
                                Assert.That(overlays.Columns, Is.EqualTo(4), context);
                                Assert.That(overlays.Children.Select(control => control.Name),
                                    Is.EquivalentTo(new[] { "DamageToggle", "FireToggle", "PressureToggle", "PowerToggle" }), context);
                                var mapControls = Named<Control>(hud, "CockpitShipMapControls");
                                Assert.That(mapControls.Children.Select(control => control.Name),
                                    Is.EquivalentTo(new[] { "DepartmentToggle", "FitButton" }), context);
                                foreach (var row in new Control[] { overlays, mapControls })
                                {
                                    var controls = row.Children.ToArray();
                                    foreach (var control in controls)
                                    {
                                        Assert.That(control.VisibleInTree, Is.True, context);
                                        Assert.That(control.GlobalPosition.Y, Is.EqualTo(controls[0].GlobalPosition.Y).Within(1), context);
                                        Assert.That(control.GlobalPosition.X, Is.GreaterThanOrEqualTo(hullDetails.GlobalPosition.X - 1), context);
                                        Assert.That(Right(control), Is.LessThanOrEqualTo(Right(hullDetails) + 1), context);
                                        Assert.That(Bottom(control), Is.LessThanOrEqualTo(Bottom(row) + 1), context);
                                        if (control is CheckBox check)
                                            AssertLabelFits(check.Label, ui, context);
                                        else
                                            AssertLabelFits((Button) control, ui, context);
                                    }
                                }
                            }
                        }
                        if (expanded)
                            Assert.That(mfd.Width, Is.GreaterThan(collapsedWidth), "Expand must give every MFD page more plotting space.");
                        else
                            collapsedWidth = mfd.Width;
                    }
                    Toggle(expand, false);
                    Layout(hud, size);
                    Assert.That(mfd.Width, Is.EqualTo(collapsedWidth).Within(1), "Collapse must restore the previous HUD balance.");
                }
            }
            hud.SelectPage("wf-cockpit-nav");
            foreach (var narrow in new[] { new Vector2(1090, 636), new Vector2(1097, 617) })
            {
                Layout(hud, narrow);
                Assert.That(Named<Button>(hud, "CockpitMfdExpand").Disabled, Is.True,
                    $"{narrow}: Expand cannot widen the MFD in a window this narrow, so it must not be offered.");
            }
            Layout(hud, new Vector2(1130, 636));
            Assert.That(Named<Button>(hud, "CockpitMfdExpand").Disabled, Is.False, "Expand returns once it can widen the MFD.");
            var initialSession = gunMessages.OfType<WFCockpitGunnerySessionMessage>().Single();
            Assert.That(initialSession.Active, Is.True);
            Assert.That(initialSession.Controlling, Is.False, "Entering FLIGHT must discover guns without claiming them.");
            Assert.That(Named<Control>(hud, "CockpitGunneryModes").Visible, Is.False,
                "Cockpits without an authorized nearby gun console keep the original flight layout.");
            var weapon = new FireControllableEntry(new NetEntity(910), default, "Battery", 10, true);
            var gunState = new FireControlConsoleBoundInterfaceState(true, new[] { weapon },
                new NavInterfaceState(250, null, null, new(), default));
            var linked = new NetEntity(911);
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, gunState));
            Assert.That(Named<Control>(hud, "CockpitGunneryModes").Visible, Is.True);
            hud.SelectPage("wf-cockpit-nav");
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            foreach (var size in new[] { new Vector2(1130, 636), new Vector2(1600, 900), new Vector2(1920, 1080) })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                Layout(hud, size);
                AssertCockpitLayout(hud, viewport, chat, ui, size, theme, "wf-cockpit-nav");
                var fuel = Named<WFCockpitFuelBank>(hud, "CockpitFuelBank");
                var fixedPosition = fuel.GlobalPosition;
                var scroll = Named<ScrollContainer>(hud, "CockpitInstrumentScroll");
                AssertInstrumentScrollInput(hud, ui, size, theme);
                scroll.SetScrollValue(new Vector2(0, 10000));
                Layout(hud, size);
                AssertFuelVisible(hud);
                Assert.That(fuel.GlobalPosition, Is.EqualTo(fixedPosition),
                    "Scrolling the large dials must never move the fuel gauge or its warning lamp.");
                Assert.That(Bottom(Named<WFGlassGauge>(hud, "CockpitHull")), Is.LessThanOrEqualTo(fuel.GlobalPosition.Y + 1),
                    "The hull strip remains the last scrolling instrument immediately above fixed fuel.");
                scroll.SetScrollValue(Vector2.Zero);
            }
            hud.SelectGunnery(true);
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Last().Controlling, Is.True,
                "GUNS must explicitly request firing control.");
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            foreach (var size in new[] { new Vector2(1130, 636), new Vector2(1600, 900) })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                Layout(hud, size);
                var guns = Named<Control>(hud, "CockpitGunnery");
                Assert.That(guns.VisibleInTree, Is.True);
                Assert.That(guns.Height, Is.GreaterThan(350), "The weapon bank retains usable height above the permanent TCAS bank.");
                var tcas = Named<WFCockpitTcasPanel>(hud, "CockpitTcas");
                Assert.That(tcas.VisibleInTree, Is.True, "Collision warning lamps must stay available while operating guns.");
                Assert.That(Bottom(guns), Is.LessThanOrEqualTo(tcas.GlobalPosition.Y));
                Assert.That(Bottom(tcas), Is.LessThanOrEqualTo(size.Y - 8));
                Assert.That(guns.Width, Is.GreaterThanOrEqualTo(320));
                Assert.That(guns.GlobalPosition.X + guns.Width, Is.LessThan(viewport.GlobalPosition.X));
                Assert.That(Named<Control>(hud, "CockpitFlight").Visible, Is.False);
                Assert.That(Named<Control>(hud, "CockpitMfd").VisibleInTree, Is.True);
                Assert.That(chat.VisibleInTree, Is.True);
                var gunsExpand = Named<Button>(hud, "CockpitMfdExpand");
                var gunsMfd = Named<Control>(hud, "CockpitMfd");
                if (size.X <= 1130)
                    Assert.That(gunsExpand.Disabled, Is.True, $"{theme}, {size}: GUNS leaves no room to widen the MFD here.");
                else
                {
                    var collapsedGuns = gunsMfd.Width;
                    Assert.That(gunsExpand.Disabled, Is.False, $"{theme}, {size}");
                    Toggle(gunsExpand, true);
                    Layout(hud, size);
                    Assert.That(gunsMfd.Width, Is.GreaterThan(collapsedGuns + 1), $"{theme}, {size}: Expand must widen the MFD in GUNS.");
                    Toggle(gunsExpand, false);
                    Layout(hud, size);
                    Assert.That(gunsMfd.Width, Is.EqualTo(collapsedGuns).Within(1));
                }
            }
            Press(Named<Button>(hud, "CockpitGunneryRefresh"));
            var refresh = gunMessages.OfType<WFCockpitGunneryCommandMessage>().Single();
            Assert.That(refresh.Console, Is.EqualTo(linked));
            Assert.That(refresh.Command, Is.InstanceOf<FireControlConsoleRefreshServerMessage>());
            var battery = Descendants(hud).OfType<WFCockpitGunneryPanel>().Single();
            var weaponButton = Descendants(battery).OfType<WFWeaponGrid>().Single().Children.OfType<Button>().Single();
            Toggle(weaponButton, true);
            Assert.That(battery.SelectedWeapons, Is.EquivalentTo(new[] { weapon.NetEntity }));
            var sessionsOnGuns = gunMessages.OfType<WFCockpitGunnerySessionMessage>().Count();
            hud.SelectGunnery(false);
            Assert.That(battery.SelectedWeapons, Is.EquivalentTo(new[] { weapon.NetEntity }),
                "Returning to FLIGHT must retain the selection for the next GUNS session.");
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Count(), Is.EqualTo(sessionsOnGuns),
                "FLIGHT with weapons selected keeps gun control without a redundant session message.");
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Last().Controlling, Is.True,
                "FLIGHT with weapons selected must keep firing control.");
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(new NetEntity(912), gunState));
            Assert.That(Descendants(hud).OfType<WFCockpitGunneryPanel>().Single().SelectedWeapons, Is.Empty,
                "A newly linked console cannot inherit the previous console's firing selection.");
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Last().Controlling, Is.False,
                "Changing the link on FLIGHT empties the selection and must release firing control at once.");
            hud.SelectGunnery(true);
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(null, null));
            Assert.That(Named<Control>(hud, "CockpitGunneryModes").Visible, Is.False);
            Assert.That(Named<Control>(hud, "CockpitFlight").Visible, Is.True);
            Assert.That(Descendants(hud).OfType<WFCockpitGunneryPanel>(), Is.Empty);
            hud.SelectPage("wf-cockpit-nav");
            var shield = Descendants(hud).OfType<WFShipShieldShuntScreen>().Single();
            var arc = Named<FloatSpinBox>(hud, "CockpitShieldArc");
            var previousArc = arc.Value;
            var allocations = new List<(float Direction, float Amount, float Arc)>();
            window.ShieldShuntRequested += (direction, amount, width) => allocations.Add((direction, amount, width));
            Press(arc.Children.OfType<Button>().Single(button => button.Text == "+"));
            typeof(WFShipShieldShuntScreen).GetMethod("FrameUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shield, new object[] { new FrameEventArgs(0.11f) });
            Assert.That(arc.Value, Is.GreaterThan(previousArc), "Arc width must be adjustable without opening the shield MFD.");
            Assert.That(allocations, Has.Count.EqualTo(1));
            Assert.That(allocations[0].Arc, Is.EqualTo(arc.Value * MathF.PI / 180f).Within(0.0001f),
                "The permanent arc control must retain its live helm command binding.");
            Assert.That(allocations[0].Amount, Is.EqualTo(0.65f).Within(0.0001f),
                "Changing the arc must preserve the selected concentration.");
            foreach (var plot in plots)
            {
                var wheel = new GUIMouseWheelEventArgs(new Vector2(0, 1), plot, Vector2.Zero, default, Vector2.Zero, Vector2.Zero);
                typeof(MapGridControl).GetMethod("MouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(plot, new object[] { wheel });
                Assert.That(wheel.Handled, Is.True, "Zooming an MFD plot must consume the wheel event.");
            }
            hud.SelectPage("wf-cockpit-shield-page");
            window.UpdateShieldShuntSnapshot(new WFShipShieldShuntState(false, false, 0, 0, 0, 0));
            Assert.That(Descendants(hud).OfType<WFShipShieldShuntScreen>().Single().VisibleInTree, Is.True,
                "Losing the generator must leave cockpit controls available.");
            var cockpitShip = Named<ShipScreen>(hud, "ShipContainer");
            cockpitShip.UpdateStatus(new ShipStatusMessage(null, new List<ShipTileStatus>(),
                new ShipStatusSummary { DamagedTiles = 3, WorstIntegrity = 0.5f }));
            Assert.That(cockpitShip.FindControl<Label>("DamagedLabel").Text, Is.EqualTo("3"));
            Assert.That(chat, Is.InstanceOf<ResizableChatBox>());
            var chatClamp = typeof(ResizableChatBox).GetField("_clampIn", BindingFlags.Instance | BindingFlags.NonPublic)!;
            chatClamp.SetValue(chat, (byte) 0);
            Assert.That(recentWindows.HasClosableWindow(), Is.False);
            typeof(EscapeContextUIController).GetMethod("CloseWindowOrOpenGameMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(ui.GetUIController<EscapeContextUIController>(), null);
            Assert.That(controller.Active, Is.False, "Escape with no other window open must leave the cockpit.");
            Assert.That(window.IsOpen, Is.True, "Leaving the cockpit with Escape keeps the helm open.");
            Assert.That((byte) chatClamp.GetValue(chat)!, Is.GreaterThan((byte) 0),
                "A chat box resized or rescaled while docked must be re-clamped when it leaves the cockpit.");
            Assert.That(cockpitShip.FindControl<Label>("DamagedLabel").Text, Is.EqualTo("-"),
                "Leaving the cockpit must drop its last hull sweep so the windowed SHIP page and the next entry start empty.");
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Select(message => (message.Active, message.Controlling)),
                Is.EqualTo(new[] { (true, false), (true, true), (true, false), (true, true), (true, false), (false, false) }),
                "GUNS claims guns; FLIGHT with a selection sends nothing; an emptied selection, a lost link or exiting release them.");
            Assert.That(screen.Children.ToArray(), Is.EqualTo(normalHud));
            Assert.That((networkPorts.LimitedDimension, networkPorts.Rows, networkPorts.Columns), Is.EqualTo(originalNetworkLayout),
                "Exiting must restore the helm's original network button rows and columns.");
            Assert.That(plots.Select(plot => plot.WfCockpitControls), Is.EqualTo(originalPlotControls),
                "Exiting must restore the console's normal plot interactions and annotation sizing.");
            Assert.That(viewport.Parent, Is.SameAs(viewportParent));
            Assert.That(chat.Parent, Is.SameAs(chatParent));
            Assert.That(alerts.Parent, Is.SameAs(alertsParent));
            Assert.That(votes.Parent, Is.SameAs(votesParent));
            Assert.That(speech.Parent, Is.SameAs(speechParent));
            var restoredCamera = window.FindControl<ShuttleCameraBar>("CameraBar");
            Assert.That(restoredCamera.FindControl<Button>("HelmButton").Pressed, Is.True,
                "Leaving cockpit must restore the camera controls shown before temporary EXT mode.");
            Assert.That(restoredCamera.FindControl<Button>("ExternalButton").Pressed, Is.False);
            Assert.That(window.Visible, Is.True);
            Layout(screen, new Vector2(1130, 636));
            Assert.That(inventory.InventoryHotbar.VisibleInTree, Is.True, "Exiting must restore the still-open clothing grid.");
            Assert.That(inventory.InventoryHotbar.TryGetButton("jumpsuit", out var restoredClothing), Is.True);
            Assert.That(restoredClothing, Is.SameAs(clothing), "Inventory restoration must retain the live equipment binding.");
            Assert.That(restoredClothing!.Entity, Is.EqualTo(ToClient(SEntMan.GetNetEntity(uniform))));
            Assert.That(chat.ChatInput.Input.Text, Is.EqualTo("unfinished cockpit transmission"));
            Assert.That(controller.Enter(window), Is.True, "Repeated entry must not leak or duplicate UI widgets.");
            window.Close();
            Assert.That(controller.Active, Is.False, "Closing the helm must return the character HUD.");
            window.OpenCentered();
            Assert.That(controller.Enter(window), Is.True);
            settings.SetCVar(CCVars.UILayout, "Separated");
            Assert.That(controller.Active, Is.False, "Changing HUD layout must first restore the old screen.");
        });
        await RunTicks(3);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var controller = ui.GetUIController<WFCockpitUIController>();
            screen = (InGameScreen) ui.ActiveScreen!;
            normalHud = screen.Children.ToArray();
            chat = screen.ChatBox;
            chat.ChatInput.Input.Text = "unfinished cockpit transmission";
            Assert.That(controller.Enter(window), Is.True);
            controller.Exit();
            Assert.That(screen.Children.ToArray(), Is.EqualTo(normalHud), "Both chat layouts must restore their original widgets.");
            Assert.That(controller.Enter(window), Is.True);
        });
        await RunSeconds(0.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryUnbuckle(SPlayer, SPlayer), Is.True);
            Assert.That(SEntMan.System<SharedWFCockpitSystem>().CanEnter(SPlayer, console), Is.False);
        });
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<WFCockpitUIController>();
            controller.FrameUpdate(new FrameEventArgs(0.1f));
            Assert.That(controller.Active, Is.False, "Replicated unbuckling must restore the character HUD.");
            Assert.That(screen.Children.ToArray(), Is.EqualTo(normalHud));
            Assert.That(controller.Enter(window), Is.False);
            Assert.That(chat.ChatInput.Input.Text, Is.EqualTo("unfinished cockpit transmission"));
        });
        await RunSeconds(0.5f);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True));
        await RunTicks(5);
        GhostGui ghostHud = default!;
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            ghostHud = screen.GetWidget<GhostGui>()!;
            Assert.That(ghostHud.Visible, Is.False);
            Assert.That(ui.GetUIController<WFCockpitUIController>().Enter(window), Is.True);
        });
        EntityUid ghost = default;
        await Server.WaitAssertion(() =>
        {
            ghost = SEntMan.SpawnEntity("MobObserver", MapData.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, ghost);
        });
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<WFCockpitUIController>();
            controller.FrameUpdate(new FrameEventArgs(0.1f));
            Assert.That(controller.Active, Is.False, "Changing bodies must end the old cockpit session.");
            Assert.That(screen.GetWidget<GhostGui>(), Is.SameAs(ghostHud));
            Assert.That(ghostHud.VisibleInTree, Is.True,
                "Restoring the HUD must preserve the new ghost body's visibility update.");
            Assert.That(screen.GetWidget<InventoryGui>()!.InventoryButton.Visible, Is.False,
                "The departed pilot's clothing controls must not be restored over the ghost HUD.");
        });
        await Server.WaitAssertion(() =>
        {
            Server.PlayerMan.SetAttachedEntity(ServerSession, SPlayer);
            SEntMan.DeleteEntity(ghost);
        });
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            window.Dispose();
            settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
            settings.SetCVar(CCVars.UILayout, originalLayout);
        });
    }

    [Test]
    public async Task GunneryControlFollowsGunsTabAndSelection()
    {
        EntityUid console = default;
        await Server.WaitAssertion(() =>
        {
            console = SEntMan.SpawnEntity("ComputerShuttle", MapData.GridCoords);
            var chair = SEntMan.SpawnEntity("Chair", MapData.GridCoords);
            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            SEntMan.System<ShuttleConsoleSystem>().AddPilot(console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(console));
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True);
        });
        await RunTicks(10);
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var originalLayout = settings.GetCVar(CCVars.UILayout);
        await Client.WaitAssertion(() => settings.SetCVar(CCVars.UILayout, "Default"));
        await RunTicks(3);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var screen = (InGameScreen) ui.ActiveScreen!;
            var controller = ui.GetUIController<WFCockpitUIController>();
            var messages = new List<BoundUserInterfaceMessage>();
            var window = new ShuttleConsoleWindow();
            window.WfCockpitGunneryCommand += messages.Add;
            window.WfSetCockpitConsole(ToClient(SEntMan.GetNetEntity(console)));
            window.OpenCentered();
            Assert.That(controller.Enter(window), Is.True);
            ui.ReleaseKeyboardFocus();
            var hud = screen.Children.OfType<WFCockpitView>().Single();
            var size = new Vector2(1130, 636);
            var nav = window.FindControl<NavScreen>("NavContainer").FindControl<ShuttleNavControl>("NavRadar");
            nav.SetMatrix(new EntityCoordinates(ToClient(MapData.MapUid), Vector2.Zero), Angle.Zero);
            var input = Descendants(hud).OfType<WFCockpitFireInput>().Single();
            var weapon = new FireControllableEntry(new NetEntity(910), default, "Battery", 10, true);
            var linked = new NetEntity(911);

            FireControlConsoleBoundInterfaceState Bank(params FireControllableEntry[] weapons) =>
                new(true, weapons, new NavInterfaceState(250, null, null, new(), default));
            ScreenCoordinates At(Vector2 offset = default) => WFCockpitFireInputTest.Pointer(nav, nav.Size / 2 + offset);
            IEnumerable<WFCockpitGunnerySessionMessage> Sessions() => messages.OfType<WFCockpitGunnerySessionMessage>();
            List<FireControlConsoleFireMessage> Fired() => messages.OfType<WFCockpitGunneryCommandMessage>()
                .Select(message => message.Command).OfType<FireControlConsoleFireMessage>().ToList();
            GUIBoundKeyEventArgs Click() => WFCockpitFireInputTest.Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Down, At());
            void Release() => WFCockpitFireInputTest.Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Up, At());
            void Tick(float delta, bool leftDown, Vector2 offset = default) =>
                WFCockpitFireInputTest.Tick(input, delta, At(offset), leftDown);
            void Select(bool guns)
            {
                hud.SelectGunnery(guns);
                Layout(hud, size);
            }
            void SelectWeapon()
            {
                var panel = Descendants(hud).OfType<WFCockpitGunneryPanel>().Single();
                Toggle(Descendants(panel).OfType<WFWeaponGrid>().Single().Children.OfType<Button>().Single(), true);
                Assert.That(panel.SelectedWeapons, Is.EquivalentTo(new[] { weapon.NetEntity }));
            }

            Layout(hud, size);
            Assert.That(ui.MouseGetControl(At()), Is.SameAs(nav), "The NAV plot must be the hovered control for the pointer checks.");
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank(weapon)));
            Layout(hud, size);
            Assert.That(Sessions().Select(message => (message.Active, message.Controlling)), Is.EqualTo(new[] { (true, false) }),
                "Discovering a gun bank on FLIGHT must not claim it.");

            Assert.That(Click().Handled, Is.False, "FLIGHT without a selection leaves ordinary NAV clicks alone.");
            Release();
            Tick(0.5f, false);
            Tick(0.5f, false, new Vector2(3, 0));
            Assert.That(messages.OfType<WFCockpitGunneryCommandMessage>(), Is.Empty, "FLIGHT without a selection sends no fire or aim.");
            Assert.That(nav.CustomCursorShape, Is.Null, "FLIGHT without a selection keeps the normal pointer.");

            Select(true);
            Assert.That(Sessions().Last().Controlling, Is.True, "GUNS claims control.");
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank(weapon)));
            Assert.That(Sessions().Count(), Is.EqualTo(2), "A state update on GUNS must not repeat the claim.");
            Select(false);
            Assert.That(Sessions().Last().Controlling, Is.False, "Leaving GUNS with nothing selected releases control.");
            Assert.That(Sessions().Count(), Is.EqualTo(3));
            Assert.That(Click().Handled, Is.False, "Released control returns NAV clicks to normal.");
            Release();
            Assert.That(Fired(), Is.Empty);

            Select(true);
            SelectWeapon();
            Select(false);
            Assert.That(Sessions().Count(), Is.EqualTo(4), "Leaving GUNS with a selection must not send a release.");
            Assert.That(Sessions().Last().Controlling, Is.True, "FLIGHT with a selection keeps control.");
            Tick(0, false);
            Assert.That(nav.CustomCursorShape, Is.Not.Null, "The aiming reticle shows on FLIGHT while weapons are selected.");
            Assert.That(Click().Handled, Is.True, "A FLIGHT click with a selection is consumed by firing.");
            Assert.That(Fired(), Has.Count.EqualTo(1));
            Assert.That(Fired()[0].Selected, Is.EquivalentTo(new[] { weapon.NetEntity }), "A press fires the selected weapons.");
            Tick(0.11f, true);
            Assert.That(Fired(), Has.Count.EqualTo(2), "Holding the button repeats fire on FLIGHT.");
            Assert.That(Fired()[1].Selected, Is.EquivalentTo(new[] { weapon.NetEntity }));
            Release();
            Tick(0.11f, false, new Vector2(3, 0));
            Assert.That(Fired(), Has.Count.EqualTo(3), "Pointer movement keeps updating missile aim on FLIGHT.");
            Assert.That(Fired()[2].Selected, Is.Empty, "Aim updates carry no weapons to fire.");

            Assert.That(Click().Handled, Is.True);
            Assert.That(Fired(), Has.Count.EqualTo(4));
            Select(true);
            Tick(0.11f, true);
            Assert.That(Fired(), Has.Count.EqualTo(5));
            Assert.That(Fired()[4].Selected, Is.Empty, "Switching tabs cancels a held trigger.");
            Release();
            Select(false);
            Assert.That(Click().Handled, Is.True, "A fresh press fires again after the tab change.");
            Assert.That(Fired()[^1].Selected, Is.EquivalentTo(new[] { weapon.NetEntity }));
            Release();

            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank()));
            Assert.That(Descendants(hud).OfType<WFCockpitGunneryPanel>().Single().HasSelectedWeapons, Is.False);
            Assert.That(Sessions().Last().Controlling, Is.False, "A pruned selection on FLIGHT releases control at once.");
            var firedBefore = Fired().Count;
            Assert.That(Click().Handled, Is.False, "Input returns to normal once the selection is gone.");
            Release();
            Tick(0.5f, false, new Vector2(5, 0));
            Assert.That(Fired(), Has.Count.EqualTo(firedBefore));
            Assert.That(nav.CustomCursorShape, Is.Null, "The reticle goes away with the selection.");

            Select(true);
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank(weapon)));
            Layout(hud, size);
            SelectWeapon();
            Select(false);
            Assert.That(Sessions().Last().Controlling, Is.True);
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(null, null));
            Assert.That(Sessions().Last().Controlling, Is.False, "A lost link on FLIGHT releases control at once.");
            Assert.That(Click().Handled, Is.False);
            Release();
            Assert.That(Fired(), Has.Count.EqualTo(firedBefore), "Nothing fires once control is released.");

            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank(weapon)));
            Layout(hud, size);
            Select(true);
            SelectWeapon();
            Select(false);
            Assert.That(Sessions().Last().Controlling, Is.True, "Re-linked and armed on FLIGHT holds control.");

            controller.Exit();
            Assert.That(Sessions().Select(message => (message.Active, message.Controlling)).TakeLast(2),
                Is.EqualTo(new[] { (true, true), (false, false) }),
                "Exiting while armed on FLIGHT ends the session without a trailing release.");
            window.Dispose();
            settings.SetCVar(CCVars.UILayout, originalLayout);
        });
    }

    private static void AssertFuelVisible(WFCockpitView hud)
    {
        var instruments = Named<Control>(hud, "CockpitInstruments");
        var fuel = Named<WFCockpitFuelBank>(hud, "CockpitFuelBank");
        var scroll = Named<ScrollContainer>(hud, "CockpitInstrumentScroll");
        Assert.That(fuel.VisibleInTree, Is.True);
        Assert.That(Ancestors(fuel, hud).OfType<ScrollContainer>(), Is.Empty,
            "Fuel and FUEL LOW must remain fixed outside the dial scroll region.");
        Assert.That(Descendants(instruments).OfType<ScrollContainer>().Count(), Is.EqualTo(1));
        Assert.That(scroll.Height, Is.GreaterThan(0), "The fixed reserve strip must leave the flight dials accessible.");
        Assert.That(fuel.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(scroll) - 1));
        Assert.That(fuel.GlobalPosition.Y, Is.GreaterThanOrEqualTo(instruments.GlobalPosition.Y));
        Assert.That(Bottom(fuel), Is.LessThanOrEqualTo(Bottom(instruments) + 1),
            "The complete fuel gauge must stay inside the visible panel, including with FLIGHT/GUNS selection present.");
        Assert.That(Right(fuel), Is.LessThanOrEqualTo(Right(instruments) + 1));
    }

    private static void AssertFuelBinding(ShipScreen ship, WFCockpitView hud)
    {
        var gauge = Named<WFGlassGauge>(hud, "CockpitFuel");
        var lamp = Named<WFCockpitFuelLamp>(hud, "CockpitFuelLow");
        ship.ClearStatus();
        Assert.That(gauge.Reading.Value, Is.Null);
        Assert.That(lamp.LowFuel, Is.Null, "Missing ship telemetry must not look like an empty tank.");
        foreach (var (summary, expected, low) in new (ShipFuelSummary, double?, bool?)[]
        {
            (default, null, null),
            (new() { Sources = 2, Fraction = 0 }, 0, true),
            (new() { Sources = 2, Fraction = 0.2f }, 20, true),
            (new() { Sources = 2, Fraction = 0.21f }, 21, false),
            (new() { Sources = 2, Fraction = 1 }, 100, false),
            (new() { Sources = 2, UnknownSources = 1, Fraction = 0 }, null, null),
        })
        {
            ship.UpdateStatus(new ShipStatusMessage(null, new List<ShipTileStatus>(), new ShipStatusSummary { Fuel = summary }));
            if (expected is { } value)
                Assert.That(gauge.Reading.Value, Is.EqualTo(value).Within(0.0001));
            else
                Assert.That(gauge.Reading.Value, Is.Null, "Absent or partially measured fuel sources must show NO SIGNAL.");
            Assert.That(lamp.LowFuel, Is.EqualTo(low));
        }
        ship.ClearStatus();
        Assert.That(gauge.Reading.Value, Is.Null, "Clearing the ship must discard its previous reserve.");
        Assert.That(lamp.LowFuel, Is.Null);
    }

    private static void AssertCockpitLayout(WFCockpitView hud, MainViewport viewport, ChatBox chat,
        IUserInterfaceManager ui, Vector2 size, string theme, string page)
    {
        var instruments = Named<Control>(hud, "CockpitInstruments");
        var flight = Named<Control>(hud, "CockpitFlight");
        var camera = Named<Control>(hud, "CockpitCamera");
        var mfd = Named<Control>(hud, "CockpitMfd");
        var comms = Named<Control>(hud, "CockpitComms");
        var shields = Named<Control>(hud, "CockpitShields");
        var translation = Named<Control>(hud, "CockpitTranslation");
        var selectors = Named<Control>(hud, "CockpitMfdSelectors");
        var arc = Named<FloatSpinBox>(hud, "CockpitShieldArc");
        var header = Named<Control>(hud, "CockpitHeader");
        var logo = Named<TextureRect>(hud, "CockpitLogo");
        var title = Named<Label>(hud, "CockpitTitle");
        var exit = Named<Button>(hud, "CockpitExit");
        Assert.That(logo.VisibleInTree && logo.Texture != null, Is.True);
        Assert.That(logo.GlobalPosition.X + logo.Width / 2, Is.EqualTo(size.X / 2).Within(1),
            "The Wolfgate wordmark must stay centered independently of the title and exit button.");
        Assert.That(Right(title), Is.LessThanOrEqualTo(logo.GlobalPosition.X));
        Assert.That(Right(logo), Is.LessThanOrEqualTo(exit.GlobalPosition.X));
        Assert.That(Bottom(logo), Is.LessThanOrEqualTo(Bottom(header)));
        var tcas = Named<WFCockpitTcasPanel>(hud, "CockpitTcas");
        Assert.That(tcas.VisibleInTree, Is.True);
        Assert.That(tcas.Height, Is.EqualTo(108).Within(1));
        Assert.That(tcas.GlobalPosition.X, Is.EqualTo(instruments.GlobalPosition.X).Within(1));
        Assert.That(tcas.Width, Is.EqualTo(instruments.Width).Within(1));
        Assert.That(tcas.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(instruments)));
        Assert.That(Bottom(tcas), Is.LessThanOrEqualTo(camera.GlobalPosition.Y));
        Assert.That(Ancestors(tcas, hud).OfType<ScrollContainer>(), Is.Empty,
            "TCAS lamps must never scroll with the instrument bank.");
        var speedLimit = Named<Control>(hud, "MaximumShuttleSpeedBox");
        Assert.That(Ancestors(speedLimit, hud), Does.Not.Contain(flight));
        Assert.That(Ancestors(speedLimit, hud), Does.Contain(mfd), "The speed limiter belongs on SYS.");
        Assert.That(speedLimit.VisibleInTree, Is.EqualTo(page == "wf-cockpit-systems"));
        if (page == "wf-cockpit-systems")
        {
            var ports = Named<GridContainer>(hud, "NetworkPortsBox");
            var portButtons = ports.Children.OfType<Button>().Where(button => button.VisibleInTree).ToArray();
            Assert.That(portButtons, Has.Length.EqualTo(8), "All wired auxiliary ports must remain available on SYS.");
            foreach (var button in portButtons)
            {
                Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(ports.GlobalPosition.X));
                Assert.That(Right(button), Is.LessThanOrEqualTo(Right(ports) + 1));
                Assert.That(Right(button), Is.LessThanOrEqualTo(Right(mfd) + 1));
                AssertLabelFits(button, ui, $"{theme}, {size}, SYS auxiliary port");
            }
            for (var index = 0; index < portButtons.Length; index += 2)
                Assert.That(Right(portButtons[index]), Is.LessThanOrEqualTo(portButtons[index + 1].GlobalPosition.X),
                    "The fixed-width auxiliary buttons must not overlap at compact MFD widths.");
        }
        Assert.That(flight.GlobalPosition.X, Is.EqualTo(instruments.GlobalPosition.X).Within(1));
        Assert.That(flight.Width, Is.EqualTo(instruments.Width).Within(1));
        Assert.That(camera.VisibleInTree, Is.True, "Camera selection must stay available on every MFD page.");
        Assert.That(camera.GlobalPosition.X, Is.EqualTo(instruments.GlobalPosition.X).Within(1));
        Assert.That(camera.Width, Is.EqualTo(instruments.Width).Within(1));
        Assert.That(camera.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(instruments)));
        Assert.That(flight.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(camera)), "Camera controls belong directly above flight controls.");
        foreach (var button in Descendants(camera).OfType<Button>().Where(control => control.VisibleInTree))
        {
            Assert.That(button.GlobalPosition.Y, Is.GreaterThanOrEqualTo(camera.GlobalPosition.Y));
            Assert.That(Bottom(button), Is.LessThanOrEqualTo(Bottom(camera)), "Camera buttons must fit in their permanent panel.");
            Assert.That(Right(button), Is.LessThanOrEqualTo(Right(camera)));
        }
        Assert.That(Bottom(translation), Is.LessThanOrEqualTo(viewport.GlobalPosition.Y));
        Assert.That(translation.GlobalPosition.X, Is.EqualTo(viewport.GlobalPosition.X).Within(1));
        Assert.That(Bottom(mfd), Is.EqualTo(size.Y - 8).Within(1), "The MFD must use the full height beside the lower controls.");
        Assert.That(flight.GlobalPosition.Y, Is.EqualTo(comms.GlobalPosition.Y).Within(1), "The lower control panels must align.");
        Assert.That(flight.Height, Is.EqualTo(comms.Height).Within(1));
        Assert.That(flight.Height, Is.EqualTo(shields.Height).Within(1));
        var status = Named<WFCockpitStatusLights>(hud, "CockpitStatus");
        Assert.That(status.VisibleInTree, Is.True);
        Assert.That(status.ChildCount, Is.EqualTo(6));
        Assert.That(status.GlobalPosition.Y, Is.GreaterThanOrEqualTo(flight.GlobalPosition.Y));
        Assert.That(Bottom(status), Is.LessThanOrEqualTo(Bottom(flight)));
        Assert.That(Ancestors(status, flight).OfType<ScrollContainer>(), Is.Empty,
            "Flight-status lamps must remain visible when flight controls need to scroll.");
        var heading = Named<WFHeadingInstrument>(hud, "CockpitHeading");
        var dialScroll = Named<ScrollContainer>(hud, "CockpitInstrumentScroll");
        var headingHeight = hud.CompactDials ? WFCockpitInstrumentSizing.CompactHeight(176) : 176;
        var dialHeight = hud.CompactDials ? WFCockpitInstrumentSizing.CompactHeight(160) : 160;
        if (hud.CompactDials)
            Assert.That(dialScroll.Height, Is.LessThan(WFCockpitInstrumentSizing.FullColumnHeight + 8),
                $"{theme}, {size}: compact dials are only for columns too short to show the full-size ones.");
        else
            Assert.That(dialScroll.Height, Is.GreaterThanOrEqualTo(WFCockpitInstrumentSizing.FullColumnHeight - 8),
                $"{theme}, {size}: full-size dials need a column tall enough to show them whole.");
        Assert.That(heading.Height, Is.EqualTo(headingHeight).Within(1));
        Assert.That(Descendants(hud).Any(control => control.Name == "CockpitSpeedometer"), Is.False,
            "The velocity instrument already supplies speed; its duplicate strip must not displace fuel telemetry.");
        var hull = Named<WFGlassGauge>(hud, "CockpitHull");
        var fuel = Named<WFGlassGauge>(hud, "CockpitFuel");
        var fuelBank = Named<WFCockpitFuelBank>(hud, "CockpitFuelBank");
        var fuelLow = Named<WFCockpitFuelLamp>(hud, "CockpitFuelLow");
        Assert.That(hull.Strip && fuel.Strip, Is.True);
        Assert.That(fuel.VisibleInTree && fuelLow.VisibleInTree, Is.True);
        AssertFuelVisible(hud);
        var instrumentScroll = Named<ScrollContainer>(hud, "CockpitInstrumentScroll");
        Assert.That(Ancestors(hull, instruments), Does.Contain(instrumentScroll));
        Assert.That(fuelBank.Width, Is.GreaterThanOrEqualTo(hull.Width - 1));
        Assert.That(fuel.Width, Is.GreaterThanOrEqualTo(104), "The fuel scale must remain readable at compact side-panel widths.");
        Assert.That(fuel.CompactStrip, Is.True);
        Assert.That(fuel.Height, Is.EqualTo(44).Within(1));
        foreach (var name in new[] { "CockpitForward", "CockpitLateral" })
        {
            var strip = Named<WFGlassGauge>(hud, name);
            Assert.That(strip.Strip && strip.CompactStrip, Is.True, name);
            Assert.That(strip.Height, Is.EqualTo(44).Within(1), name);
        }
        Assert.That(fuelLow.Height, Is.EqualTo(fuel.Height).Within(1));
        Assert.That(fuelLow.GlobalPosition.Y, Is.EqualTo(fuel.GlobalPosition.Y).Within(1));
        Assert.That(fuelLow.GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(fuel)));
        Assert.That(Right(fuelLow), Is.LessThanOrEqualTo(Right(fuelBank) + 1));
        var velocity = Named<WFVelocityVectorInstrument>(hud, "CockpitVelocity");
        Assert.That(velocity.VisibleInTree, Is.True);
        var yaw = Named<WFGlassGauge>(hud, "CockpitYaw");
        Assert.That(yaw.GlobalPosition.X - Right(velocity), Is.GreaterThanOrEqualTo(11),
            "The velocity and turn-rate captions need a clear gap between their dial faces.");
        Assert.That(yaw.CaptionInset, Is.GreaterThanOrEqualTo(8));
        Assert.That(velocity.Height, Is.EqualTo(dialHeight).Within(1),
            "Digital and mechanical vector instruments must retain the same readable footprint.");
        Assert.That(velocity.Width, Is.GreaterThanOrEqualTo(88));
        Assert.That(velocity.GlobalPosition.X, Is.GreaterThanOrEqualTo(instruments.GlobalPosition.X));
        Assert.That(Right(velocity), Is.LessThanOrEqualTo(Right(instruments)));
        Assert.That(velocity.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(heading)));
        Assert.That(Bottom(velocity), Is.LessThanOrEqualTo(Bottom(velocity.Parent!)),
            "The vector dial must fit the original speed/yaw row even when the instrument bank needs to scroll.");
        foreach (var gauge in Descendants(instruments).OfType<WFGlassGauge>().Where(gauge => !gauge.Strip))
            Assert.That(gauge.Height, Is.EqualTo(dialHeight).Within(1),
                "Both instrument themes need the same readable full-size face.");
        Assert.That(comms.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(viewport)));
        Assert.That(shields.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(viewport)));
        Assert.That(shields.GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(comms)));
        Assert.That(Right(shields), Is.LessThanOrEqualTo(mfd.GlobalPosition.X));
        Assert.That(viewport.Width, Is.GreaterThan(300));
        Assert.That(viewport.Height, Is.GreaterThan(200));
        Assert.That(viewport.VisibleInTree, Is.True, "Switching MFD context must keep the world visible.");
        var speechClip = Named<WFCockpitSpeechClip>(hud, "CockpitSpeechClip");
        var speechRoot = Named<Control>(hud, "CockpitSpeechBubbles");
        Assert.That(speechClip.RectClipContent, Is.True, "Speech bubbles must be cut off at the world view's edge.");
        Assert.That(speechClip.GlobalPosition.X, Is.EqualTo(viewport.GlobalPosition.X).Within(1));
        Assert.That(speechClip.GlobalPosition.Y, Is.EqualTo(viewport.GlobalPosition.Y).Within(1));
        Assert.That(speechClip.Width, Is.EqualTo(viewport.Width).Within(1));
        Assert.That(speechClip.Height, Is.EqualTo(viewport.Height).Within(1));
        Assert.That(speechRoot.GlobalPosition.X, Is.EqualTo(0).Within(1), "Bubble coordinates are in screen space.");
        Assert.That(speechRoot.GlobalPosition.Y, Is.EqualTo(0).Within(1), "Bubble coordinates are in screen space.");
        Assert.That(speechRoot.Width, Is.EqualTo(size.X).Within(1));
        Assert.That(speechRoot.Height, Is.EqualTo(size.Y).Within(1));
        Assert.That(chat.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(viewport)));
        Assert.That(Right(chat), Is.LessThanOrEqualTo(size.X));
        Assert.That(arc.VisibleInTree, Is.True, "Shield arc width must remain available on every MFD page.");
        Assert.That(arc.GlobalPosition.Y, Is.GreaterThanOrEqualTo(shields.GlobalPosition.Y));
        Assert.That(Bottom(arc), Is.LessThanOrEqualTo(Bottom(shields)));
        Assert.That(Descendants(shields).OfType<Slider>().Single().Width, Is.GreaterThan(100),
            "The permanent shield power slider must remain usable.");
        var buttons = selectors.Children.OfType<Button>().ToArray();
        Assert.That(buttons, Has.Length.EqualTo(8));
        foreach (var button in buttons)
        {
            Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(mfd.GlobalPosition.X));
            Assert.That(Right(button), Is.LessThanOrEqualTo(Right(mfd)), "All MFD contexts must fit in one selector row.");
            Assert.That(button.ToolTip, Is.Not.Null.And.Not.Empty, "Abbreviated selectors need their complete names on hover.");
            AssertLabelFits(button, ui, $"{theme}, {size}, {page}");
        }
        foreach (var scroll in Descendants(mfd).OfType<ScrollContainer>().Where(control => control.VisibleInTree))
            Assert.That(Ancestors(scroll, mfd).OfType<ScrollContainer>(), Is.Empty,
                "An MFD list must not scroll inside another scrolling panel.");
        foreach (var plot in Descendants(mfd).OfType<MapGridControl>().Where(control => control.VisibleInTree))
        {
            Assert.That(Ancestors(plot, mfd).OfType<ScrollContainer>(), Is.Empty,
                "Plots must stay fixed when their separate details are scrolled.");
            var context = $"{theme}, {size.X}x{size.Y}, {page}: {plot.GetType().Name} ({plot.Name})";
            var containers = string.Join(" <- ", Ancestors(plot, mfd)
                .Select(control => $"{control.GetType().Name} ({control.Name}) {control.Size}"));
            Assert.That(plot.Height, Is.GreaterThan(100), $"{context} needs a usable plot. Containers: {containers}");
            if (page == "wf-cockpit-dock")
                Assert.That(plot.Height, Is.GreaterThanOrEqualTo((Bottom(mfd) - Bottom(selectors)) * 0.5f),
                    $"{context}: the approach plot must occupy most of the MFD's usable height. Containers: {containers}");
        }
    }

    /// <summary>Exercises the visible native thumb rather than only setting a scroll offset programmatically.</summary>
    private static void AssertInstrumentScrollInput(WFCockpitView hud, IUserInterfaceManager ui, Vector2 size, string theme)
    {
        var scroll = Named<ScrollContainer>(hud, "CockpitInstrumentScroll");
        scroll.SetScrollValue(Vector2.Zero);
        Layout(hud, size);
        var bar = scroll.Children.OfType<VScrollBar>().Single();
        var content = scroll.Children.Single(child => child is not ScrollBar);
        var overflow = content.DesiredSize.Y - scroll.Height;
        var context = $"{theme}, {size}: content {content.DesiredSize.Y}, viewport {scroll.Height}, range {bar.MaxValue - bar.Page}";
        Assert.That(bar.Visible, Is.EqualTo(overflow > 0.001f), context);
        Assert.That(scroll.Children.OfType<HScrollBar>().Single().Visible, Is.False, context);
        if (size.Y >= 1080)
            Assert.That(bar.Visible, Is.False, $"{context}: fitting instruments must not retain an immovable scrollbar.");
        if (!bar.Visible)
        {
            Assert.That(scroll.GetScrollValue(), Is.EqualTo(Vector2.Zero), context);
            return;
        }
        Assert.That(bar.MaxValue - bar.Page, Is.GreaterThan(1), context);
        var grabber = (UIBox2) typeof(ScrollBar).GetMethod("_getGrabberBox", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, null)!;
        var start = grabber.TopLeft + new Vector2(grabber.Width / 2, Math.Min(2, grabber.Height / 2));
        var pointer = new ScreenCoordinates(bar.GlobalPixelPosition + start, bar.Window!.Id);
        Assert.That(ui.MouseGetControl(pointer), Is.SameAs(bar), $"{context}: the visible thumb must receive pointer input.");
        var down = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, pointer, true,
            start / bar.UIScale, start);
        typeof(ScrollBar).GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, new object[] { down });
        Assert.That(down.Handled, Is.True, $"{context}: the thumb must accept a drag.");
        var before = content.GlobalPosition.Y;
        var delta = new Vector2(0, Math.Max(8, bar.PixelHeight / 4f));
        var end = start + delta;
        var endPointer = new ScreenCoordinates(bar.GlobalPixelPosition + end, bar.Window.Id);
        var move = new GUIMouseMoveEventArgs(delta / bar.UIScale, bar,
            bar.GlobalPosition + end / bar.UIScale, endPointer, end / bar.UIScale, end);
        typeof(ScrollBar).GetMethod("MouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, new object[] { move });
        var up = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, endPointer, true,
            end / bar.UIScale, end);
        typeof(ScrollBar).GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, new object[] { up });
        Layout(hud, size);
        Assert.That(scroll.VScroll, Is.GreaterThan(0), $"{context}: dragging must advance the native range.");
        Assert.That(content.GlobalPosition.Y, Is.LessThan(before), $"{context}: dragging must move the dial column.");
        scroll.SetScrollValue(Vector2.Zero);
        Layout(hud, size);
    }

    private static void Layout(Control root, Vector2 size)
    {
        // Visibility queues parent layout changes; this synchronous sweep cannot wait for the next UI frame.
        foreach (var control in Descendants(root))
            control.InvalidateMeasure();
        root.Measure(size);
        root.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
    }

    private static void AssertLabelFits(Button button, IUserInterfaceManager ui, string context) =>
        AssertLabelFits(button.Label, ui, context);

    private static void AssertLabelFits(Label label, IUserInterfaceManager ui, string context)
    {
        var font = label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
            ? styled : ui.ThemeDefaults.LabelFont);
        var width = 0f;
        foreach (var rune in label.Text!.EnumerateRunes())
            width += font.GetCharMetrics(rune, label.UIScale)?.Advance ?? 0;
        Assert.That(label.PixelSize.X + 1, Is.GreaterThanOrEqualTo(width),
            $"{context}: the {label.Text} button must display its complete label.");
    }

    private static void Toggle(BaseButton button, bool pressed) => WFButtonTestInput.Toggle(button, pressed);

    private static T Named<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().Single(control => control.Name == name);

    private static float Bottom(Control control) => control.GlobalPosition.Y + control.Height;

    private static float Right(Control control) => control.GlobalPosition.X + control.Width;

    private static IEnumerable<Control> Ancestors(Control control, Control boundary)
    {
        for (var parent = control.Parent; parent != null && parent != boundary; parent = parent.Parent)
            yield return parent;
    }

    private static void Press(BaseButton button) => WFButtonTestInput.Click(button);

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }
}
