using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._WF.GodPanel;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._WF.GodPanel;

/// <summary>
/// The gamemaster's overwatch panel: keeps the set of open panels, sends each the round state about once a
/// second, and owns their camera feeds, actions and the event timeline (see the other partials).
/// </summary>
public sealed partial class WFGodPanelSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ISharedPlayerManager _players = default!;

    /// <summary>Open panels, each with the id of the last timeline event it was sent.</summary>
    private readonly Dictionary<ICommonSession, int> _subscribers = new();

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFGodPanelSubscribeEvent>(OnSubscribe);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;

        InitializeFeeds();
        InitializeActions();
        InitializeLog();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    /// <summary>How many panels are open.</summary>
    public int SubscriberCount => _subscribers.Count;

    /// <summary>Whether this session has the panel open.</summary>
    public bool IsSubscribed(ICommonSession session)
    {
        return _subscribers.ContainsKey(session);
    }

    private bool HasFlag(ICommonSession session, AdminFlags flag)
    {
        return _admin.HasAdminFlag(session, flag);
    }

    private void OnSubscribe(WFGodPanelSubscribeEvent ev, EntitySessionEventArgs args)
    {
        Subscribe(args.SenderSession, ev.Subscribe);
    }

    /// <summary>
    /// Opens or closes a session's panel. Opening needs the Admin flag and sends the first state at once;
    /// closing drops its feeds. Returns whether the session is subscribed afterwards.
    /// </summary>
    public bool Subscribe(ICommonSession session, bool subscribe)
    {
        if (!subscribe)
        {
            Unsubscribe(session);
            return false;
        }

        if (!HasFlag(session, AdminFlags.Admin))
        {
            _adminLog.Add(LogType.AdminCommands, LogImpact.Medium,
                $"{session.Name} tried to open the God Panel without the Admin flag");
            return false;
        }

        if (_subscribers.ContainsKey(session))
            return true;

        _subscribers[session] = 0;
        Send(session, BuildBase());
        return true;
    }

    private void Unsubscribe(ICommonSession session)
    {
        if (!_subscribers.Remove(session))
            return;

        CloseFeeds(session);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            Unsubscribe(args.Session);

        OnLogPlayerStatus(args);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        foreach (var session in _subscribers.Keys.ToList())
        {
            CloseFeeds(session);
        }

        _subscribers.Clear();
        ClearLog();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateFeeds();

        if (_subscribers.Count == 0)
            return;

        _accumulator += frameTime;
        if (_accumulator < Math.Max(0.1f, _cfg.GetCVar(WFGodPanelCVars.UpdateInterval)))
            return;

        _accumulator = 0;
        SendAll();
    }

    /// <summary>Builds the state once and sends every open panel its copy with only the events it has not seen.</summary>
    public void SendAll()
    {
        foreach (var session in _subscribers.Keys.ToList())
        {
            if (!HasFlag(session, AdminFlags.Admin))
                Unsubscribe(session);
        }

        if (_subscribers.Count == 0)
            return;

        var state = BuildBase();
        foreach (var session in _subscribers.Keys.ToList())
        {
            Send(session, state);
        }
    }

    private void Send(ICommonSession session, WFGodPanelState state)
    {
        var last = _subscribers.GetValueOrDefault(session);
        _subscribers[session] = LastEventId;
        RaiseNetworkEvent(new WFGodPanelStateEvent(state with { Events = EventsAfter(last), LastEventId = LastEventId }), session);
    }
}
