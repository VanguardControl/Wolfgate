using JetBrains.Annotations;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._WG.Mentors;

/// <summary>Opens the Mentoring page from the Esc menu and keeps it in step with the server.</summary>
[UsedImplicitly]
public sealed class MentoringUIController : UIController, IOnSystemChanged<WGClientMentorPageSystem>
{
    private WGClientMentorPageSystem? _system;
    private MentoringWindow? _window;

    public void OnSystemLoaded(WGClientMentorPageSystem system)
    {
        _system = system;
        system.PageReceived += OnPage;
    }

    public void OnSystemUnloaded(WGClientMentorPageSystem system)
    {
        system.PageReceived -= OnPage;
        _system = null;
        _window?.Close();
    }

    public void ToggleWindow()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        if (_system == null)
            return;

        _window = new MentoringWindow();
        _window.OnClose += () => _window = null;
        _window.ApplyPressed += (answers, agreed) => _system?.Apply(answers, agreed);
        _window.ActionPressed += action => _system?.PageAction(action);
        _window.OpenCentered();
        _system.RequestPage();
    }

    private void OnPage(Content.Shared._WG.Mentors.WGMentorPageStateEvent state)
    {
        _window?.SetState(state);
    }
}
