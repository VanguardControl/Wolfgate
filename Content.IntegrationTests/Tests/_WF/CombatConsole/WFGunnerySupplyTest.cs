using System.Linq;
using System.Numerics;
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
            WFGlassGauge Gauge(int index) => window.WeaponsList[entries[index].NetEntity].Children.OfType<WFGlassGauge>().Single();
            Assert.That(Gauge(0).Reading.Text, Is.EqualTo(Loc.GetString("wf-gauge-unlimited")));
            Assert.That(Gauge(0).Reading.Value, Is.GreaterThan(0));
            Assert.That(window.WeaponsList[entries[0].NetEntity].ModulateSelfOverride, Is.Null);
            Assert.That(Gauge(1).Reading.Value, Is.EqualTo(30));
            Assert.That(Gauge(1).Reading.Maximum, Is.EqualTo(100));
            Assert.That(Gauge(2).Reading.Text, Is.EqualTo(Loc.GetString("wf-gauge-recharging")));
            Assert.That(Gauge(3).Reading.Value, Is.Zero);
            try
            {
                foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(960, 600), new Vector2(1180, 780) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, theme);
                    window.SetSize = size;
                    foreach (var control in Descendants(window))
                        control.InvalidateMeasure();
                    window.Measure(size);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    var battery = Descendants(window).OfType<WFWeaponGrid>().Single();
                    for (var parent = battery.Parent; parent != null; parent = parent.Parent)
                        Assert.That(parent, Is.Not.InstanceOf<ScrollContainer>(), "The weapon battery must never become a scrolling list.");
                    Assert.That(window.WeaponsList.Values.All(button => button.VisibleInTree), Is.True);
                    foreach (var button in window.WeaponsList.Values)
                    {
                        Assert.That(button.Height, Is.GreaterThanOrEqualTo(34));
                        Assert.That(button.Label.Align, Is.EqualTo(Label.AlignMode.Left), "Long names must preserve their identifying prefix.");
                        Assert.That(button.Text!.Split('\n'), Has.Length.EqualTo(2), "Name and supply must occupy separate readable lines.");
                        var font = button.Label.FontOverride ?? (button.Label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
                            ? styled : pair.Client.ResolveDependency<IUserInterfaceManager>().ThemeDefaults.LabelFont);
                        foreach (var line in button.Text.Split('\n'))
                        {
                            var width = 0f;
                            foreach (var rune in line.EnumerateRunes())
                                width += font.GetCharMetrics(rune, button.UIScale)?.Advance ?? 0;
                            Assert.That(button.Label.PixelWidth + 1, Is.GreaterThanOrEqualTo(width), $"Battery identifiers and supply must fit: {line}");
                        }
                        Assert.That(button.Label.PixelHeight + 1, Is.GreaterThanOrEqualTo(font.GetHeight(button.UIScale) + font.GetLineHeight(button.UIScale)));
                        Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(battery.GlobalPosition.X));
                        Assert.That(button.GlobalPosition.Y, Is.GreaterThanOrEqualTo(battery.GlobalPosition.Y));
                        Assert.That(button.GlobalPosition.X + button.Width, Is.LessThanOrEqualTo(battery.GlobalPosition.X + battery.Width + 1));
                        Assert.That(button.GlobalPosition.Y + button.Height, Is.LessThanOrEqualTo(battery.GlobalPosition.Y + battery.Height + 1));
                    }
                    var batteryPanel = Descendants(window).Single(control => control.Name == "WfWeaponBattery");
                    Assert.That(batteryPanel.GlobalPosition.X - window.GlobalPosition.X, Is.LessThan(40), "The battery must not sit in an unused centered allocation.");
                    Assert.That(batteryPanel.Width, Is.GreaterThan(size.X * 0.35f));
                    var flares = Descendants(window).Single(control => control.Name == "WfCountermeasurePanel");
                    Assert.That(flares.Height, Is.LessThanOrEqualTo(144), "Countermeasures must leave the tactical plot most of its height.");
                    Assert.That(Descendants(flares).OfType<WFGlassGauge>().All(gauge => gauge.Strip), Is.True);
                }
                state.Connected = false;
                window.UpdateStatus(state);
                Assert.That(Gauge(0).Reading.Value, Is.Null, "Disconnected infinite weapons must not retain live supply telemetry.");
            }
            finally
            {
                settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
                window.Close();
            }
        });
        await pair.CleanReturnAsync();
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }
}
