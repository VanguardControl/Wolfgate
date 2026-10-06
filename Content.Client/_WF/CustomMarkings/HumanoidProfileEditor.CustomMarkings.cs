using Content.Client._WF.CustomMarkings.UI;
using Content.Shared._WF.CustomMarkings;

namespace Content.Client.Lobby.UI;

/// <summary>The Custom markings button on the Markings tab, which opens the player's library.</summary>
public sealed partial class HumanoidProfileEditor
{
    private CustomMarkingLibraryWindow? _customMarkingWindow;

    private void InitializeCustomMarkings()
    {
        CustomMarkingsButton.OnPressed += _ => OpenCustomMarkings();
        UpdateCustomMarkings();
    }

    /// <summary>Syncs the button with the loaded profile and closes a library opened for the previous one.</summary>
    private void UpdateCustomMarkings()
    {
        CloseCustomMarkings();
        CustomMarkingsButton.Visible = _cfgManager.GetCVar(CustomMarkingCVars.Enabled);
        UpdateCustomMarkingsButton();
    }

    private void UpdateCustomMarkingsButton()
    {
        CustomMarkingsButton.Text = Loc.GetString("wf-custom-marking-creator-button", ("worn", Profile?.CustomMarkings.Count ?? 0));
    }

    private void OpenCustomMarkings()
    {
        CloseCustomMarkings();
        if (Profile == null)
            return;

        _customMarkingWindow = new CustomMarkingLibraryWindow(() => Profile);
        _customMarkingWindow.SetWorn(Profile.CustomMarkings);
        _customMarkingWindow.OnWornChanged += SetCustomMarkings;
        _customMarkingWindow.OnClose += () => _customMarkingWindow = null;
        _customMarkingWindow.OpenCentered();
    }

    private void SetCustomMarkings(List<CustomMarking> worn)
    {
        if (Profile == null)
            return;

        Profile = Profile.WithCustomMarkings(worn);
        UpdateCustomMarkingsButton();
        ReloadProfilePreview();
    }

    private void CloseCustomMarkings()
    {
        _customMarkingWindow?.Close();
        _customMarkingWindow = null;
    }
}
