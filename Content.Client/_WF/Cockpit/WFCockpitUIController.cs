using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WF.Cockpit;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>Owns the optional cockpit and returns the normal HUD when piloting eligibility ends.</summary>
public sealed partial class WFCockpitUIController : UIController
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    private WFCockpitView? _view;
    private ShuttleConsoleWindow? _console;
    private InGameScreen? _screen;
    private EntityUid? _pilot;

    /// <summary>Whether a cockpit currently owns the screen.</summary>
    public bool Active => _view != null;

    public override void Initialize()
    {
        base.Initialize();
        UIManager.GetUIController<GameplayStateLoadController>().OnScreenUnload += () => Exit();
    }

    /// <summary>Checks seat and helm ownership without changing gameplay state.</summary>
    public bool CanEnter(EntityUid? console) => UIManager.ActiveScreen is InGameScreen &&
        _entities.System<SharedWFCockpitSystem>().CanEnter(_player.LocalEntity, console);

    /// <summary>Rehouses the existing console and world viewport in a cockpit.</summary>
    public bool Enter(ShuttleConsoleWindow console)
    {
        if (Active || !CanEnter(console.WfCockpitConsole) ||
            UIManager.ActiveScreen is not InGameScreen screen || screen.GetWidget<MainViewport>() is not { } viewport)
            return false;
        _console = console;
        _screen = screen;
        _pilot = _player.LocalEntity;
        _view = new WFCockpitView(console, screen, viewport, () => Exit());
        return true;
    }

    /// <summary>Restores the same chat, viewport and console controls, including unfinished input.</summary>
    public void Exit(ShuttleConsoleWindow? owner = null)
    {
        if (_view == null || owner != null && owner != _console)
            return;
        var view = _view;
        _view = null;
        _console = null;
        _screen = null;
        _pilot = null;
        view.Restore();
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_view != null && (_player.LocalEntity != _pilot || UIManager.ActiveScreen != _screen ||
            !CanEnter(_console?.WfCockpitConsole)))
            Exit();
    }
}
