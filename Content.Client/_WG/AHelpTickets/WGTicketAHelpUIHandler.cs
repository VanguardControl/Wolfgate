using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Client.Administration.UI.Bwoink;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.Shared.Administration;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Network;

namespace Content.Client._WG.AHelpTickets;

/// <summary>The staff side of ahelp when tickets are on: the ticket panel in a window or popped out, with one chat panel per player.</summary>
public sealed class WGTicketAHelpUIHandler : IAHelpUIHandler
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private readonly NetUserId _owner;
    private readonly Dictionary<NetUserId, BwoinkPanel> _panels = new();

    private DefaultWindow? _window;
    private IClydeWindow? _clydeWindow;
    private WindowRoot? _windowRoot;
    private AHelpTicketHistoryWindow? _history;
    private bool _everOpened;

    public AHelpTicketsControl? Control { get; private set; }

    public WGTicketAHelpUIHandler(NetUserId owner)
    {
        IoCManager.InjectDependencies(this);
        _owner = owner;
    }

    public bool IsAdmin => true;
    public bool IsOpen => _window is { Disposed: false, IsOpen: true } || _clydeWindow is { IsDisposed: false };

    public event Action? OnClose;
    public event Action? OnOpen;
    public Action<NetUserId, string, bool, bool>? SendMessageAction { get; set; }
    public event Action<NetUserId, string>? InputTextChanged;

    public void Receive(SharedBwoinkSystem.BwoinkTextMessage message)
    {
        EnsurePanel(message.UserId).ReceiveLine(message);
        Control?.OnLine(message.UserId);
    }

    public void Open(NetUserId channel, bool relayActive)
    {
        EnsureControl();
        if (channel != _owner)
            Control!.OpenPlayer(channel);

        OpenWindow();
    }

    public void ToggleWindow()
    {
        if (IsOpen)
            Close();
        else
        {
            EnsureControl();
            OpenWindow();
        }
    }

    public void Close()
    {
        _window?.Close();

        if (_clydeWindow == null)
            return;

        // A popped-out panel can't go back into the window, so it is rebuilt next time; the chats are kept.
        _clydeWindow.RequestClosed -= OnRequestClosed;
        _clydeWindow.Dispose();
        _clydeWindow = null;
        _windowRoot = null;
        foreach (var panel in _panels.Values)
        {
            panel.Orphan();
        }

        Control?.Dispose();
        Control = null;
        OnClose?.Invoke();
    }

    public void DiscordRelayChanged(bool active)
    {
    }

    public void PeopleTypingUpdated(BwoinkPlayerTypingUpdated args)
    {
        if (_panels.TryGetValue(args.Channel, out var panel))
            panel.UpdatePlayerTyping(args.PlayerName, args.Typing);

        Control?.PeopleTypingUpdated(args);
    }

    /// <summary>Sends a staff line in a player's ahelp, as typed in their chat panel.</summary>
    public void Send(NetUserId channel, string text, bool playSound, bool adminOnly)
    {
        SendMessageAction?.Invoke(channel, text, playSound, adminOnly);
    }

    public bool TryGetChannel(NetUserId channel, [NotNullWhen(true)] out BwoinkPanel? panel)
    {
        return _panels.TryGetValue(channel, out panel);
    }

    /// <summary>Shows only this player's chat panel.</summary>
    public void ShowPanel(NetUserId channel)
    {
        var shown = EnsurePanel(channel);
        foreach (var panel in _panels.Values)
        {
            panel.Visible = panel == shown;
        }
    }

    /// <summary>The ticket history window, opened if need be.</summary>
    public AHelpTicketHistoryWindow OpenHistory()
    {
        if (_history is not { Disposed: false })
            _history = new AHelpTicketHistoryWindow();

        if (!_history.IsOpen)
            _history.OpenCentered();

        _history.MoveToFront();
        return _history;
    }

    /// <summary>Moves the panel into an OS window of its own.</summary>
    public void PopOut()
    {
        if (_window == null || Control == null)
            return;

        Control.Orphan();
        _window.Dispose();
        _window = null;
        _everOpened = false;

        _clydeWindow = _clyde.CreateWindow(new WindowCreateParameters
        {
            Maximized = false,
            Title = Loc.GetString("wg-ahelp-tickets-title"),
            Monitor = _clyde.EnumerateMonitors().First(),
            Width = 1000,
            Height = 560,
        });
        _clydeWindow.RequestClosed += OnRequestClosed;
        _clydeWindow.DisposeOnClose = true;
        _windowRoot = _ui.CreateWindowRoot(_clydeWindow);
        _windowRoot.AddChild(Control);
        Control.PopOut.Visible = false;
    }

    private void OnRequestClosed(WindowRequestClosedEventArgs args)
    {
        Close();
    }

    private void OpenWindow()
    {
        if (_window == null)
            return;

        if (_everOpened)
            _window.Open();
        else
            _window.OpenCentered();
    }

    private void EnsureControl()
    {
        if (Control is { Disposed: false })
            return;

        Control = new AHelpTicketsControl(this) { VerticalExpand = true, HorizontalExpand = true };
        Control.PopOut.OnPressed += _ => PopOut();
        foreach (var panel in _panels.Values)
        {
            panel.Orphan();
            panel.Visible = false;
            Control.BwoinkArea.AddChild(panel);
        }

        _window = new DefaultWindow
        {
            Title = Loc.GetString("wg-ahelp-tickets-title"),
            MinSize = new Vector2(900, 520),
            SetSize = new Vector2(1000, 600),
        };
        _window.Contents.AddChild(Control);
        _window.OnClose += () => OnClose?.Invoke();
        _window.OnOpen += () =>
        {
            OnOpen?.Invoke();
            _everOpened = true;
        };
    }

    private BwoinkPanel EnsurePanel(NetUserId channel)
    {
        EnsureControl();
        if (_panels.TryGetValue(channel, out var panel))
            return panel;

        panel = new BwoinkPanel(text => SendMessageAction?.Invoke(channel, text,
            (Control?.PlaySound.Pressed ?? true) && !(Control?.AdminOnly.Pressed ?? false),
            Control?.AdminOnly.Pressed ?? false))
        {
            Visible = false,
        };
        panel.InputTextChanged += text => InputTextChanged?.Invoke(channel, text);
        _panels[channel] = panel;
        Control!.BwoinkArea.AddChild(panel);
        return panel;
    }

    public void Dispose()
    {
        _history?.Dispose();
        _clydeWindow?.Dispose();
        _window?.Dispose();
        Control?.Dispose();
        _window = null;
        _clydeWindow = null;
        Control = null;
        _panels.Clear();
    }
}
