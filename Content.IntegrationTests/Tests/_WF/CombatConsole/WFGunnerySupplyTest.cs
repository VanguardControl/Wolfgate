using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Client._Mono.FireControl.UI;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Server._WF.CombatConsole;
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
                            Press(Named("WfWeaponNext"));
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
                Toggle(Named("CockpitGroupStore"), true);
                Press(Named("CockpitGroupSave2"));
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

    private static void Press(BaseButton button)
    {
        var handler = (Action<BaseButton.ButtonEventArgs>?) typeof(BaseButton)
            .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }

    private static void Toggle(BaseButton button, bool pressed)
    {
        button.Pressed = pressed;
        var handler = (Action<BaseButton.ButtonToggledEventArgs>?) typeof(BaseButton)
            .GetField("OnToggled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonToggledEventArgs(pressed, button, null!));
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }
}
