using Content.Shared._WF.GodPanel;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WF.GodPanel;

/// <summary>One open camera feed as the server reported it.</summary>
public readonly record struct WFGodFeedInfo(int Id, NetEntity Camera, NetEntity? Target, GameTick Tick);

/// <summary>
/// The client half of the God Panel: holds the latest state, the timeline and the open feeds, and sends the
/// panel's requests. The window reads from here and listens to the events.
/// </summary>
public sealed partial class WFGodPanelSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>The most timeline entries kept.</summary>
    public const int MaxLog = 500;

    private static readonly TimeSpan ResubscribeAfter = TimeSpan.FromSeconds(5);

    private readonly List<WFGodEvent> _log = new();
    private readonly Dictionary<int, WFGodFeedInfo> _feeds = new();

    private bool _open;
    private TimeSpan _lastState;
    private TimeSpan _lastSubscribe;
    private TimeSpan _lastRoundTime;
    private int _lastEventId;

    /// <summary>The latest state, or null before the first one arrives.</summary>
    public WFGodPanelState? State { get; private set; }

    /// <summary>The timeline, oldest first.</summary>
    public IReadOnlyList<WFGodEvent> Timeline => _log;

    /// <summary>Changes whenever the log is cleared, so a view knows to start over.</summary>
    public int LogGeneration { get; private set; }

    /// <summary>The open feeds by id.</summary>
    public IReadOnlyDictionary<int, WFGodFeedInfo> Feeds => _feeds;

    /// <summary>A new state arrived.</summary>
    public event Action? StateUpdated;

    /// <summary>A feed opened; the argument is its id.</summary>
    public event Action<int>? FeedOpened;

    /// <summary>A feed closed; the argument is its id.</summary>
    public event Action<int>? FeedClosed;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFGodPanelStateEvent>(OnState);
        SubscribeNetworkEvent<WFGodFeedOpenedEvent>(OnFeedOpened);
        SubscribeNetworkEvent<WFGodFeedClosedEvent>(OnFeedClosed);
    }

    /// <summary>Opens or closes the panel on the server. While open it resubscribes if the server goes quiet.</summary>
    public void Subscribe(bool subscribe)
    {
        _open = subscribe;
        _lastState = _lastSubscribe = _timing.RealTime;

        if (!subscribe)
        {
            foreach (var id in new List<int>(_feeds.Keys))
            {
                _feeds.Remove(id);
                FeedClosed?.Invoke(id);
            }
        }

        RaiseNetworkEvent(new WFGodPanelSubscribeEvent(subscribe));
    }

    /// <summary>Asks the server to do something to a player, a grid or the round.</summary>
    public void Act(WFGodAction action, NetEntity? target, string? text = null, bool flag = false)
    {
        RaiseNetworkEvent(new WFGodPanelActionEvent(action, target, text, flag));
    }

    /// <summary>Asks for a camera feed on a target, or a free camera for null.</summary>
    public void OpenFeed(NetEntity? target)
    {
        RaiseNetworkEvent(new WFGodFeedOpenEvent(target));
    }

    /// <summary>Closes a feed.</summary>
    public void CloseFeed(int feedId)
    {
        RaiseNetworkEvent(new WFGodFeedCloseEvent(feedId));
    }

    /// <summary>Moves a free camera.</summary>
    public void MoveFeed(int feedId, EntityCoordinates coordinates)
    {
        RaiseNetworkEvent(new WFGodFeedMoveEvent(feedId, GetNetCoordinates(coordinates)));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // The server forgets every panel when a round restarts.
        if (!_open)
            return;

        var now = _timing.RealTime;
        if (now - _lastState < ResubscribeAfter || now - _lastSubscribe < ResubscribeAfter)
            return;

        _lastSubscribe = now;
        RaiseNetworkEvent(new WFGodPanelSubscribeEvent(true));
    }

    private void OnState(WFGodPanelStateEvent ev)
    {
        _lastState = _timing.RealTime;

        var state = ev.State;
        if (state.Round.RoundTime < _lastRoundTime - TimeSpan.FromSeconds(5))
            ClearLog();

        _lastRoundTime = state.Round.RoundTime;
        State = state;

        foreach (var entry in state.Events)
        {
            if (entry.Id <= _lastEventId)
                continue;

            _lastEventId = entry.Id;
            _log.Add(entry);
        }

        if (_log.Count > MaxLog)
            _log.RemoveRange(0, _log.Count - MaxLog);

        StateUpdated?.Invoke();
    }

    private void ClearLog()
    {
        _log.Clear();
        LogGeneration++;
    }

    private void OnFeedOpened(WFGodFeedOpenedEvent ev)
    {
        // The window closed before the server answered.
        if (!_open)
        {
            CloseFeed(ev.FeedId);
            return;
        }

        _feeds[ev.FeedId] = new WFGodFeedInfo(ev.FeedId, ev.Camera, ev.Target, ev.Tick);
        FeedOpened?.Invoke(ev.FeedId);
    }

    private void OnFeedClosed(WFGodFeedClosedEvent ev)
    {
        if (_feeds.Remove(ev.FeedId))
            FeedClosed?.Invoke(ev.FeedId);
    }
}
