using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.CustomMarkings;
using Content.Client._WF.CustomMarkings.UI;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.IntegrationTests.Tests._WF.CustomMarkings;

/// <summary>The library and editor windows open, lay out and fill in from the server without errors.</summary>
[TestFixture]
[TestOf(typeof(CustomMarkingLibraryWindow))]
[TestOf(typeof(CustomMarkingEditorWindow))]
public sealed class CustomMarkingWindowsTest
{
    [Test]
    public async Task WindowsOpenTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var system = client.System<CustomMarkingSystem>();
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");

        var art = new CustomMarkingArt();
        art.SetPixel(CustomMarkingArt.South, 16, 12, new Rgba32(10, 200, 10, 255));

        CustomMarkingLibraryWindow library = null;
        CustomMarkingEditorWindow editor = null;
        await client.WaitPost(() =>
        {
            library = new CustomMarkingLibraryWindow(() => profile);
            library.OpenCentered();
            editor = new CustomMarkingEditorWindow(null, art, "Leaf", profile);
            editor.OpenCentered();
            system.Save(0, "Leaf", CustomMarkingPlacement.Hair, art);
        });

        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            Assert.That(system.Library, Has.Count.EqualTo(1), "the library reached the client");
            Assert.That(library.IsOpen, Is.True);
            Assert.That(editor.IsOpen, Is.True);

            var labels = Descendants(library).OfType<Label>().Select(label => label.Text).ToList();
            Assert.That(labels, Does.Contain("Leaf"), "the library lists the saved marking");
            Assert.That(labels, Does.Contain(Loc.GetString("wf-custom-marking-placement-hair")));
            Assert.That(Descendants(editor).OfType<CustomMarkingCanvas>().Count(), Is.EqualTo(1 + CustomMarkingRules.Facings),
                "the editor shows the drawing and each facing");

            var viewport = new Vector2(1024, 768);
            foreach (var window in new Control[] { library, editor })
            {
                window.Measure(viewport);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, viewport));
                foreach (var button in Descendants(window).OfType<BaseButton>())
                {
                    var what = button is Button text ? text.Text : button.ToolTip;
                    var bottomRight = button.GlobalPosition - window.GlobalPosition + button.Size;
                    Assert.That(bottomRight.X, Is.LessThanOrEqualTo(viewport.X), $"{what} fits horizontally");
                    Assert.That(bottomRight.Y, Is.LessThanOrEqualTo(viewport.Y), $"{what} fits vertically");
                }
            }

            // Tools and actions are icons, so each must say what it does.
            var icons = Descendants(editor).OfType<CustomMarkingIconButton>().ToList();
            Assert.That(icons, Has.Count.GreaterThanOrEqualTo(13), "the tools, undo and redo and the facing operations are icon buttons");
            Assert.That(icons.All(icon => !string.IsNullOrEmpty(icon.ToolTip)), Is.True, "every icon button has a tooltip");
            Assert.That(icons.Single(icon => icon.ToolTip == Loc.GetString("wf-custom-marking-editor-symmetry")).ToggleMode,
                Is.True, "mirror drawing is switched on and off");

            // A facing tile is picked by clicking anywhere on it, so its preview must not take the click itself.
            var canvases = Descendants(editor).OfType<CustomMarkingCanvas>().ToList();
            Assert.That(canvases.Count(canvas => canvas.MouseFilter == Control.MouseFilterMode.Ignore), Is.EqualTo(CustomMarkingRules.Facings));
            Assert.That(canvases.Count(canvas => canvas.MouseFilter == Control.MouseFilterMode.Stop), Is.EqualTo(1), "only the canvas is drawn on");
            Assert.That(Descendants(library).OfType<CustomMarkingIconButton>().All(icon => !string.IsNullOrEmpty(icon.ToolTip)), Is.True);
            Assert.That(Descendants(library).OfType<CustomMarkingIconButton>().Count(), Is.EqualTo(3), "a library row offers edit, export and delete");

            // Wearing from outside, as the creator does when a profile loads.
            library.SetWorn(new List<CustomMarking> { new(system.Library[0].Hash, CustomMarkingPlacement.Hair) });
            Assert.That(Descendants(library).OfType<Button>().Any(button => button.Text == Loc.GetString("wf-custom-marking-library-take-off")),
                Is.True, "a worn marking offers to come off");

            editor.Close();
            library.Close();
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    /// <summary>The creator's tiles list the library, show what is worn and keep a tile for a worn marking it doesn't hold.</summary>
    [Test]
    public async Task QuickListTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var system = client.System<CustomMarkingSystem>();

        var art = new CustomMarkingArt();
        art.SetPixel(CustomMarkingArt.South, 16, 12, new Rgba32(10, 200, 10, 255));

        CustomMarkingQuickList list = null;
        await client.WaitPost(() =>
        {
            list = new CustomMarkingQuickList();
            client.ResolveDependency<IUserInterfaceManager>().RootControl.AddChild(list);
            system.Save(0, "Leaf", CustomMarkingPlacement.Hair, art);
        });

        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            Assert.That(system.Library, Has.Count.EqualTo(1));
            var entry = system.Library[0];

            var tiles = Descendants(list).OfType<Content.Client._WF.Humanoid.WolfgateMarkingTile>().ToList();
            Assert.That(tiles, Has.Count.EqualTo(1), "a tile for the saved marking");
            Assert.That(tiles[0].Pressed, Is.False);
            Assert.That(tiles[0].ToolTip, Does.Contain("Leaf"));

            // Worn: its tile is pressed, and a worn marking the library doesn't hold gets a tile too.
            var stray = new CustomMarking(new string('d', CustomMarkingRules.HashLength), CustomMarkingPlacement.Skin);
            list.SetWorn(new List<CustomMarking> { new(entry.Hash, entry.Placement), stray });
            tiles = Descendants(list).OfType<Content.Client._WF.Humanoid.WolfgateMarkingTile>().ToList();
            Assert.That(tiles, Has.Count.EqualTo(2));
            Assert.That(tiles.All(tile => tile.Pressed), Is.True);

            // With as many on as allowed, the rest can't be pressed.
            var max = client.CfgMan.GetCVar(CustomMarkingCVars.MaxWorn);
            var full = Enumerable.Range(0, max)
                .Select(i => new CustomMarking(i.ToString("x64"), CustomMarkingPlacement.Skin))
                .ToList();
            list.SetWorn(full);
            tiles = Descendants(list).OfType<Content.Client._WF.Humanoid.WolfgateMarkingTile>().ToList();
            Assert.That(tiles, Has.Count.EqualTo(max + 1));
            Assert.That(tiles.Single(tile => !tile.Pressed).Disabled, Is.True);

            list.Orphan();
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }
}
