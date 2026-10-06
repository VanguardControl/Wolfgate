using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.CustomMarkings;
using Content.Client._WF.CustomMarkings.UI;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
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
