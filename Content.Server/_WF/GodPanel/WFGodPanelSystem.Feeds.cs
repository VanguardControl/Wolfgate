using System.Linq;
using System.Numerics;
using Content.Shared._WF.GodPanel;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.GodPanel;

public sealed partial class WFGodPanelSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedViewSubscriberSystem _viewSubscriber = default!;

    private static readonly EntProtoId CameraProto = "AdminCamera";

    private sealed class Feed
    {
        public int Id;
        public ICommonSession Session = default!;
        public EntityUid Camera;
        public EntityUid? Target;
        public bool Free;
    }

    private readonly Dictionary<int, Feed> _feeds = new();

    /// <summary>Not reset between rounds, so a stale close can't match a new feed.</summary>
    private int _lastFeedId;

    private void InitializeFeeds()
    {
        SubscribeNetworkEvent<WFGodFeedOpenEvent>(OnFeedOpen);
        SubscribeNetworkEvent<WFGodFeedCloseEvent>(OnFeedClose);
        SubscribeNetworkEvent<WFGodFeedMoveEvent>(OnFeedMove);
    }

    private void OnFeedOpen(WFGodFeedOpenEvent ev, EntitySessionEventArgs args)
    {
        EntityUid? target = null;
        if (ev.Target is { } net && !TryGetEntity(net, out target))
            return;

        OpenFeed(args.SenderSession, target);
    }

    private void OnFeedClose(WFGodFeedCloseEvent ev, EntitySessionEventArgs args)
    {
        CloseFeed(args.SenderSession, ev.FeedId);
    }

    private void OnFeedMove(WFGodFeedMoveEvent ev, EntitySessionEventArgs args)
    {
        MoveFeed(args.SenderSession, ev.FeedId, GetCoordinates(ev.Coordinates));
    }

    /// <summary>
    /// Opens a camera feed for a subscribed admin: an admin camera attached to the target, so it follows it, or
    /// a free camera on the admin's map when <paramref name="target"/> is null. Returns the feed id, or null if
    /// it was refused.
    /// </summary>
    public int? OpenFeed(ICommonSession session, EntityUid? target)
    {
        if (!_subscribers.ContainsKey(session) || !HasFlag(session, AdminFlags.Admin))
            return null;

        var count = 0;
        foreach (var other in _feeds.Values)
        {
            if (other.Session == session)
                count++;
        }

        if (count >= _cfg.GetCVar(WFGodPanelCVars.MaxFeeds))
            return null;

        EntityCoordinates coordinates;
        if (target is { } uid)
        {
            if (TerminatingOrDeleted(uid) || Transform(uid).MapID == MapId.Nullspace)
                return null;

            // A grid is looked at from its middle.
            coordinates = TryComp<MapGridComponent>(uid, out var grid)
                ? new EntityCoordinates(uid, grid.LocalAABB.Center)
                : new EntityCoordinates(uid, Vector2.Zero);
        }
        else
        {
            if (session.AttachedEntity is not { } gm || TerminatingOrDeleted(gm)
                || Transform(gm).MapUid is not { } map)
                return null;

            coordinates = new EntityCoordinates(map, _transform.GetWorldPosition(gm));
        }

        // Spawned attached to the target so it moves with it. Its visibility keeps cheat clients from seeing it;
        // the view subscription sends it, and what is around it, to this admin.
        var camera = SpawnAttachedTo(CameraProto, coordinates);
        _viewSubscriber.AddViewSubscriber(camera, session);

        var feed = new Feed
        {
            Id = ++_lastFeedId,
            Session = session,
            Camera = camera,
            Target = target,
            Free = target == null,
        };
        _feeds[feed.Id] = feed;

        RaiseNetworkEvent(new WFGodFeedOpenedEvent(feed.Id, GetNetEntity(camera),
            target is { } t ? GetNetEntity(t) : null, _timing.CurTick), session);
        return feed.Id;
    }

    /// <summary>Closes one of the session's feeds. Returns whether there was one.</summary>
    public bool CloseFeed(ICommonSession session, int feedId)
    {
        if (!_feeds.TryGetValue(feedId, out var feed) || feed.Session != session)
            return false;

        CloseFeed(feed);
        return true;
    }

    private void CloseFeeds(ICommonSession session)
    {
        foreach (var feed in _feeds.Values.Where(feed => feed.Session == session).ToList())
        {
            CloseFeed(feed);
        }
    }

    private void CloseFeed(Feed feed)
    {
        _feeds.Remove(feed.Id);

        if (Exists(feed.Camera))
        {
            _viewSubscriber.RemoveViewSubscriber(feed.Camera, feed.Session);
            Del(feed.Camera);
        }

        if (feed.Session.Status != SessionStatus.Disconnected)
            RaiseNetworkEvent(new WFGodFeedClosedEvent(feed.Id), feed.Session);
    }

    /// <summary>Moves a free camera to a point on a map or grid. Only the feed's own session may.</summary>
    public bool MoveFeed(ICommonSession session, int feedId, EntityCoordinates coordinates)
    {
        if (!_feeds.TryGetValue(feedId, out var feed) || feed.Session != session || !feed.Free
            || TerminatingOrDeleted(feed.Camera) || !coordinates.IsValid(EntityManager))
            return false;

        var map = _transform.ToMapCoordinates(coordinates);
        if (map.MapId == MapId.Nullspace || !float.IsFinite(map.X) || !float.IsFinite(map.Y))
            return false;

        _transform.SetMapCoordinates(feed.Camera, map);
        return true;
    }

    /// <summary>Closes feeds whose camera or target is gone, or whose admin lost the panel.</summary>
    private void UpdateFeeds()
    {
        if (_feeds.Count == 0)
            return;

        foreach (var feed in _feeds.Values.ToList())
        {
            if (TerminatingOrDeleted(feed.Camera)
                || feed.Target is { } target && TerminatingOrDeleted(target)
                || !_subscribers.ContainsKey(feed.Session))
                CloseFeed(feed);
        }
    }
}
