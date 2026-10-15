using System.Linq;
using Content.Client._WF.Loadouts;
using Content.Client.Lobby;
using Content.Server.Preferences.Managers;
using Content.Shared.Preferences;
using System.Numerics;
using Content.Client._WF.RoundSetup;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Localization;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

public sealed partial class RoundSetupUiTest
{
    /// <summary>Live crew changes cannot destroy an active text field or an open dropdown.</summary>
    [Test]
    public async Task BackgroundUpdatesPreserveTypingAndDropdowns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
        var id = state.OwnLobby!.Value;
        WFRoundSetupWindow window = null!;
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        await pair.Client.WaitPost(() =>
        {
            window = new WFRoundSetupWindow();
            window.FitTo(new Vector2(1120, 780));
            window.OpenCentered();
        });
        await pair.RunTicksSync(3);
        Button admission = null!;
        await pair.Client.WaitPost(() => admission = Buttons(Named<Control>(window, "RoundSetupSections"), "admission").Single());
        await Click(pair, admission);
        LineEdit title = null!;
        await pair.Client.WaitPost(() =>
        {
            title = Descendants(window).OfType<LineEdit>().Single(edit => edit.PlaceHolder == Loc.GetString("wf-round-setup-crew-title-placeholder"));
            title.GrabKeyboardFocus();
        });
        await Request(pair, new(WFRoundSetupAction.Configure) { LobbyId = id, Title = "Live title", Access = WFLobbyAccess.Public });
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(title.IsInsideTree, Is.True);
            Assert.That(ui.KeyboardFocused, Is.SameAs(title));
            title.ReleaseKeyboardFocus();
        });
        await pair.RunTicksSync(3);
        WFRoundSetupOptionButton selector = null!;
        await pair.Client.WaitPost(() => selector = Descendants(window).OfType<WFRoundSetupOptionButton>().Single(button => !button.Disabled));
        await Click(pair, selector);
        await Request(pair, new(WFRoundSetupAction.Configure) { LobbyId = id, Title = "Another title", Access = WFLobbyAccess.Public });
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(selector.IsInsideTree, Is.True);
            Assert.That(selector.OptionsScroll.VisibleInTree, Is.True);
            window.Close();
        });
        await Request(pair, new(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.CleanReturnAsync();
    }

    /// <summary>The chosen crew job opens its normal picker and saves the selected kit to that character slot.</summary>
    [Test]
    public async Task CrewLoadoutPickerSavesTheSelectedJobsEquipment()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var prefs = pair.Server.ResolveDependency<IServerPreferencesManager>();
        var slot = prefs.GetPreferences(pair.Player!.UserId).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) prefs.GetPreferences(pair.Player.UserId).SelectedCharacter!;
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientPreferencesManager>().UpdateCharacter(
            HumanoidCharacterProfile.DefaultWithSpecies("Human").WithName("Loadout Tester"), slot));
        await pair.RunTicksSync(pair.SecondsToTicks(1));
        var state = await Request(pair, new(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
        var id = state.OwnLobby!.Value;
        await Request(pair, new(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = "Contractor" });
        WFRoundSetupWindow window = null!;
        Button button = null!;
        WolfgateLoadoutWindow editor = null!;
        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
        await pair.Client.WaitPost(() =>
        {
            window = new WFRoundSetupWindow();
            window.FitTo(new Vector2(1120, 780));
            window.OpenCentered();
        });
        await pair.RunTicksSync(3);
        await pair.Client.WaitAssertion(() =>
        {
            button = Buttons(window, "job-loadout").Single();
            Assert.That(Labels(window).Any(text => text.Contains("Loadout:") && text.Contains("Loadout Tester")), Is.True);
        });
        await Click(pair, button);
        await pair.Client.WaitPost(() =>
        {
            editor = Descendants(ui.WindowRoot).OfType<WolfgateLoadoutWindow>().Single();
            editor.OpenGroup(editor.Catalog.Groups.Single(group => group.Id == "MercenaryJumpsuit"));
        });
        await pair.RunTicksSync(4);
        LoadoutTile tile = null!;
        await pair.Client.WaitAssertion(() => tile = Descendants(editor).OfType<LoadoutTile>()
            .Single(choice => choice.VisibleInTree && choice.Entry?.Id == "MercenaryClothingUniformJumpskirtMercenary"));
        await Click(pair, tile);
        await pair.Client.WaitPost(() => editor.Close());
        await pair.RunTicksSync(pair.SecondsToTicks(1));
        await pair.Server.WaitAssertion(() =>
        {
            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(pair.Player.UserId).Characters[slot];
            Assert.That(profile.Loadouts["JobContractor"].SelectedLoadouts["MercenaryJumpsuit"].Single().Prototype.Id,
                Is.EqualTo("MercenaryClothingUniformJumpskirtMercenary"));
        });
        await pair.Client.WaitAssertion(() => { Assert.That(window.VisibleInTree, Is.True); window.Close(); });
        await Request(pair, new(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.Server.WaitPost(() => prefs.SetProfile(pair.Player.UserId, slot, original).Wait());
        await pair.CleanReturnAsync();
    }
}
