using Content.Client._WG.Mentors;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._WG.MentorHelp;
using Content.Shared._WG.Mentors;
using Content.Shared._WG.MentorCritters;
using Content.Shared._WG.MentorObserve;
using Content.Client._WG.MentorObserve;
using System.Linq;
using Robust.Shared.Prototypes;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Client._WG.MentorHelp;

/// <summary>
/// Opens the Mentor Help window from the menu bar, the lobby and Shift+F1, keeps it in step with the server, and
/// marks the menu button when a reply or a new question arrives while the window is closed.
/// </summary>
[UsedImplicitly]
public sealed class MentorHelpUIController : UIController,
    IOnSystemChanged<WGClientMentorHelpSystem>,
    IOnSystemChanged<WGClientMentorSystem>,
    IOnSystemChanged<WGClientMentorObserveSystem>
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private static readonly SoundSpecifier AlertSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    [UISystemDependency] private SharedAudioSystem? _audio = default!;

    private WGClientMentorHelpSystem? _help;
    private WGClientMentorSystem? _mentors;
    private WGClientMentorObserveSystem? _observe;
    private MentorHelpWindow? _window;
    private string? _lastNotice;

    private MenuButton? GameButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.MentorHelpButton;

    public void OnSystemLoaded(WGClientMentorHelpSystem system)
    {
        _help = system;
        system.Changed += Refresh;
        system.OwnReply += OnOwnReply;
        system.QueueAlert += OnQueueAlert;
        system.Notice += OnNotice;

        CommandBinds.Builder
            .Bind(WGMentorHelpKeys.WGOpenMentorHelp, InputCmdHandler.FromDelegate(_ => Toggle()))
            .Register<MentorHelpUIController>();
    }

    public void OnSystemUnloaded(WGClientMentorHelpSystem system)
    {
        CommandBinds.Unregister<MentorHelpUIController>();
        system.Changed -= Refresh;
        system.OwnReply -= OnOwnReply;
        system.QueueAlert -= OnQueueAlert;
        system.Notice -= OnNotice;
        _help = null;
        _window?.Close();
    }

    public void OnSystemLoaded(WGClientMentorSystem system)
    {
        _mentors = system;
        system.StatusChanged += Refresh;
        system.CountChanged += Refresh;
    }

    public void OnSystemUnloaded(WGClientMentorSystem system)
    {
        system.StatusChanged -= Refresh;
        system.CountChanged -= Refresh;
        _mentors = null;
    }

    public void OnSystemLoaded(WGClientMentorObserveSystem system)
    {
        _observe = system;
        system.Changed += OnVisitsChanged;
    }

    public void OnSystemUnloaded(WGClientMentorObserveSystem system)
    {
        system.Changed -= OnVisitsChanged;
        _observe = null;
    }

    /// <summary>A prompt opens the window, so the asker sees it; anything else just refreshes it.</summary>
    private void OnVisitsChanged()
    {
        if (_observe?.Prompt != null && _window == null)
            Open();

        Refresh();
    }

    /// <summary>The critters this player may join as: Mentors and Seniors on duty, and staff, while critters are on.</summary>
    private List<(string Id, string Name)> Critters()
    {
        var list = new List<(string Id, string Name)>();
        var allowed = _cfg.GetCVar(MentorObserveCVars.Critters)
                      && ((_help?.Staff ?? false) || _mentors is { IsMentor: true, Tier: not MentorTier.Trainee });
        if (!allowed)
            return list;

        foreach (var critter in _prototypes.EnumeratePrototypes<WGMentorCritterPrototype>().OrderBy(c => c.Priority))
        {
            list.Add((critter.ID, Loc.GetString("wg-mentor-critter-name", ("animal", Loc.GetString(critter.Animal)))));
        }

        return list;
    }

    /// <summary>Hooks up the menu bar button.</summary>
    public void LoadButton()
    {
        if (GameButton is { } button)
            button.OnPressed += ButtonPressed;
    }

    public void UnloadButton()
    {
        if (GameButton is { } button)
            button.OnPressed -= ButtonPressed;
    }

    /// <summary>Opens the window, or closes it if it is open.</summary>
    public void Toggle()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        Open();
    }

    public void Open()
    {
        if (_help == null)
            return;

        if (_window == null)
        {
            _window = new MentorHelpWindow();
            _window.OnClose += () => _window = null;
            _window.OpenPressed += (text, topic, secret) => _help?.Open(text, topic, secret);
            _window.PostPressed += (ticket, text) => _help?.Post(ticket, text);
            _window.ActionPressed += (ticket, action, note, target) => _help?.Act(ticket, action, note, target);
            _window.PlayerActionPressed += (action, up, comment) => _help?.PlayerAct(action, up, comment);
            _window.AskStaffPressed += () => UIManager.GetUIController<AHelpUIController>().Open();
            _window.DutyToggled += onDuty => _mentors?.RequestDuty(onDuty);
            _window.StartOffDutyToggled += off => _cfg.SetCVar(MentorCVars.StartOffDuty, off);
            _window.CrownToggled += show => _cfg.SetCVar(MentorCVars.ShowCrown, show);
            _window.SoundToggled += on => _cfg.SetCVar(MentorHelpCVars.Sound, on);
            _window.TypingChanged += (ticket, typing) => _help?.SetTyping(ticket, typing);
            _window.CannedSaved += answer => _help?.SaveCanned(answer);
            _window.CannedDeleted += id => _help?.DeleteCanned(id);
            _window.VisitRequested += (ticket, kind, critter) => _observe?.Request(ticket, kind, critter);
            _window.VisitAnswered += allow => _observe?.Answer(allow);
            _window.VisitsEnded += takeOutOnly => _observe?.EndVisits(takeOutOnly);
            _window.ReturnPressed += () => _observe?.Return();
            if (_lastNotice != null)
                _window.ShowNotice(_lastNotice);
        }

        _window.OpenCentered();
        GameButton?.StyleClasses.Remove(MenuButton.StyleClassRedTopButton);
        _help.Refresh();
        Refresh();
    }

    /// <summary>Opens the window with a how-to question the player had typed into the ahelp.</summary>
    public void AskFromAHelp(string text)
    {
        Open();
        _window?.SetQuestion(text);
        _window?.ShowNotice(Loc.GetString("wg-mhelp-ahelp-moved"));
    }

    private void ButtonPressed(BaseButton.ButtonEventArgs args)
    {
        Toggle();
    }

    private void Refresh()
    {
        if (_window == null || _help == null)
            return;

        _window.SetState(new MentorHelpWindowState
        {
            Own = _help.Own,
            Responder = _help.Responder,
            Staff = _help.Staff,
            IsMentor = _mentors?.IsMentor ?? false,
            OnDuty = _mentors?.OnDuty ?? false,
            StartOffDuty = _cfg.GetCVar(MentorCVars.StartOffDuty),
            ShowCrown = _cfg.GetCVar(MentorCVars.ShowCrown),
            MentorsOnDuty = _mentors?.OnDutyCount ?? 0,
            Tickets = _help.Tickets.Values,
            Canned = _help.Canned,
            CanEditCanned = _help.CanEditCanned,
            Sound = _cfg.GetCVar(MentorHelpCVars.Sound),
            Tier = _mentors?.Tier ?? MentorTier.Trainee,
            VisitPrompt = _observe?.Prompt,
            Visits = _observe?.State ?? new WGMentorVisitStateEvent(),
            Critters = Critters(),
        });
    }

    /// <summary>A reply in the player's own ticket: always a sound, and the button marked if the window is closed.</summary>
    private void OnOwnReply()
    {
        PlaySound();
        OnAlert();
    }

    /// <summary>A new question or message in the queue: a sound if the responder wants one.</summary>
    private void OnQueueAlert()
    {
        if (_cfg.GetCVar(MentorHelpCVars.Sound))
            PlaySound();

        OnAlert();
    }

    private void PlaySound()
    {
        _audio?.PlayGlobal(AlertSound, Filter.Local(), false);
    }

    private void OnAlert()
    {
        if (_window != null)
            return;

        if (GameButton is { } button && !button.StyleClasses.Contains(MenuButton.StyleClassRedTopButton))
            button.StyleClasses.Add(MenuButton.StyleClassRedTopButton);
    }

    private void OnNotice(string text)
    {
        _lastNotice = text;
        if (_window == null)
            Open();

        _window?.ShowNotice(text);
    }
}
