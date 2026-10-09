using System.Numerics;
using System.Linq;
using System.Reflection;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Shared.Localization;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.CCVar;
using Robust.Shared.Configuration;
using Content.Shared._WF.Shuttles;
using Content.Client._Mono.FireControl.UI;
using Content.Client.Shuttles.UI;
using Content.Client._WF.Shuttles.UI;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared.Projectiles;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.CombatConsole;

/// <summary>Checks instrument construction, console ownership and real missile-lock filtering.</summary>
[TestFixture]
public sealed class WFCombatConsoleTest
{
    [Test]
    public async Task ConsoleOwnershipAndExposedMissileLocks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid consoleUid = default;
        EntityUid missileUid = default;
        EntityUid serverUid = default;
        EntityUid flareUid = default;
        await pair.Server.WaitAssertion(() =>
        {
            consoleUid = em.SpawnEntity(null, map.GridCoords);
            serverUid = em.SpawnEntity(null, map.GridCoords);
            em.System<SharedTransformSystem>().AnchorEntity(consoleUid);
            var console = em.AddComponent<FireControlConsoleComponent>(consoleUid);
            var server = em.AddComponent<FireControlServerComponent>(serverUid);
            server.ConnectedGrid = map.Grid.Owner;
            server.ProcessingPower = 100;
            em.EnsureComponent<FireControlGridComponent>(map.Grid.Owner).ControllingServer = serverUid;
            server.Consoles.Add(consoleUid);

            console.ConnectedServer = serverUid;
            var settings = em.AddComponent<WFCombatConsoleComponent>(consoleUid);
            settings.Automatic = true;
            var system = em.System<WFCombatConsoleSystem>();
            Assert.That(system.TryGetServer(consoleUid, console, out _, out _), Is.True);
            server.Consoles.Clear();
            Assert.That(system.TryGetServer(consoleUid, console, out _, out _), Is.False);
            server.Consoles.Add(consoleUid);

            flareUid = em.SpawnEntity("WeaponTurretFlare", map.GridCoords);
            Assert.That(em.GetComponent<TransformComponent>(flareUid).Anchored, Is.True);
            em.GetComponent<FireControllableComponent>(flareUid).ControllingServer = serverUid;
            server.Controlled.Add(flareUid);
            var snapshot = system.GetState(consoleUid, console);
            Assert.That(snapshot.UnlimitedSupply, Is.True, "Sunny's existing autoloader must remain usable at zero stored rounds.");

            missileUid = em.SpawnEntity(null, new EntityCoordinates(map.MapUid, new Vector2(10, 0)));
            em.AddComponent<ProjectileComponent>(missileUid);
            var seeker = em.AddComponent<TargetSeekingComponent>(missileUid);
            seeker.CurrentTarget = map.Grid.Owner;
            seeker.Launched = true;
        });

        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.EqualTo(1));
            Assert.That(em.GetComponent<WFFlareLauncherComponent>(flareUid).NextBurst, Is.GreaterThan(TimeSpan.Zero),
                "Automatic defense must actually fire the existing Sunny launcher.");
            em.GetComponent<TargetSeekingComponent>(missileUid).ExposesTracking = false;
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.Zero);
            var seeker = em.GetComponent<TargetSeekingComponent>(missileUid);
            seeker.ExposesTracking = true;
            seeker.SeekingDisabled = true;
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.Zero);
            em.GetComponent<TargetSeekingComponent>(missileUid).SeekingDisabled = false;
            em.GetComponent<ProjectileComponent>(missileUid).Shooter = consoleUid;
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.Zero,
                "Own-ship projectiles must not waste flares.");
            em.GetComponent<ProjectileComponent>(missileUid).Shooter = null;
            em.System<SharedTransformSystem>().SetCoordinates(missileUid,
                new EntityCoordinates(map.MapUid, new Vector2(1000, 0)));
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.Zero);
            em.DeleteEntity(missileUid);
            em.DeleteEntity(flareUid);
            em.DeleteEntity(consoleUid);
            em.DeleteEntity(serverUid);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var resources = pair.Client.ResolveDependency<IResourceCache>();
            foreach (var cue in new[] { "key", "switch_on", "switch_off", "selector", "bearing", "warning" })
            {
                var clip = resources.GetResource<AudioResource>($"/Audio/_WF/CombatConsole/HighFleet/{cue}.wav").AudioStream;
                Assert.That(clip.ChannelCount, Is.EqualTo(2), "Console cues use the original stereo switch recordings.");
                Assert.That(clip.Length.TotalSeconds, Is.InRange(0.07, 1.0), "Short UI cues must decode completely.");
            }
            using var gunnery = new FireControlWindow();
            using var helm = new ShuttleConsoleWindow();
            Assert.That(gunnery.MinHeight, Is.LessThan(800));
            Assert.That(helm.MinHeight, Is.LessThan(1000));
            var camera = helm.FindControl<ShuttleCameraBar>("CameraBar");
            Assert.That(camera.Visible, Is.True);
            foreach (var mode in Enum.GetValues<ShuttleConsoleWindow.ShuttleConsoleMode>())
            {
                helm.SwitchMode(mode);
                Assert.That(camera.Visible, Is.EqualTo(mode == ShuttleConsoleWindow.ShuttleConsoleMode.Nav),
                    "Camera controls belong only to Navigation.");
                helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Nav);
                Assert.That(camera.Visible, Is.True, "Returning to Navigation must restore its camera controls.");
            }
            var navigation = helm.FindControl<NavScreen>("NavContainer");
            var altitude = Descendants(navigation).OfType<WFGlassGauge>().Single(gauge => gauge.Name == "WfAltitudeGauge");
            var climb = Descendants(navigation).OfType<WFGlassGauge>().Single(gauge => gauge.Name == "WfClimbGauge");
            var travel = navigation.FindControl<Label>("GridTravelState").Parent!;
            Assert.That(altitude.Visible || climb.Visible || travel.Visible, Is.False,
                "A console without altitude data must not show vertical-flight instruments.");
            var clientMaps = pair.Client.ResolveDependency<IMapManager>();
            var flightMap = clientMaps.CreateMap();
            var flightGrid = clientMaps.CreateGrid(flightMap);
            var clientEntities = pair.Client.EntMan;
            var flightMapUid = clientMaps.GetMapEntityId(flightMap);
            var flightTransform = clientEntities.GetComponent<TransformComponent>(flightGrid.Owner);
            clientEntities.EnsureComponent<CEZPhysicsComponent>(flightGrid.Owner).Velocity = 3f;
            navigation.SetShuttle(flightGrid.Owner);
            void CheckFlightInstruments(bool visible)
            {
                typeof(NavScreen).GetMethod("UpdateAltitude", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(navigation, new object[] { flightTransform });
                Assert.That(altitude.Visible, Is.EqualTo(visible));
                Assert.That(climb.Visible, Is.EqualTo(visible));
                Assert.That(travel.Visible, Is.EqualTo(visible), "Hidden travel text must leave no empty glass panel.");
                if (visible)
                    Assert.That(climb.Reading.Value, Is.EqualTo(3f));
            }
            CheckFlightInstruments(false);
            clientEntities.AddComponent<CEZMapComponent>(flightMapUid).Depth = 2;
            CheckFlightInstruments(true);
            clientEntities.RemoveComponent<CEZMapComponent>(flightMapUid);
            clientEntities.AddComponent<CEZTransitMapComponent>(flightMapUid);
            CheckFlightInstruments(true);
            clientEntities.RemoveComponent<CEZTransitMapComponent>(flightMapUid);
            CheckFlightInstruments(false);
            navigation.SetShuttle(null);
            clientMaps.DeleteMap(flightMap);
            foreach (var size in new[] { new Vector2(960, 640), new Vector2(1128, 776) })
            {
                helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Ship);
                helm.SetSize = size;
                helm.Measure(size);
                helm.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                var ship = helm.FindControl<ShipScreen>("ShipContainer");
                var mapView = ship.FindControl<ShipViewControl>("ShipView");
                Assert.That(camera.Visible, Is.False);
                var departments = Descendants(ship).OfType<CheckBox>().Single(toggle => toggle.Name == "DepartmentToggle");
                Assert.That(departments.GlobalPosition.X, Is.GreaterThanOrEqualTo(mapView.GlobalPosition.X + mapView.Width),
                    "The department toggle must sit beside the hull plot, not over it.");
                Assert.That(departments.Label.Width, Is.GreaterThan(30));
                Assert.That(Descendants(ship).OfType<Label>().Any(label => label.Text == Loc.GetString("wf-console-hull-controls")),
                    Is.False, "The ship plot must not retain a separate display-control header.");
                Assert.That(ship.FindControl<CheckBox>("DamageToggle").Label.Width, Is.GreaterThan(30),
                    "Mechanical checkbox styling must leave room for its caption.");
                Assert.That(mapView.Width, Is.GreaterThan(200));
                Assert.That(mapView.Height, Is.GreaterThan(150));
                Assert.That(mapView.GlobalPosition.Y + mapView.Height, Is.LessThanOrEqualTo(helm.GlobalPosition.Y + helm.Height),
                    "The inherited map size must not escape the console's content area.");
            }
            var gun = new NetEntity(710);
            FireControlConsoleBoundInterfaceState GunState(bool connected, int? ammo) => new(connected,
                new[] { new FireControllableEntry(gun, default, "Gauge test weapon", ammo, true) },
                new Content.Shared.Shuttles.BUIStates.NavInterfaceState(250, null, null, new(), default));
            var armed = GunState(true, 240);
            armed.Combat.FlareLaunchers.Add(new NetEntity(711));
            armed.Combat.Ammunition = 18;
            armed.Combat.Threats = 2;
            gunnery.UpdateStatus(armed);
            var meter = gunnery.WeaponsList[gun].Children.OfType<WFGlassGauge>().Single();
            Assert.That(meter.Reading.Value, Is.EqualTo(240));
            Assert.That(meter.Reading.Maximum, Is.GreaterThanOrEqualTo(240));
            foreach (var size in new[] { new Vector2(960, 600), new Vector2(960, 640), new Vector2(1180, 780) })
            {
                gunnery.SetSize = size;
                gunnery.Measure(size);
                gunnery.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(meter.Width, Is.GreaterThan(100), "An ammunition instrument must remain readable within its selection button.");
                Assert.That(meter.GlobalPosition.X + meter.Width, Is.LessThanOrEqualTo(gunnery.GlobalPosition.X + size.X));
                var rack = Descendants(gunnery).OfType<WFCountermeasureInstrument>().Single();
                var dispense = Descendants(gunnery).OfType<Button>().Single(button => button.HasStyleClass("WfDispense"));
                foreach (var gauge in rack.Parent!.Children.OfType<WFGlassGauge>())
                {
                    Assert.That(gauge.Width, Is.GreaterThanOrEqualTo(100));
                    var panel = Descendants(gunnery).Single(control => control.Name == "WfCountermeasurePanel");
                    Assert.That(gauge.GlobalPosition.Y + gauge.Height, Is.LessThanOrEqualTo(panel.GlobalPosition.Y + panel.Height),
                        "Linear flare gauges must remain inside the compact countermeasure bank.");
                    Assert.That(gauge.GlobalPosition.X + gauge.Width, Is.LessThanOrEqualTo(gunnery.GlobalPosition.X + size.X),
                        "Countermeasure gauges must fit at minimum console width.");
                }
            }
            gunnery.UpdateStatus(GunState(true, 0));
            Assert.That(meter.Reading.Value, Is.Zero, "Empty ammunition is a real zero.");
            gunnery.UpdateStatus(GunState(false, 240));
            Assert.That(meter.Reading.Value, Is.Null, "A disconnected console must not retain a live needle.");
            gunnery.UpdateStatus(GunState(true, null));
            Assert.That(meter.Reading.Value, Is.Null, "Unknown ammo capacity must not be displayed as an empty magazine.");
            var hull = helm.FindControl<ShipScreen>("ShipContainer");
            hull.UpdateStatus(new ShipStatusMessage(null, new(), new ShipStatusSummary { WorstIntegrity = 0.85f, DamagedTiles = 7 }));
            var hullMeters = Descendants(hull).OfType<WFGlassGauge>().ToArray();
            Assert.That(hullMeters.Any(gauge => Math.Abs((gauge.Reading.Value ?? -100) - 85) < 0.001), Is.True);
            Assert.That(hullMeters.Any(gauge => gauge.Reading.Value == 7), Is.True);
            hull.SetShuttle(null);
            Assert.That(hullMeters.Any(gauge => gauge.Reading.Value == 7), Is.True,
                "A repeated shuttle assignment must not blank the latest telemetry sweep.");
            hull.ClearStatus();
            Assert.That(hullMeters.All(gauge => gauge.Reading.Value == null), Is.True, "Cleared telemetry must blank every hull instrument.");
            var cfg = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = cfg.GetCVar(WolfgateCVars.UiStyle);
            var requests = 0;
            gunnery.CombatMessage += _ => requests++;
            gunnery.UpdateStatus(armed);
            gunnery.WeaponsList[gun].Pressed = true;
            gunnery.OpenCentered();
            helm.OpenCentered();
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro, WolfgateSkins.Futurist, WolfgateSkins.Retro })
                {
                    cfg.SetCVar(WolfgateCVars.UiStyle, skin.Id);
                    Assert.That(WFInstrumentTheme.Skin, Is.SameAs(skin));
                    Assert.That(WFInstrumentTheme.Digital, Is.EqualTo(skin == WolfgateSkins.Futurist));
                    Assert.That(Descendants(gunnery).OfType<Label>().Single(label => label.Text == "WOLFGATE / FIRE CONTROL").FontColorOverride,
                        Is.EqualTo(skin.Accent), "Open labels must change palette with the selected theme.");
                    Assert.That(gunnery.WeaponsList[gun].Children.OfType<WFGlassGauge>().Single(), Is.SameAs(meter),
                        "A style switch must retain the same controls and ammunition binding.");
                    Assert.That(meter.Reading.Value, Is.EqualTo(240));
                    Assert.That(gunnery.WeaponsList[gun].Pressed, Is.True);
                    Assert.That(gunnery.WeaponsList[gun].Height, Is.GreaterThanOrEqualTo(86),
                        "A sparse battery must keep its embedded ammunition instruments visible.");
                    foreach (var console in new Control[] { gunnery, helm })
                    {
                        var size = new Vector2(960, 600);
                        console.SetSize = size;
                        console.Measure(size);
                        console.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                        Assert.That(console.DesiredSize.X, Is.LessThanOrEqualTo(size.X));
                    }
                }
                Assert.That(requests, Is.Zero, "Changing a theme must not fire weapons, save groups or toggle automatic flares.");
                gunnery.Close();
                cfg.SetCVar(WolfgateCVars.UiStyle, WolfgateSkins.Futurist.Id);
                gunnery.OpenCentered();
                Assert.That(Descendants(gunnery).OfType<Label>().Single(label => label.Text == "WOLFGATE / FIRE CONTROL").FontColorOverride,
                    Is.EqualTo(WolfgateSkins.Futurist.Accent), "A closed console must adopt the current skin when reopened.");
            }
            finally
            {
                gunnery.Close();
                helm.Close();
                cfg.SetCVar(WolfgateCVars.UiStyle, originalSkin);
            }
        });
        await pair.CleanReturnAsync();
    }
    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var item in Descendants(child))
            yield return item;
    }
}
