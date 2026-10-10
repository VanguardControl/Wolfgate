#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.Shared._WF.CCVar;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Checks crowded dock callouts and the live port actions moved out of the approach plot.</summary>
public sealed class WFCockpitDockingTest
{
    [TestCase(0f, 0f)]
    [TestCase(300f, 180f)]
    [TestCase(150f, 90f)]
    public void CrowdedPortCalloutsStayInsideThePlotWithoutOverlapping(float x, float y)
    {
        var viewport = new Vector2(300, 180);
        var occupied = new List<UIBox2>();
        for (var port = 0; port < 12; port++)
        {
            var marker = WFDockMarkerLayout.Place(new Vector2(x, y), new Vector2(24, 20), viewport, occupied);
            Assert.That(marker, Is.Not.Null);
            var bounds = marker!.Value;
            Assert.That(UIBox2.FromDimensions(Vector2.Zero, viewport).Encloses(bounds), Is.True);
            Assert.That(occupied.Any(other => other.Intersects(bounds)), Is.False,
                "Adjacent and coincident airlocks must not cover each other's numbers.");
            occupied.Add(bounds);
        }
    }

    [Test]
    public async Task PortActionsFitRetainBindingsAndReturnToTheNormalConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var entities = pair.Client.ResolveDependency<IEntityManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
            var owner = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var ownGrid = entities.GetNetEntity(owner);
            var own = Port(new NetEntity(710), ownGrid);
            var other = Port(new NetEntity(711), ownGrid);
            var connected = Port(new NetEntity(712), ownGrid);
            connected.GridDockedWith = ownGrid;
            using var plot = new ShuttleDockControl
            {
                GridEntity = owner,
                DockState = new DockingInterfaceState(new Dictionary<NetEntity, List<DockingPortState>>
                {
                    [ownGrid] = new() { own },
                    [new NetEntity(900)] = new() { other, connected },
                }),
            };
            var lease = new WFCockpitLease();
            plot.WfCockpitInteraction(lease);
            using var actions = plot.WfCockpitDockActions(lease);
            pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.AddChild(actions);
            Assert.That(Tree(plot).OfType<Button>(), Is.Empty, "Text panels and action buttons must not cover the approach plot.");
            var buttons = Tree(actions).OfType<Button>().ToArray();
            Assert.That(buttons, Has.Length.EqualTo(3));
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                WFInstrumentTheme.Apply(actions);
                foreach (var row in actions.Children)
                    row.Visible = true;
                var size = new Vector2(280, 140);
                actions.Measure(size);
                actions.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                foreach (var button in buttons)
                {
                    var font = button.Label.FontOverride ?? (button.Label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
                        ? styled : pair.Client.ResolveDependency<IUserInterfaceManager>().ThemeDefaults.LabelFont);
                    var textWidth = button.Text!.EnumerateRunes().Sum(rune => font.GetCharMetrics(rune, button.UIScale)?.Advance ?? 0);
                    Assert.That(button.Label.PixelWidth + 1, Is.GreaterThanOrEqualTo(textWidth), $"{theme}: {button.Text} must be readable in full.");
                    Assert.That(button.GlobalPosition.X + button.Width, Is.LessThanOrEqualTo(280));
                }
                foreach (var label in Tree(actions).OfType<Label>().Where(label => label.ToolTip != null))
                    Assert.That(label.ToolTip, Does.Contain("external airlock"), "The complete port name remains available when its row is narrow.");
            }
            var requests = new List<(NetEntity From, NetEntity To)>();
            var undocks = new List<NetEntity>();
            plot.DockRequest += (from, to) => requests.Add((from, to));
            plot.UndockRequest += undocks.Add;
            Press(buttons.Single(button => button.Text == Loc.GetString("shuttle-console-view")));
            Assert.That(plot.ViewedDock, Is.EqualTo(own.Entity));
            var dock = buttons.Single(button => button.Text == Loc.GetString("shuttle-console-dock"));
            Assert.That(dock.Disabled, Is.True);
            Press(dock);
            Assert.That(requests, Is.Empty, "A dock that has not passed the plot's alignment check cannot send a request.");
            // This fixture checks leased bindings; a rendered plot supplies spatial docking eligibility.
            dock.Disabled = false;
            Press(dock);
            Press(buttons.Single(button => button.Text == Loc.GetString("shuttle-console-undock")));
            Assert.That(requests, Is.EqualTo(new[] { (own.Entity, other.Entity) }));
            Assert.That(undocks, Is.EqualTo(new[] { connected.Entity }));
            plot.Offset = new Vector2(3, 4);
            plot.BuildDocks(owner);
            Assert.That(plot.ViewedDock, Is.EqualTo(own.Entity));
            Assert.That(plot.Offset, Is.EqualTo(new Vector2(3, 4)), "State refresh must preserve an ongoing approach pan.");
            Assert.That(Tree(actions).OfType<Button>().Count(), Is.EqualTo(3));
            lease.Restore();
            Assert.That(plot.WfCockpitControls, Is.False);
            Assert.That(actions.ChildCount, Is.Zero);
            Assert.That(Tree(plot).OfType<Button>().Count(), Is.EqualTo(3), "Exiting cockpit restores the normal docking UI.");
            settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
            entities.DeleteEntity(owner);
        });
        await pair.CleanReturnAsync();
    }

    private static DockingPortState Port(NetEntity entity, NetEntity grid) => new()
    {
        Entity = entity,
        Coordinates = new NetCoordinates(grid, Vector2.Zero),
        Name = "external airlock with a deliberately long operational name",
    };

    private static void Press(BaseButton button) => WFButtonTestInput.Click(button);

    private static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Tree(child))
            yield return nested;
    }
}
