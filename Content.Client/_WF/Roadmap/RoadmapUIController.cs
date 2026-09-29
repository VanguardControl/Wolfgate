using Content.Client.Lobby;
using Content.Client.UserInterface.Systems.Info;
using Robust.Client.State;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._WF.Roadmap;

/// <summary>
/// Opens the roadmap from the lobby links and the game menu, and once per launch on reaching the lobby,
/// waiting for the rules popup if one is up.
/// </summary>
public sealed class RoadmapUIController : UIController, IOnStateEntered<LobbyState>
{
    [Dependency] private IStateManager _state = default!;
    [Dependency] private InfoUIController _info = default!;

    private RoadmapWindow? _window;
    private bool _shown;

    /// <summary>Whether the roadmap window is open.</summary>
    public bool IsOpen => _window != null;

    public override void Initialize()
    {
        base.Initialize();
        _info.RulesAccepted += OnRulesAccepted;
    }

    public void OnStateEntered(LobbyState state)
    {
        if (_shown || _info.RulesPopupOpen)
            return;

        OpenRoadmap();
    }

    private void OnRulesAccepted()
    {
        if (!_shown && _state.CurrentState is LobbyState)
            OpenRoadmap();
    }

    /// <summary>Opens the roadmap, or closes it if it is open.</summary>
    public void ToggleRoadmap()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        OpenRoadmap();
    }

    private void OpenRoadmap()
    {
        _shown = true;
        if (_window != null)
            return;

        _window = new RoadmapWindow();
        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }
}
