#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._Mono.FireControl.UI;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Server._WF.CombatConsole;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server.Power.Components;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.CombatConsole;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.CombatConsole;

/// <summary>Checks actual ammunition providers and the non-scrolling gunnery battery at compact sizes.</summary>
[TestFixture]
public sealed class WFGunnerySupplyTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFGunneryFiniteTest
  components:
  - type: BasicEntityAmmoProvider
    proto: BulletHawk
    capacity: 10
    count: 0
- type: entity
  id: WFGunneryInfiniteTest
  components:
  - type: BasicEntityAmmoProvider
    proto: BulletHawk
- type: entity
  parent: WFGunneryFiniteTest
  id: WFGunneryRechargeTest
  components:
  - type: RechargeBasicEntityAmmo
    rechargeCooldown: 3600
";

    [Test]
    public async Task ProvidersAndAdaptiveBattery()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var system = em.System<WFCombatConsoleSystem>();
            foreach (var (prototype, kind) in new[]
            {
                ("WFGunneryFiniteTest", WFWeaponSupplyKind.Finite),
                ("WFGunneryInfiniteTest", WFWeaponSupplyKind.Infinite),
                ("WFGunneryRechargeTest", WFWeaponSupplyKind.Recharging),
                ("SunnyMagazine", WFWeaponSupplyKind.Infinite),
                ("WeaponTurretFlare", WFWeaponSupplyKind.Infinite),
            })
            {
                var uid = em.SpawnEntity(prototype, map.GridCoords);
                var supply = system.GetWeaponSupply(uid);
                Assert.That(supply.Kind, Is.EqualTo(kind), prototype);
                if (kind == WFWeaponSupplyKind.Infinite)
                    Assert.That(supply.Count, Is.Null, "Unlimited providers must not report zero stored rounds as an empty magazine.");
                else
                {
                    Assert.That(supply.Count, Is.Zero);
                    Assert.That(supply.Capacity, Is.EqualTo(10));
                }
                em.DeleteEntity(uid);
            }
            var energy = em.SpawnEntity(null, map.GridCoords);
            var provider = em.AddComponent<ProjectileBatteryAmmoProviderComponent>(energy);
            provider.Shots = 3;
            provider.Capacity = 10;
            Assert.That(system.GetWeaponSupply(energy), Is.EqualTo(new WFWeaponSupply(WFWeaponSupplyKind.Energy, 3, 10)));
            em.DeleteEntity(energy);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
            using var window = new FireControlWindow();
            window.OpenCentered();
            var entries = Enumerable.Range(1, 24).Select(index => new FireControllableEntry(new NetEntity(810 + index),
                default, $"Battery {index}", 0, true)).ToArray();
            var state = new FireControlConsoleBoundInterfaceState(true, entries,
                new NavInterfaceState(250, null, null, new(), default));
            state.Combat.WeaponSupplies[entries[0].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Infinite, null, null);
            state.Combat.WeaponSupplies[entries[1].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Energy, 3, 10);
            state.Combat.WeaponSupplies[entries[2].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Recharging, 0, 10);
            state.Combat.WeaponSupplies[entries[3].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Finite, 0, 10);
            window.UpdateStatus(state);
            WFWeaponRow Row(int index) => window.WeaponsList[entries[index].NetEntity].Children.OfType<WFWeaponRow>().Single();
            Assert.That(Row(0).Reading.Text, Is.EqualTo(Loc.GetString("wf-gauge-unlimited")));
            Assert.That(Row(0).Reading.Value, Is.GreaterThan(0));
            Assert.That(window.WeaponsList[entries[0].NetEntity].ModulateSelfOverride, Is.Null);
            Assert.That(Row(1).Reading.Value, Is.EqualTo(30));
            Assert.That(Row(1).Reading.Maximum, Is.EqualTo(100));
            Assert.That(Row(2).Reading.Text, Is.EqualTo(Loc.GetString("wf-gauge-recharging")));
            Assert.That(Row(3).Reading.Value, Is.Zero);
            var commands = new List<BoundUserInterfaceMessage>();
            window.CombatMessage += commands.Add;
            window.WeaponsList[entries[0].NetEntity].Pressed = true;
            window.WeaponsList[entries[23].NetEntity].Pressed = true;
            window.OnWeaponSelectionChanged?.Invoke();
            var selection = new[] { entries[0].NetEntity, entries[23].NetEntity };
            Button Named(string name) => Descendants(window).OfType<Button>().Single(button => button.Name == name);
            void Layout(Vector2 size)
            {
                window.SetSize = size;
                foreach (var control in Descendants(window))
                    control.InvalidateMeasure();
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            }
            try
            {
                foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(960, 600), new Vector2(1180, 780) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, theme);
                    Layout(size);
                    var battery = Descendants(window).OfType<WFWeaponGrid>().Single();
                    for (var parent = battery.Parent; parent != null; parent = parent.Parent)
                        Assert.That(parent, Is.Not.InstanceOf<ScrollContainer>(), "The weapon battery must never become a scrolling list.");
                    Assert.That(battery.PageCount, Is.GreaterThan(1), "A dense battery pages instead of shrinking its weapon rows.");
                    var lastPage = battery.PageCount - 1;
                    battery.SetPage(lastPage);
                    Layout(size);
                    Assert.That(battery.PageIndex, Is.EqualTo(lastPage), "Parent measurement must leave the last weapon page reachable.");
                    battery.SetPage(0);
                    var seen = new HashSet<Button>();
                    for (var page = 0; page < battery.PageCount; page++)
                    {
                        Layout(size);
                        var visible = window.WeaponsList.Values.Where(button => button.VisibleInTree).ToArray();
                        Assert.That(visible.Length, Is.InRange(1, 23));
                        Assert.That(visible.Select(button => button.Position.X).Distinct().Count(), Is.EqualTo(1));
                        foreach (var button in visible)
                        {
                            Assert.That(seen.Add(button), Is.True);
                            Assert.That(button.Height, Is.InRange(48, 56));
                            Assert.That(button.Width, Is.GreaterThanOrEqualTo(250));
                            var row = button.Children.OfType<WFWeaponRow>().Single();
                            Assert.That(row.NameLabel.Align, Is.EqualTo(Label.AlignMode.Left), "Long names must preserve their identifying prefix.");
                            foreach (var label in new[] { row.NameLabel, row.SupplyLabel })
                            {
                                var font = label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
                                    ? styled : pair.Client.ResolveDependency<IUserInterfaceManager>().ThemeDefaults.LabelFont);
                                var width = 0f;
                                foreach (var rune in label.Text!.EnumerateRunes())
                                    width += font.GetCharMetrics(rune, label.UIScale)?.Advance ?? 0;
                                Assert.That(label.PixelWidth + 1, Is.GreaterThanOrEqualTo(width), $"Battery identifiers and supply must fit: {label.Text}");
                                Assert.That(label.PixelHeight + 1, Is.GreaterThanOrEqualTo(font.GetHeight(label.UIScale)));
                            }
                            Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(battery.GlobalPosition.X));
                            Assert.That(button.GlobalPosition.Y, Is.GreaterThanOrEqualTo(battery.GlobalPosition.Y));
                            Assert.That(button.GlobalPosition.X + button.Width, Is.LessThanOrEqualTo(battery.GlobalPosition.X + battery.Width + 1));
                            Assert.That(button.GlobalPosition.Y + button.Height, Is.LessThanOrEqualTo(battery.GlobalPosition.Y + battery.Height + 1));
                        }
                        Assert.That(window.WeaponsList.Where(weapon => weapon.Value.Pressed).Select(weapon => weapon.Key), Is.EquivalentTo(selection),
                            "Page navigation and theme changes must retain every selected weapon.");
                        window.UpdateStatus(state);
                        Layout(size);
                        Assert.That(battery.PageIndex, Is.EqualTo(page), "Supply updates must leave the current weapon page in place.");
                        if (page < battery.PageCount - 1)
                            WFButtonTestInput.Click(Named("WfWeaponNext"));
                    }
                    Assert.That(seen, Is.EquivalentTo(window.WeaponsList.Values));
                    Assert.That(commands, Is.Empty, "Paging, snapshots and restyling must not send gunnery commands.");
                    var batteryPanel = Descendants(window).Single(control => control.Name == "WfWeaponBattery");
                    Assert.That(batteryPanel.GlobalPosition.X - window.GlobalPosition.X, Is.LessThan(40), "The battery must not sit in an unused centered allocation.");
                    Assert.That(batteryPanel.Width, Is.LessThan(size.X * 0.4f), "Weapon controls must remain a sidebar.");
                    Assert.That(window.Radar.Width, Is.GreaterThan(size.X * 0.5f), "The fire director must remain the primary control across most of the console.");
                    Assert.That(window.Radar.Height, Is.GreaterThan(batteryPanel.Height * 0.65f), "Status and support controls must not crowd out the fire director vertically.");
                    var flares = Descendants(window).Single(control => control.Name == "WfCountermeasurePanel");
                    Assert.That(flares.Visible, Is.False, "A ship without launchers must not reserve space for flare controls.");
                }
                WFButtonTestInput.Toggle(Named("CockpitGroupStore"), true);
                Layout(new Vector2(1180, 780));
                WFButtonTestInput.Click(Named("CockpitGroupSave2"));
                var saved = commands.OfType<WFSaveWeaponGroupMessage>().Single();
                Assert.That(saved.Weapons, Is.EquivalentTo(selection), "A standalone group save includes off-page selections.");
                state.Connected = false;
                window.UpdateStatus(state);
                Assert.That(Row(0).Reading.Value, Is.Null, "Disconnected infinite weapons must not retain live supply telemetry.");
            }
            finally
            {
                settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
                window.Close();
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FlaresUseClearLanesDistractEligibleLocksAndRefreshClosedAlerts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var entities = new List<EntityUid>();
        var seekers = new List<EntityUid>();
        EntityUid consoleUid = default;
        EntityUid launcher = default;
        EntityUid decoy = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 6; x++)
            for (var y = 0; y < 3; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            EntityUid Spawn(string? prototype, EntityCoordinates coordinates)
            {
                var entity = em.SpawnEntity(prototype, coordinates);
                entities.Add(entity);
                return entity;
            }
            consoleUid = Spawn("ComputerGunneryConsole", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(consoleUid).Powered = true;
            var actor = Spawn("MobHuman", map.GridCoords);
            var serverUid = Spawn(null, map.GridCoords);
            var server = em.AddComponent<FireControlServerComponent>(serverUid);
            server.ConnectedGrid = map.Grid.Owner;
            server.ProcessingPower = 100;
            server.Consoles.Add(consoleUid);
            em.EnsureComponent<FireControlGridComponent>(map.Grid.Owner).ControllingServer = serverUid;
            var console = em.GetComponent<FireControlConsoleComponent>(consoleUid);
            console.ConnectedServer = serverUid;
            launcher = Spawn("WeaponTurretFlare", new EntityCoordinates(map.Grid.Owner, new Vector2(4.5f, 1.5f)));
            em.GetComponent<FireControllableComponent>(launcher).ControllingServer = serverUid;
            server.Controlled.Add(launcher);
            Spawn("WallSolid", new EntityCoordinates(map.Grid.Owner, new Vector2(4.5f, 0.5f)));
            var fire = em.System<FireControlSystem>();
            Assert.That(fire.WfFlareTarget(launcher, out var lane), Is.True,
                "An inward obstruction must not stop a launcher with a clear outward lane.");
            var transforms = em.System<SharedTransformSystem>();
            var lanePoint = transforms.ToMapCoordinates(em.GetCoordinates(lane)).Position;
            Assert.That(lanePoint.X, Is.GreaterThan(transforms.GetWorldPosition(launcher).X));
            var ui = em.System<SharedUserInterfaceSystem>();
            ui.OpenUi(consoleUid, FireControlConsoleUiKey.Key, actor);
            ui.RaiseUiMessage(consoleUid, FireControlConsoleUiKey.Key, new WFDispenseFlaresMessage { Actor = actor });
            em.System<GunSystem>().Update(1f / 30f);
            Assert.That(em.GetComponent<WFFlareLauncherComponent>(launcher).NextBurst, Is.GreaterThan(TimeSpan.Zero),
                "Manual DISPENSE must really fire through the unobstructed lane.");
            var launched = em.EntityQuery<WFFlareDecoyComponent>(true).ToArray();
            Assert.That(launched, Is.Not.Empty, "The real Sunny ammunition path must mark its emitted countermeasures.");
            foreach (var flare in launched)
                em.DeleteEntity(flare.Owner);
            em.RemoveComponent<GunComponent>(launcher);

            // Only the fixture's emitted flare participates in these range/arc comparisons.
            decoy = Spawn("ShipSunnyFlare", new EntityCoordinates(map.MapUid, new Vector2(10, 0)));
            em.EventBus.RaiseLocalEvent(launcher, new AmmoShotEvent { FiredProjectiles = new List<EntityUid> { decoy } });
            Assert.That(em.GetComponent<WFFlareDecoyComponent>(decoy).ProtectedGrid, Is.EqualTo(map.Grid.Owner));
            for (var i = 0; i < 5; i++)
            {
                var missile = Spawn(null, new EntityCoordinates(map.MapUid, new Vector2(20, 0)));
                seekers.Add(missile);
                var projectile = em.AddComponent<ProjectileComponent>(missile);
                var seeker = em.AddComponent<TargetSeekingComponent>(missile);
                seeker.Launched = true;
                seeker.ScanArc = 90;
                em.System<TargetSeekingSystem>().SetSeekerTarget((missile, seeker), map.Grid.Owner);
                transforms.SetWorldRotation(missile, Angle.FromWorldVec(-Vector2.UnitX));
                if (i == 1) seeker.DetectionRange = 5;
                if (i == 2) transforms.SetWorldRotation(missile, Angle.FromWorldVec(Vector2.UnitY));
                if (i == 3) projectile.Shooter = consoleUid;
                if (i == 4) seeker.SeekingDisabled = true;
            }
            var settings = em.EnsureComponent<WFCombatConsoleComponent>(consoleUid);
            settings.Automatic = false;
            ui.CloseUi(consoleUid, FireControlConsoleUiKey.Key, actor);
            settings.Threats = 99;
            ui.OpenUi(consoleUid, FireControlConsoleUiKey.Key, actor);
            Assert.That(ui.TryGetUiState<FireControlConsoleBoundInterfaceState>(consoleUid, FireControlConsoleUiKey.Key, out var snapshot), Is.True);
            Assert.That(snapshot!.Combat.Threats, Is.EqualTo(3),
                "Reopening must rescan real threats immediately instead of replaying stale cached alerts.");
            ui.CloseUi(consoleUid, FireControlConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TargetSeekingComponent>(seekers[0]).CurrentTarget, Is.EqualTo(decoy),
                "A closer emitted flare inside the seeker's arc must distract an already locked missile.");
            foreach (var missile in seekers.Skip(1))
                Assert.That(em.GetComponent<TargetSeekingComponent>(missile).CurrentTarget, Is.EqualTo(map.Grid.Owner),
                    "Out-of-range, rearward, friendly and disabled seekers must keep their existing target.");
            Assert.That(em.System<WFCombatConsoleSystem>().GetState(consoleUid,
                em.GetComponent<FireControlConsoleComponent>(consoleUid)).Threats, Is.EqualTo(2),
                "The distracted missile must disappear from the ship's live lock count.");
            for (var x = 3; x <= 5; x++)
            for (var y = 0; y <= 2; y++)
            {
                if (x == 4 && (y == 0 || y == 1))
                    continue;
                entities.Add(em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, new Vector2(x + 0.5f, y + 0.5f))));
            }
            Assert.That(em.System<FireControlSystem>().WfFlareTarget(launcher, out _), Is.False,
                "Countermeasures must not bypass a hull that blocks every firing lane.");
            foreach (var missile in seekers)
                em.DeleteEntity(missile);
            foreach (var entity in entities)
            {
                if (em.EntityExists(entity))
                    em.DeleteEntity(entity);
            }
        });
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }
}
