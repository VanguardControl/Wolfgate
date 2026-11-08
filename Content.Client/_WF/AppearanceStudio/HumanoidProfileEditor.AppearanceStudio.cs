using Content.Client._WF.AppearanceStudio;
using Content.Client._WF.AppearanceStudio.UI;
using Content.Shared._WF.AppearanceStudio;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

/// <summary>The Studio button under the creator's preview, which opens the Appearance Studio on this character.</summary>
public sealed partial class HumanoidProfileEditor
{
    private Button? _studioButton;
    private StudioWindow? _studioWindow;

    private void InitializeAppearanceStudio()
    {
        _studioButton = new Button
        {
            Text = Loc.GetString("wf-studio-creator-button"),
            ToolTip = Loc.GetString("wf-studio-creator-tooltip"),
            Margin = new Thickness(6, 0, 0, 0),
        };
        _studioButton.OnPressed += _ => OpenAppearanceStudio();
        SpriteRotateLeft.Parent?.AddChild(_studioButton);
        UpdateAppearanceStudio();
    }

    /// <summary>Syncs the button with the server setting and closes a studio opened for the previous profile.</summary>
    private void UpdateAppearanceStudio()
    {
        CloseAppearanceStudio();
        if (_studioButton != null)
            _studioButton.Visible = _cfgManager.GetCVar(StudioCVars.Enabled);
    }

    /// <summary>Points the studio's per-facing offsets at the way the preview faces.</summary>
    private void SetAppearanceStudioFacing()
    {
        if (SpriteView.OverrideDirection is { } dir)
            _entManager.System<StudioSpriteSystem>().SetPreviewFacing(PreviewDummy, dir);
    }

    /// <summary>
    /// Closes the studio without letting its last pending edit land: it belongs to the profile it was opened for,
    /// which may already have been replaced, reset or the editor torn down.
    /// </summary>
    private void CloseAppearanceStudio()
    {
        if (_studioWindow == null)
            return;

        var window = _studioWindow;
        _studioWindow = null;
        window.Changed -= SetStudioTweaks;
        window.Close();
    }

    private void OpenAppearanceStudio()
    {
        CloseAppearanceStudio();
        if (Profile == null)
            return;

        _studioWindow = new StudioWindow(() => Profile);
        _studioWindow.Changed += SetStudioTweaks;
        var window = _studioWindow;
        window.OnClose += () =>
        {
            if (_studioWindow == window)
                _studioWindow = null;
        };
        _studioWindow.OpenCentered();
    }

    private void SetStudioTweaks(List<StudioTweak> tweaks)
    {
        if (Profile == null)
            return;

        Profile = Profile.WithStudioTweaks(tweaks);
        ReloadProfilePreview();
    }
}
