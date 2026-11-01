using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client._Mono.Company;
using Content.Client._WF.Traits.UI;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Traits;

/// <summary>
/// The lobby character editor shows the card-based trait tab and its checkboxes update the profile.
/// </summary>
[TestFixture]
public sealed class TraitsUiTest
{
    [Test]
    public async Task TraitsTabRendersAndToggles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var editor = new HumanoidProfileEditor(
                client.ResolveDependency<IClientPreferencesManager>(),
                client.ResolveDependency<IConfigurationManager>(),
                client.ResolveDependency<IEntityManager>(),
                client.ResolveDependency<IFileDialogManager>(),
                client.ResolveDependency<ILogManager>(),
                client.ResolveDependency<IPlayerManager>(),
                client.ResolveDependency<IPrototypeManager>(),
                client.ResolveDependency<IResourceCache>(),
                client.ResolveDependency<JobRequirementsManager>(),
                client.ResolveDependency<MarkingManager>(),
                client.ResolveDependency<CompanyManager>());

            editor.SetProfile(HumanoidCharacterProfile.DefaultWithSpecies(), null);

            var traitsTab = editor.FindControl<TraitsTab>("TraitsTab");
            Assert.That(traitsTab, Is.Not.Null, "The card-based trait tab is missing from the editor.");
            Assert.That(traitsTab!.FindControl<LineEdit>("SearchBar"), Is.Not.Null);
            var oldList = editor.FindControl<BoxContainer>("TraitsList");
            Assert.That(oldList, Is.Not.Null);
            Assert.That(oldList!.Parent?.Visible, Is.False,
                "The old flat trait list should stay hidden behind the new editor.");

            var entries = Descend(traitsTab).OfType<TraitEntry>().ToList();
            Assert.That(entries, Is.Not.Empty, "The new trait tab should list traits.");

            var pacifist = entries.First(entry => entry.Trait.ID == "Pacifist");
            var checkbox = pacifist.FindControl<CheckBox>("TraitCheckbox");
            Assert.That(checkbox, Is.Not.Null);

            var trait = new ProtoId<TraitPrototype>("Pacifist");
            Toggle(checkbox, true);
            Assert.That(editor.Profile!.TraitPreferences, Does.Contain(trait),
                "Selecting a trait in the new tab should update the profile.");

            Toggle(checkbox, false);
            Assert.That(editor.Profile!.TraitPreferences, Does.Not.Contain(trait),
                "Deselecting a trait in the new tab should update the profile.");
        });

        await pair.CleanReturnAsync();
    }

    private static void Toggle(CheckBox checkBox, bool pressed)
    {
        checkBox.Pressed = pressed;
        var toggled = (Action<BaseButton.ButtonToggledEventArgs>?) typeof(BaseButton)
            .GetField(nameof(BaseButton.OnToggled), BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(checkBox);
        toggled!.Invoke(new BaseButton.ButtonToggledEventArgs(pressed, checkBox, ClickArgs()));
    }

    private static GUIBoundKeyEventArgs ClickArgs()
    {
        return new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, default, true, default, default);
    }

    private static IEnumerable<Control> Descend(Control control)
    {
        yield return control;
        foreach (var child in control.Children)
        {
            foreach (var descendant in Descend(child))
                yield return descendant;
        }
    }
}
