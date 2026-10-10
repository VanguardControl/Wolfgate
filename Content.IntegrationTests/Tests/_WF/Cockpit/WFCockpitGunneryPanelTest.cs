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
            combat.Groups[0].AddRange(new[] { entries[0].NetEntity, entries[23].NetEntity });
            combat.WeaponSupplies[entries[0].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Infinite, null, null);
            combat.WeaponSupplies[entries[1].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Energy, 3, 10);
            combat.WeaponSupplies[entries[2].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Recharging, 0, 10);
            combat.WeaponTypes[entries[0].NetEntity] = ShipGunType.Ballistic;
            panel.UpdateState(state);
            var grid = Tree(panel).OfType<WFWeaponGrid>().Single();
            var buttons = grid.Children.OfType<Button>().ToArray();
            Button Named(string name) => Tree(panel).OfType<Button>().Single(button => button.Name == name);
            Label NamedLabel(string name) => Tree(panel).OfType<Label>().Single(label => label.Name == name);
            Assert.That(Tree(ui.RootControl).OfType<BaseWindow>(), Is.EqualTo(windows), "The cockpit battery must not open a gunnery window.");
            void Layout(Vector2 size)
            {
                foreach (var child in Tree(panel))
                    child.InvalidateMeasure();
                panel.Measure(size);
                panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            }
            WFWeaponRow Row(Button button) => button.Children.OfType<WFWeaponRow>().Single();
            void CheckFinalPage(Vector2 size)
            {
                var lastPage = grid.PageCount - 1;
                grid.SetPage(lastPage);
                Layout(size);
                Assert.That(grid.PageIndex, Is.EqualTo(lastPage), "Parent measurement must not prevent reaching the last weapon page.");
                var visible = buttons.Where(button => button.Visible).ToArray();
                grid.Measure(new Vector2(grid.Width, grid.Height + 160));
                Assert.That(grid.PageIndex, Is.EqualTo(lastPage), "A provisional larger measurement must not change the operator's page.");
                Assert.That(buttons.Where(button => button.Visible), Is.EqualTo(visible), "Measuring a candidate layout must not hide the active page's weapons.");
                Layout(size);
                Assert.That(grid.PageIndex, Is.EqualTo(lastPage), "The final parent arrangement must retain the last page.");
            }
            Layout(new Vector2(296, 450));
            Assert.That(grid.PageCount, Is.GreaterThan(1), "A large battery must page rather than shrink every row.");
            Assert.That(buttons.Count(button => button.Visible), Is.InRange(1, 23));
            Assert.That(buttons[24].Visible, Is.False, "Flares are excluded from offensive selection.");
            Assert.That(Row(buttons[0]).Reading.Text, Does.Contain(Loc.GetString("wf-gauge-unlimited")));
            Assert.That(buttons[0].ModulateSelfOverride, Is.Null);
            Assert.That(Row(buttons[1]).Reading.Value, Is.EqualTo(30));
            Assert.That(Row(buttons[2]).Reading.Text, Does.Contain(Loc.GetString("wf-gauge-recharging")));
            Assert.That(Row(buttons[3]).Reading.Value, Is.EqualTo(6), "Finite supply must remain an exact count.");
            Press(Named("CockpitGroupRecall0"));
            Assert.That(panel.SelectedWeapons, Is.EquivalentTo(combat.Groups[0]), "A group includes weapons on other pages.");
            var firstPage = buttons.Where(button => button.Visible).ToArray();
            Press(Named("WfWeaponNext"));
            Layout(new Vector2(296, 450));
            Assert.That(grid.PageIndex, Is.EqualTo(1));
            Assert.That(buttons.Where(button => button.Visible).Intersect(firstPage), Is.Empty);
            Assert.That(panel.SelectedWeapons, Is.EquivalentTo(combat.Groups[0]), "Paging must not drop off-page firing selections.");
            var secondPageWeapon = buttons.First(button => button.Visible && !button.Pressed);
            Toggle(secondPageWeapon, true);
            var expectedSelection = combat.Groups[0].Append(entries[Array.IndexOf(buttons, secondPageWeapon)].NetEntity).ToArray();
            Assert.That(panel.SelectedWeapons, Is.EquivalentTo(expectedSelection));
            panel.UpdateStatus(state);
            Layout(new Vector2(296, 450));
            Assert.That(grid.PageIndex, Is.EqualTo(1), "Authoritative supply snapshots must not send the operator back to page one.");
            Assert.That(panel.SelectedWeapons, Is.EquivalentTo(expectedSelection));
            Assert.That(commands, Is.Empty, "Page navigation and received state must not send control commands.");
            var store = Named("CockpitGroupStore");
            Toggle(store, true);
            Assert.That(Named("CockpitGroupSave2").Visible, Is.True);
            Press(Named("CockpitGroupSave2"));
            var saved = commands.OfType<WFSaveWeaponGroupMessage>().Single();
            Assert.That(saved.Slot, Is.EqualTo(2));
            Assert.That(saved.Weapons, Is.EquivalentTo(expectedSelection), "Saving a group includes selected weapons on all pages.");
            Assert.That(store.Pressed, Is.False);
            Assert.That(Named("CockpitGroupSave2").Visible, Is.False);
            Toggle(Named("CockpitAutomaticFlares"), true);
            Assert.That(commands.OfType<WFAutomaticFlaresMessage>().Single().Enabled, Is.True);
            Assert.That(Named("CockpitDispenseFlares").Disabled, Is.False);
            Press(Named("CockpitDispenseFlares"));
            Assert.That(commands.OfType<WFDispenseFlaresMessage>().Count(), Is.EqualTo(1));
            Press(Named("CockpitGunneryRefresh"));
            Assert.That(commands.OfType<FireControlConsoleRefreshServerMessage>().Count(), Is.EqualTo(1));
            var supply = NamedLabel("CockpitFlareSupply");
            var flarePanel = Tree(panel).Single(control => control.Name == "WfCountermeasurePanel");
            Assert.That(supply.Text, Is.EqualTo(Loc.GetString("wf-gunnery-flare-ready", ("ammo", "8"))));
            combat.FlareLaunchers.Clear();
            panel.UpdateStatus(state);
            Assert.That(flarePanel.Visible, Is.False);
            Assert.That(NamedLabel("WfMissileAlert").VisibleInTree, Is.True, "Incoming missile status remains available without flare launchers.");
            Assert.That(Named("CockpitAutomaticFlares").Visible, Is.False);
            Assert.That(Named("CockpitDispenseFlares").Visible, Is.False);
            Assert.That(Named("CockpitGunneryRefresh").Visible, Is.True, "Link refresh remains available without flares.");
            combat.FlareLaunchers.Add(entries[24].NetEntity);
            combat.Ammunition = 0;
            panel.UpdateStatus(state);
            Assert.That(flarePanel.Visible, Is.True, "Installed but empty launchers must remain visible.");
            Assert.That(Named("CockpitAutomaticFlares").Visible, Is.True);
            Assert.That(Named("CockpitDispenseFlares").Visible, Is.True);
            Assert.That(Named("CockpitDispenseFlares").Disabled, Is.True);
            combat.UnlimitedSupply = true;
            panel.UpdateStatus(state);
            Assert.That(Named("CockpitDispenseFlares").Disabled, Is.False, "An automatic feed remains usable at zero stored rounds.");
            Assert.That(supply.Text, Does.Contain(Loc.GetString("wf-gunnery-flare-unlimited")));
            combat.UnlimitedSupply = false;
            combat.Ammunition = 8;
            combat.Cooldown = 3;
            panel.UpdateStatus(state);
            Assert.That(Named("CockpitDispenseFlares").Disabled, Is.True, "The embedded button retains the existing lockout.");
            var commandsBeforeTheme = commands.Count;
            try
            {
                foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(296, 450), new Vector2(400, 700) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, theme);
                    Layout(size);
                    Toggle(Named("WfWeaponTypeSelect"), true);
                    Layout(size);
                    CheckFinalPage(size);
                    Toggle(Named("WfWeaponTypeSelect"), false);
                    Layout(size);
                    CheckFinalPage(size);
                    Assert.That(Tree(panel).OfType<ScrollContainer>(), Is.Empty);
                    Assert.That(panel.SelectedWeapons, Is.EquivalentTo(expectedSelection), "Theme changes retain off-page selections.");
                    Assert.That(commands.Count, Is.EqualTo(commandsBeforeTheme), "Restyling must not send commands.");
                    var seen = new HashSet<Button>();
                    grid.SetPage(0);
                    for (var page = 0; page < grid.PageCount; page++)
                    {
                        Layout(size);
                        var visible = buttons.Where(button => button.Visible).ToArray();
                        Assert.That(visible, Is.Not.Empty);
                        Assert.That(visible.Select(button => button.Position.X).Distinct().Count(), Is.EqualTo(1),
                            "The weapon battery keeps one readable column.");
                        foreach (var button in visible)
                        {
                            Assert.That(seen.Add(button), Is.True, "Every available weapon appears on exactly one page.");
                            Assert.That(button.Height, Is.InRange(48, 56));
                            Assert.That(button.Width, Is.GreaterThanOrEqualTo(250));
                            var row = Row(button);
                            foreach (var label in new[] { row.NameLabel, row.SupplyLabel })
                            {
                                var font = label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
                                    ? styled : ui.ThemeDefaults.LabelFont);
                                Assert.That(label.PixelHeight + 1, Is.GreaterThanOrEqualTo(font.GetHeight(label.UIScale)),
                                    $"{theme}/{size}: {label.Text} must fit vertically");
                                var width = 0f;
                                foreach (var rune in label.Text!.EnumerateRunes())
                                    width += font.GetCharMetrics(rune, label.UIScale)?.Advance ?? 0;
                                Assert.That(label.PixelWidth + 1, Is.GreaterThanOrEqualTo(width), $"{theme}/{size}: {label.Text}");
                            }
                        }
                        foreach (var button in Tree(panel).OfType<Button>().Where(button => button.VisibleInTree))
                        {
                            Assert.That(button.Height, Is.GreaterThanOrEqualTo(28), $"{theme}: {button.Name}");
                            Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(panel.GlobalPosition.X));
                            Assert.That(button.GlobalPosition.Y, Is.GreaterThanOrEqualTo(panel.GlobalPosition.Y));
                            Assert.That(button.GlobalPosition.X + button.Width, Is.LessThanOrEqualTo(panel.GlobalPosition.X + panel.Width + 1));
                            Assert.That(button.GlobalPosition.Y + button.Height, Is.LessThanOrEqualTo(panel.GlobalPosition.Y + panel.Height + 1));
                        }
                        if (page < grid.PageCount - 1)
                            Press(Named("WfWeaponNext"));
                    }
                    Assert.That(seen, Is.EquivalentTo(buttons.Take(24)), "All offensive weapons remain accessible through paging.");
                    Assert.That(Named("WfWeaponNext").Disabled, Is.True);
                    Assert.That(panel.SelectedWeapons, Is.EquivalentTo(expectedSelection));
                    Assert.That(commands.Count, Is.EqualTo(commandsBeforeTheme));
                    Press(Named("WfWeaponPrevious"));
                    Layout(size);
                    Assert.That(grid.PageIndex, Is.EqualTo(grid.PageCount - 2));
                }
                var compactSize = new Vector2(296, 450);
                Layout(compactSize);
                var fullBatteryPages = grid.PageCount;
                grid.SetPage(fullBatteryPages - 1);
                Layout(compactSize);
                panel.UpdateStatus(new FireControlConsoleBoundInterfaceState(true, entries.Take(4).ToArray(), state.NavState));
                Layout(compactSize);
                Assert.That(grid.PageCount, Is.LessThan(fullBatteryPages), "Removing weapons must reduce the page count at the same panel size.");
                Assert.That(grid.PageIndex, Is.LessThan(grid.PageCount), "Removing the active final page must select a remaining page.");
                Assert.That(grid.Children.OfType<Button>().Count(), Is.EqualTo(4));
                panel.UpdateStatus(state);
                Layout(compactSize);
                Assert.That(grid.PageCount, Is.EqualTo(fullBatteryPages), "Adding weapons back must restore their pages without resizing the panel.");
                grid.SetPage(grid.PageCount - 1);
                Layout(compactSize);
                Assert.That(grid.Children.OfType<Button>().Any(button => button.Visible && button.Text == entries[23].Name), Is.True,
                    "The newly added final offensive weapon must be reachable after an in-place refresh.");
                panel.ClearSelection();
                Assert.That(panel.SelectedWeapons, Is.Empty);
                panel.UpdateState(null);
                Layout(new Vector2(296, 450));
                Assert.That(grid.PageIndex, Is.Zero);
                Assert.That(grid.PageCount, Is.EqualTo(1));
                Assert.That(grid.Children.OfType<Button>(), Is.Empty);
                Assert.That(flarePanel.Visible, Is.False);
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
