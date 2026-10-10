using Content.Shared._Mono.FireControl;
using Content.Shared.Shuttles.BUIStates;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.ShipShields;
using Content.Shared.CCVar;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Cockpit;
using Content.Shared._WF.Shuttles;
using Content.Shared._WF.ShipShields;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
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
  - type: Buckle
";
    protected override string PlayerPrototype => "WFCockpitTestMob";

    [Test]
    public async Task SeatedCockpitRestoresHudAndBindings()
    {
        EntityUid console = default;
        EntityUid chair = default;
        await Server.WaitAssertion(() =>
        {
            console = SEntMan.SpawnEntity("ComputerShuttle", MapData.GridCoords);
            chair = SEntMan.SpawnEntity("ChairPilotSeat", MapData.GridCoords);
            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            SEntMan.System<ShuttleConsoleSystem>().AddPilot(console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(console));
            var cockpit = SEntMan.System<SharedWFCockpitSystem>();
            Assert.That(cockpit.CanEnter(SPlayer, console), Is.False, "Standing pilots cannot enter cockpit mode.");
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True);
            Assert.That(cockpit.CanEnter(SPlayer, console), Is.True);
            Assert.That(cockpit.CanEnter(SPlayer, chair), Is.False, "Another console cannot borrow this piloting session.");
            SEntMan.RemoveComponent<WFCockpitSeatComponent>(chair);
            Assert.That(cockpit.CanEnter(SPlayer, console), Is.False, "An ordinary seat is insufficient.");
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
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
        var originalLayout = settings.GetCVar(CCVars.UILayout);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            screen = (InGameScreen) ui.ActiveScreen!;
            normalHud = screen.Children.ToArray();
            chat = screen.ChatBox;
            viewport = screen.GetWidget<MainViewport>()!;
            viewportParent = viewport.Parent!;
            chatParent = chat.Parent!;
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
            Assert.That(normalHud.Where(control => control != chat && control != viewport)
                .All(control => !control.VisibleInTree), Is.True, "Character HUD controls must stay hidden.");
            var hud = screen.Children.OfType<WFCockpitView>().Single();
            var navigation = Named<NavScreen>(window, "NavContainer");
            var velocity = Named<WFVelocityVectorInstrument>(hud, "CockpitVelocity");
            Assert.That(velocity.Reading, Is.Null, "An unbound helm must not invent a stopped velocity sample.");
            var ship = ToClient(SEntMan.GetNetEntity(MapData.Grid.Owner));
            var physics = CEntMan.System<SharedPhysicsSystem>();
            var transform = CEntMan.System<SharedTransformSystem>();
            var body = CEntMan.GetComponent<PhysicsComponent>(ship);
            var originalBodyType = body.BodyType;
            var originalVelocity = body.LinearVelocity;
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
            }
            finally
            {
                physics.SetLinearVelocity(ship, originalVelocity);
                physics.SetBodyType(ship, originalBodyType);
                transform.SetWorldRotation(ship, originalRotation);
            }
            Assert.That(networkPorts.Columns, Is.EqualTo(4), "Auxiliary device buttons must fit the compact MFD.");
            Assert.That(plots, Has.Length.GreaterThanOrEqualTo(5));
            Assert.That(plots.All(plot => plot.WfCockpitControls), Is.True,
                "Navigation, hull, strategic, docking and access plots must share cockpit interactions.");
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
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
                                }
                                else
                                {
                                    Assert.That(accessDetails.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(accessPlot)),
                                        "Compact access MFDs must keep their controls below the fixed diagram.");
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
                                    Assert.That(hullDetails.Width, Is.GreaterThan(hullPlot.Width), "Expanded hull details need most of the MFD width.");
                                }
                                else
                                {
                                    Assert.That(hullDetails.Position.Y, Is.GreaterThanOrEqualTo(hullPlot.Position.Y + hullPlot.Height),
                                        "Compact ship MFDs must stack details below the readable hull plot.");
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
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Single().Active, Is.True);
            Assert.That(Named<Control>(hud, "CockpitGunneryModes").Visible, Is.False,
                "Cockpits without an authorized nearby gun console keep the original flight layout.");
            var weapon = new FireControllableEntry(new NetEntity(910), default, "Battery", 10, true);
            var gunState = new FireControlConsoleBoundInterfaceState(true, new[] { weapon },
                new NavInterfaceState(250, null, null, new(), default));
            var linked = new NetEntity(911);
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, gunState));
            Assert.That(Named<Control>(hud, "CockpitGunneryModes").Visible, Is.True);
            hud.SelectGunnery(true);
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            foreach (var size in new[] { new Vector2(1130, 636), new Vector2(1600, 900) })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                Layout(hud, size);
                var guns = Named<Control>(hud, "CockpitGunnery");
                Assert.That(guns.VisibleInTree, Is.True);
                Assert.That(guns.Height, Is.GreaterThan(450), "The weapon bank gets the full left column.");
                Assert.That(guns.Width, Is.GreaterThanOrEqualTo(320));
                Assert.That(guns.GlobalPosition.X + guns.Width, Is.LessThan(viewport.GlobalPosition.X));
                Assert.That(Named<Control>(hud, "CockpitFlight").Visible, Is.False);
                Assert.That(Named<Control>(hud, "CockpitMfd").VisibleInTree, Is.True);
                Assert.That(chat.VisibleInTree, Is.True);
            }
            Press(Named<Button>(hud, "CockpitGunneryRefresh"));
            var refresh = gunMessages.OfType<WFCockpitGunneryCommandMessage>().Single();
            Assert.That(refresh.Console, Is.EqualTo(linked));
            Assert.That(refresh.Command, Is.InstanceOf<FireControlConsoleRefreshServerMessage>());
            var battery = Descendants(hud).OfType<WFCockpitGunneryPanel>().Single();
            var weaponButton = Descendants(battery).OfType<WFWeaponGrid>().Single().Children.OfType<Button>().Single();
            Toggle(weaponButton, true);
            Assert.That(battery.SelectedWeapons, Is.EquivalentTo(new[] { weapon.NetEntity }));
            hud.SelectGunnery(false);
            Assert.That(battery.SelectedWeapons, Is.EquivalentTo(new[] { weapon.NetEntity }),
                "Returning to flight instruments must retain the weapons aimed through the world or NAV view.");
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(new NetEntity(912), gunState));
            Assert.That(Descendants(hud).OfType<WFCockpitGunneryPanel>().Single().SelectedWeapons, Is.Empty,
                "A newly linked console cannot inherit the previous console's firing selection.");
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
            controller.Exit();
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Select(message => message.Active),
                Is.EqualTo(new[] { true, false }), "Exit must release the server-side gun link.");
            Assert.That(screen.Children.ToArray(), Is.EqualTo(normalHud));
            Assert.That((networkPorts.LimitedDimension, networkPorts.Rows, networkPorts.Columns), Is.EqualTo(originalNetworkLayout),
                "Exiting must restore the helm's original network button rows and columns.");
            Assert.That(plots.Select(plot => plot.WfCockpitControls), Is.EqualTo(originalPlotControls),
                "Exiting must restore the console's normal plot interactions and annotation sizing.");
            Assert.That(viewport.Parent, Is.SameAs(viewportParent));
            Assert.That(chat.Parent, Is.SameAs(chatParent));
            Assert.That(window.Visible, Is.True);
            Assert.That(chat.ChatInput.Input.Text, Is.EqualTo("unfinished cockpit transmission"));
            Assert.That(controller.Enter(window), Is.True, "Repeated entry must not leak or duplicate UI widgets.");
            window.Close();
            Assert.That(controller.Active, Is.False, "Closing the helm must return the character HUD.");
            window.OpenCentered();
            Assert.That(controller.Enter(window), Is.True);
            settings.SetCVar(CCVars.UILayout, originalLayout == "Separated" ? "Default" : "Separated");
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
            window.Dispose();
            settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
            settings.SetCVar(CCVars.UILayout, originalLayout);
        });
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
        Assert.That(heading.Height, Is.EqualTo(theme == WolfgateSkins.Retro.Id ? 176 : 106).Within(1));
        var velocity = Named<WFVelocityVectorInstrument>(hud, "CockpitVelocity");
        Assert.That(velocity.VisibleInTree, Is.True);
        Assert.That(velocity.Height, Is.EqualTo(theme == WolfgateSkins.Retro.Id ? 160 : 100).Within(1),
            "The vector instrument must retain the existing speed dial's theme-specific footprint.");
        Assert.That(velocity.Width, Is.GreaterThanOrEqualTo(88));
        Assert.That(velocity.GlobalPosition.X, Is.GreaterThanOrEqualTo(instruments.GlobalPosition.X));
        Assert.That(Right(velocity), Is.LessThanOrEqualTo(Right(instruments)));
        Assert.That(velocity.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(heading)));
        Assert.That(Bottom(velocity), Is.LessThanOrEqualTo(Bottom(velocity.Parent!)),
            "The vector dial must fit the original speed/yaw row even when the instrument bank needs to scroll.");
        foreach (var gauge in Descendants(instruments).OfType<WFGlassGauge>().Where(gauge => !gauge.Strip))
            Assert.That(gauge.Height, Is.EqualTo(theme == WolfgateSkins.Retro.Id ? 160 : 100).Within(1),
                "Retro mechanical dials need a readable full-size face.");
        Assert.That(comms.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(viewport)));
        Assert.That(shields.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(viewport)));
        Assert.That(shields.GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(comms)));
        Assert.That(Right(shields), Is.LessThanOrEqualTo(mfd.GlobalPosition.X));
        Assert.That(viewport.Width, Is.GreaterThan(300));
        Assert.That(viewport.Height, Is.GreaterThan(200));
        Assert.That(viewport.VisibleInTree, Is.True, "Switching MFD context must keep the world visible.");
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
            Assert.That(button.GlobalPosition.Y, Is.EqualTo(buttons[0].GlobalPosition.Y).Within(1),
                "All MFD contexts must fit in one selector row.");
            Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(mfd.GlobalPosition.X));
            Assert.That(Right(button), Is.LessThanOrEqualTo(Right(mfd)));
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

    private static void Layout(Control root, Vector2 size)
    {
        // Visibility queues parent layout changes; this synchronous sweep cannot wait for the next UI frame.
        foreach (var control in Descendants(root))
            control.InvalidateMeasure();
        root.Measure(size);
        root.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
    }

    private static void AssertLabelFits(Button button, IUserInterfaceManager ui, string context)
    {
        var label = button.Label;
        var font = label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
            ? styled : ui.ThemeDefaults.LabelFont);
        var width = 0f;
        foreach (var rune in button.Text!.EnumerateRunes())
            width += font.GetCharMetrics(rune, label.UIScale)?.Advance ?? 0;
        Assert.That(label.PixelSize.X + 1, Is.GreaterThanOrEqualTo(width),
            $"{context}: the {button.Text} button must display its complete label.");
    }

    private static void Toggle(BaseButton button, bool pressed)
    {
        button.Pressed = pressed;
        var handler = (Action<BaseButton.ButtonToggledEventArgs>?) typeof(BaseButton)
            .GetField("OnToggled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonToggledEventArgs(pressed, button, null!));
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().Single(control => control.Name == name);

    private static float Bottom(Control control) => control.GlobalPosition.Y + control.Height;

    private static float Right(Control control) => control.GlobalPosition.X + control.Width;

    private static IEnumerable<Control> Ancestors(Control control, Control boundary)
    {
        for (var parent = control.Parent; parent != null && parent != boundary; parent = parent.Parent)
            yield return parent;
    }

    private static void Press(BaseButton button)
    {
        var handler = (Action<BaseButton.ButtonEventArgs>?) typeof(BaseButton)
            .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }
}
