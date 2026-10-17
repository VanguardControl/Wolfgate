using Content.Client.Administration.Managers;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.GodPanel;
using Content.Shared.Administration;
using Robust.Client.Console;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.GodPanel.UI;

/// <summary>
/// The panel's top bar: the round at a glance, and the buttons that act on the whole round.
/// </summary>
public sealed partial class WFGodPanelTopBar : BoxContainer
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IClientConsoleHost _console = default!;
    [Dependency] private IEntityManager _entities = default!;

    private readonly Label _round = new();
    private readonly Label _war = new();
    private readonly Label _reavers = new();
    private readonly Label _encounters = new();
    private readonly Label _sound = new();
    private readonly Label _gms = new();

    private readonly Button _internetSound;
    private readonly Button _pauseEncounters;
    private readonly ConfirmButton _warLevel;
    private readonly Button _pauseReavers;
    private readonly List<(Button Button, AdminFlags Flag)> _gated = new();

    private WFGodPanelState? _state;

    public WFGodPanelTopBar()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        Margin = new Thickness(6, 2);

        var facts = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 18 };
        facts.AddChild(_round);
        facts.AddChild(_war);
        facts.AddChild(_reavers);
        facts.AddChild(_encounters);
        facts.AddChild(_sound);
        facts.AddChild(new Control { HorizontalExpand = true });
        facts.AddChild(_gms);
        AddChild(facts);

        var buttons = new WrapContainer { HorizontalExpand = true };
        AddChild(buttons);

        _internetSound = Gated(buttons, "wf-godpanel-top-internet-sound", AdminFlags.Fun,
            () => _console.ExecuteCommand("playinternetsound"));
        _pauseEncounters = Gated(buttons, "wf-godpanel-top-pause-encounters", AdminFlags.Admin, () =>
        {
            if (_state is { } state)
                Act(state.Round.EncountersPaused ? WFGodAction.ResumeEncounters : WFGodAction.PauseEncounters);
        });

        _warLevel = new ConfirmButton
        {
            ConfirmationText = Loc.GetString("wf-godpanel-top-confirm"),
            ResetTime = TimeSpan.FromSeconds(3),
            StyleClasses = { "ButtonSquare" },
            Margin = new Thickness(0, 0, 4, 0),
        };
        _warLevel.OnPressed += _ =>
        {
            if (_state is { } state)
                Act(state.Round.WarHot ? WFGodAction.WarLevelCold : WFGodAction.WarLevelHot);
        };
        buttons.AddChild(_warLevel);

        _pauseReavers = Gated(buttons, "wf-godpanel-top-pause-reavers", AdminFlags.Admin, () =>
        {
            if (_state is { } state)
                Act(state.Round.ReaversPaused ? WFGodAction.ReaverResume : WFGodAction.ReaverPause);
        });

        Gated(buttons, "wf-godpanel-top-crew", AdminFlags.Spawn, () => _console.ExecuteCommand("wf_crew"));
        Gated(buttons, "wf-godpanel-top-encounters", AdminFlags.Admin, () => _console.ExecuteCommand("wf_encounter"));
        Gated(buttons, "wf-godpanel-top-vessel", AdminFlags.Spawn, () => _console.ExecuteCommand("spawnvessel"));
        Gated(buttons, "wf-godpanel-top-ert", AdminFlags.Spawn, () => _console.ExecuteCommand("ertbuilder"));
        Gated(buttons, "wf-godpanel-top-planets", AdminFlags.Admin, () => _console.ExecuteCommand("planetcontrol"));
        Gated(buttons, "wf-godpanel-top-power", AdminFlags.Admin, () => _console.ExecuteCommand("gridpower"));

        Update(null);
    }

    private Button Gated(WrapContainer parent, string loc, AdminFlags flag, Action action)
    {
        var button = WFGodPanelText.SmallButton(Loc.GetString(loc));
        button.Margin = new Thickness(0, 0, 4, 0);
        button.OnPressed += _ => action();
        parent.AddChild(button);
        _gated.Add((button, flag));
        return button;
    }

    private void Act(WFGodAction action)
    {
        _entities.System<WFGodPanelSystem>().Act(action, null);
    }

    /// <summary>Shows a new state; null before the first one arrives.</summary>
    public void Update(WFGodPanelState? state)
    {
        _state = state;

        foreach (var (button, flag) in _gated)
        {
            button.Disabled = !_admin.HasFlag(flag);
        }

        if (!_warLevel.IsConfirming)
            _warLevel.Disabled = !_admin.HasFlag(AdminFlags.Admin);

        if (state == null)
        {
            _round.Text = Loc.GetString("wf-godpanel-top-waiting");
            if (!_warLevel.IsConfirming)
                _warLevel.Text = Loc.GetString("wf-godpanel-top-war-level");
            _war.Text = _reavers.Text = _encounters.Text = _sound.Text = _gms.Text = string.Empty;
            return;
        }

        var round = state.Round;
        _round.Text = Loc.GetString("wf-godpanel-top-round", ("time", WFGodPanelText.Clock(round.RoundTime)));

        _war.Text = Loc.GetString("wf-godpanel-top-war",
            ("level", Loc.GetString(round.WarHot ? "wf-godpanel-war-hot" : "wf-godpanel-war-cold")));
        _war.FontColorOverride = round.WarHot ? WFGodPanelText.Red : WFGodPanelText.Blue;

        _reavers.Text = Loc.GetString("wf-godpanel-top-reavers",
            ("tier", round.ReaverTier), ("name", round.ReaverTierName), ("cells", round.ReaverCells));
        _encounters.Text = Loc.GetString(round.EncountersPaused ? "wf-godpanel-top-encounters-paused" : "wf-godpanel-top-encounters-running",
            ("count", round.EncountersRunning));
        _sound.Text = round.SoundTitle is { } title
            ? Loc.GetString("wf-godpanel-top-sound", ("title", title))
            : Loc.GetString("wf-godpanel-top-sound-none");
        _gms.Text = Loc.GetString("wf-godpanel-top-gms", ("count", round.Gms));

        _pauseEncounters.Text = Loc.GetString(round.EncountersPaused ? "wf-godpanel-top-resume-encounters" : "wf-godpanel-top-pause-encounters");
        _pauseReavers.Text = Loc.GetString(round.ReaversPaused ? "wf-godpanel-top-resume-reavers" : "wf-godpanel-top-pause-reavers");

        if (!_warLevel.IsConfirming)
            _warLevel.Text = Loc.GetString(round.WarHot ? "wf-godpanel-top-war-to-cold" : "wf-godpanel-top-war-to-hot");
    }
}
