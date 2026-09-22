#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using Content.Client._WF.Wolfmed.Autodoc;
using Content.IntegrationTests.Fixtures;
using Content.Shared._WF.Wolfmed.Autodoc;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// AUTODOC5: the pod window's bottom row has to be on screen at the size the window opens at.
/// </summary>
/// <remarks>
/// The test this replaces asked whether anything hung below the window, which a BoxContainer can never let
/// happen: when it runs out of room it clamps its last children to nothing rather than pushing them past
/// the edge. The controls row was therefore squashed to zero height while every assertion passed. This
/// measures the row itself, at UI scale 1 and at 1.25, where the fonts round up and the column is tighter.
/// </remarks>
[TestFixture]
[TestOf(typeof(AutodocWindow))]
public sealed class WolfmedAutodocWindowLayoutTest : GameTest
{
    [Test]
    [TestCase(1f)]
    [TestCase(1.25f)]
    public async Task ControlsFitTheWindowTest(float uiScale)
    {
        var client = Pair.Client;
        var config = client.ResolveDependency<IConfigurationManager>();
        var original = config.GetCVar(CVars.DisplayUIScale);
        AutodocWindow window = default!;

        await client.WaitPost(() =>
        {
            config.SetCVar(CVars.DisplayUIScale, uiScale);
            window = new AutodocWindow();
            window.Update(new AutodocBuiState());
            window.OpenCentered();
        });
        await Pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var contents = window.Contents;
            var bottom = contents.GlobalPosition.Y + contents.Size.Y;
            var report = Tree(window);

            var controls = window.ControlsRow;
            Assert.Multiple(() =>
            {
                Assert.That(controls.Size.Y, Is.GreaterThanOrEqualTo(controls.DesiredSize.Y - 0.5f),
                    $"the controls row was squeezed from {controls.DesiredSize.Y} to {controls.Size.Y}.\n{report}");

                foreach (var control in controls.Children)
                {
                    if (!control.Visible)
                        continue;

                    Assert.That(control.Size.Y, Is.GreaterThanOrEqualTo(control.DesiredSize.Y - 0.5f),
                        $"{Name(control)} was squeezed to {control.Size.Y}.\n{report}");
                    Assert.That(control.GlobalPosition.Y + control.Size.Y, Is.LessThanOrEqualTo(bottom + 0.5f),
                        $"{Name(control)} ends below the window's contents.\n{report}");
                }
            });

            // Nothing else may hang out of the window either, at any depth.
            Assert.Multiple(() => Walk(contents, child =>
            {
                if (child.Visible)
                    Assert.That(child.GlobalPosition.Y + child.Size.Y, Is.LessThanOrEqualTo(bottom + 0.5f),
                        $"{Name(child)} ends below the window's contents.\n{report}");
            }));
        });

        await client.WaitPost(() =>
        {
            window.Close();
            config.SetCVar(CVars.DisplayUIScale, original);
        });
        await Pair.RunTicksSync(5);
    }

    private static void Walk(Control control, Action<Control> visit)
    {
        visit(control);
        foreach (var child in control.Children)
            Walk(child, visit);
    }

    private static string Name(Control control) =>
        control.Name is { } name ? $"{control.GetType().Name} '{name}'" : control.GetType().Name;

    /// <summary>The whole tree with its measurements, printed only when an assertion has already failed.</summary>
    private static string Tree(Control root)
    {
        var report = new StringBuilder();

        void Dump(Control control, int level)
        {
            if (level > 6)
                return;

            report.AppendLine($"{new string(' ', level * 2)}{Name(control)} scale={control.UIScale} " +
                              $"desired={control.DesiredSize} size={control.Size} pos={control.Position}");
            foreach (var child in control.Children)
                Dump(child, level + 1);
        }

        Dump(root, 0);
        return report.ToString();
    }
}
