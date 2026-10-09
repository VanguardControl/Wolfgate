using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.CombatConsole;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Exercises the embedded battery's existing command handlers, supply telemetry and compact layout.</summary>
[TestFixture]
public sealed class WFCockpitGunneryPanelTest
{
    [Test]
    public async Task EmbeddedBatteryKeepsGroupsFlaresAndSupply()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
            var windows = Tree(ui.RootControl).OfType<BaseWindow>().ToArray();
            using var panel = new WFCockpitGunneryPanel();
            var commands = new List<BoundUserInterfaceMessage>();
            panel.Command += commands.Add;
            ui.RootControl.AddChild(panel);
            var entries = Enumerable.Range(1, 25).Select(index => new FireControllableEntry(new NetEntity(910 + index),
                default, $"Battery {index}", 6, true)).ToArray();
            var state = new FireControlConsoleBoundInterfaceState(true, entries,
                new NavInterfaceState(250, null, null, new(), default));
            var combat = state.Combat;
            combat.FlareLaunchers.Add(entries[24].NetEntity);
            combat.Ammunition = 8;
            combat.Groups[0].AddRange(new[] { entries[0].NetEntity, entries[2].NetEntity });
            combat.WeaponSupplies[entries[0].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Infinite, null, null);
            combat.WeaponSupplies[entries[1].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Energy, 3, 10);
            combat.WeaponSupplies[entries[2].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Recharging, 0, 10);
            combat.WeaponTypes[entries[0].NetEntity] = ShipGunType.Ballistic;
            panel.UpdateState(state);
            var grid = Tree(panel).OfType<WFWeaponGrid>().Single();
            var buttons = grid.Children.OfType<Button>().ToArray();
            Button Named(string name) => Tree(panel).OfType<Button>().Single(button => button.Name == name);
            Assert.That(Tree(ui.RootControl).OfType<BaseWindow>(), Is.EqualTo(windows), "The cockpit battery must not open a gunnery window.");
            Assert.That(buttons.Count(button => button.Visible), Is.EqualTo(24), "Flares are excluded from offensive selection.");
            Assert.That(buttons[0].Text, Does.Contain(Loc.GetString("wf-gauge-unlimited")));
            Assert.That(buttons[0].ModulateSelfOverride, Is.Null);
            Assert.That(buttons[1].Children.OfType<WFGlassGauge>().Single().Reading.Value, Is.EqualTo(30));
            Assert.That(buttons[2].Text, Does.Contain(Loc.GetString("wf-gauge-recharging")));
            Press(Named("CockpitGroupRecall0"));
            Assert.That(panel.SelectedWeapons, Is.EquivalentTo(combat.Groups[0]));
            var store = Named("CockpitGroupStore");
            Toggle(store, true);
            Assert.That(Named("CockpitGroupSave2").Visible, Is.True);
            Press(Named("CockpitGroupSave2"));
            var saved = commands.OfType<WFSaveWeaponGroupMessage>().Single();
            Assert.That(saved.Slot, Is.EqualTo(2));
            Assert.That(saved.Weapons, Is.EquivalentTo(combat.Groups[0]));
            Assert.That(store.Pressed, Is.False);
            Assert.That(Named("CockpitGroupSave2").Visible, Is.False);
            Toggle(Named("CockpitAutomaticFlares"), true);
            Assert.That(commands.OfType<WFAutomaticFlaresMessage>().Single().Enabled, Is.True);
            Assert.That(Named("CockpitDispenseFlares").Disabled, Is.False);
            Press(Named("CockpitDispenseFlares"));
            Assert.That(commands.OfType<WFDispenseFlaresMessage>().Count(), Is.EqualTo(1));
            Press(Named("CockpitGunneryRefresh"));
            Assert.That(commands.OfType<FireControlConsoleRefreshServerMessage>().Count(), Is.EqualTo(1));
            var supply = Tree(panel).OfType<WFGlassGauge>().Single(gauge => gauge.Name == "CockpitFlareSupply");
            Assert.That(supply.Reading.Value, Is.EqualTo(8));
            combat.Cooldown = 3;
            panel.UpdateStatus(state);
            Assert.That(Named("CockpitDispenseFlares").Disabled, Is.True, "The embedded button retains the existing lockout.");
            try
            {
                foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(296, 450), new Vector2(400, 700) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, theme);
                    foreach (var child in Tree(panel))
                        child.InvalidateMeasure();
                    panel.Measure(size);
                    panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    Assert.That(Tree(panel).OfType<ScrollContainer>(), Is.Empty);
                    if (size.X == 296)
                        Assert.That(buttons.Where(button => button.Visible).Select(button => button.Position.X).Distinct().Count(),
                            Is.EqualTo(3), "The compact battery needs wide enough columns for supply and identifiers.");
                    foreach (var button in Tree(panel).OfType<Button>().Where(button => button.VisibleInTree))
                    {
                        Assert.That(button.Height, Is.GreaterThanOrEqualTo(32), $"{theme}: {button.Name}");
                        Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(panel.GlobalPosition.X));
                        Assert.That(button.GlobalPosition.Y, Is.GreaterThanOrEqualTo(panel.GlobalPosition.Y));
                        Assert.That(button.GlobalPosition.X + button.Width, Is.LessThanOrEqualTo(panel.GlobalPosition.X + panel.Width + 1));
                        Assert.That(button.GlobalPosition.Y + button.Height, Is.LessThanOrEqualTo(panel.GlobalPosition.Y + panel.Height + 1));
                        if (!button.HasStyleClass("WfWeapon"))
                            continue;
                        var font = button.Label.FontOverride ?? (button.Label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
                            ? styled : ui.ThemeDefaults.LabelFont);
                        Assert.That(button.Label.PixelHeight + 1,
                            Is.GreaterThanOrEqualTo(font.GetHeight(button.UIScale) + font.GetLineHeight(button.UIScale)),
                            $"{theme}/{size}: both battery label lines must fit vertically");
                        foreach (var line in button.Text!.Split('\n'))
                        {
                            var width = 0f;
                            foreach (var rune in line.EnumerateRunes())
                                width += font.GetCharMetrics(rune, button.UIScale)?.Advance ?? 0;
                            Assert.That(button.Label.PixelWidth + 1, Is.GreaterThanOrEqualTo(width), $"{theme}/{size}: {line}");
                        }
                    }
                }
                panel.ClearSelection();
                Assert.That(panel.SelectedWeapons, Is.Empty);
                panel.UpdateState(null);
                Assert.That(grid.ChildCount, Is.Zero);
                Assert.That(supply.Reading.Value, Is.Null);
                Assert.That(Named("CockpitAutomaticFlares").Disabled, Is.True);
                Assert.That(Named("CockpitDispenseFlares").Disabled, Is.True);
            }
            finally
            {
                panel.Parent?.RemoveChild(panel);
                settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
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

    private static IEnumerable<Control> Tree(Control root) => WFCockpitLease.Descendants(root);
}
