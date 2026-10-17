using Content.Shared._WF.GodPanel;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.GodPanel.UI;

/// <summary>
/// The panel's right side: the open camera feeds in a grid, and a button for a free camera.
/// </summary>
public sealed partial class WFGodFeedsControl : BoxContainer
{
    [Dependency] private IEntityManager _entities = default!;

    /// <summary>The most feeds the panel offers; the server's cvar has the last word.</summary>
    public const int MaxFeeds = 6;

    private readonly Dictionary<int, WFGodFeedControl> _feeds = new();
    private readonly WFGodPanelSystem _system;
    private readonly GridContainer _grid = new() { Columns = 2, HorizontalExpand = true };
    private readonly Button _free;
    private readonly Label _empty = new() { StyleClasses = { "LabelSubText" }, Margin = new Thickness(6) };

    private WFGodPanelState? _state;

    /// <summary>A feed asked for its target to be selected.</summary>
    public event Action<NetEntity>? SelectRequested;

    public WFGodFeedsControl()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        MinWidth = 240;
        _system = _entities.System<WFGodPanelSystem>();

        _free = WFGodPanelText.SmallButton(Loc.GetString("wf-godpanel-feeds-free"), Loc.GetString("wf-godpanel-feeds-free-tooltip"));
        _free.OnPressed += _ => _system.OpenFeed(null);

        var header = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(4, 2) };
        header.AddChild(new Label { Text = Loc.GetString("wf-godpanel-feeds-title"), StyleClasses = { "LabelHeading" }, HorizontalExpand = true });
        header.AddChild(_free);
        AddChild(header);

        _empty.Text = Loc.GetString("wf-godpanel-feeds-empty");
        AddChild(_empty);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_grid);
        AddChild(scroll);

        _system.FeedOpened += OnFeedOpened;
        _system.FeedClosed += OnFeedClosed;

        // Feeds already open when the window is made, e.g. after it was popped out and back.
        foreach (var id in new List<int>(_system.Feeds.Keys))
        {
            OnFeedOpened(id);
        }
    }

    /// <summary>Stops listening and closes every feed. Called when the window closes, not when it is popped out.</summary>
    public void Detach()
    {
        _system.FeedOpened -= OnFeedOpened;
        _system.FeedClosed -= OnFeedClosed;
        CloseAll();
    }

    /// <summary>Asks for a feed on a target.</summary>
    public void Watch(NetEntity target)
    {
        foreach (var feed in _feeds.Values)
        {
            if (feed.Info.Target == target)
                return;
        }

        _system.OpenFeed(target);
    }

    /// <summary>Closes every feed on the server and drops their controls.</summary>
    public void CloseAll()
    {
        foreach (var id in new List<int>(_feeds.Keys))
        {
            _system.CloseFeed(id);
            Remove(id);
        }
    }

    /// <summary>Names the feeds after what they show.</summary>
    public void Update(WFGodPanelState state)
    {
        _state = state;
        foreach (var feed in _feeds.Values)
        {
            feed.SetTitle(TitleOf(feed.Info));
        }
    }

    private string TitleOf(WFGodFeedInfo info)
    {
        if (info.Target is not { } target)
            return Loc.GetString("wf-godpanel-feed-free");

        if (_state != null)
        {
            foreach (var grid in _state.Grids)
            {
                if (grid.Entity == target)
                    return grid.Name;
            }

            foreach (var player in _state.Players)
            {
                if (player.Entity == target)
                    return player.Character.Length > 0 ? player.Character : player.Username;
            }
        }

        return Loc.GetString("wf-godpanel-feed-unknown");
    }

    private void OnFeedOpened(int id)
    {
        if (_feeds.ContainsKey(id) || !_system.Feeds.TryGetValue(id, out var info))
            return;

        var feed = new WFGodFeedControl(info);
        feed.SetTitle(TitleOf(info));
        feed.CloseRequested += feedId => _system.CloseFeed(feedId);
        feed.SelectRequested += target => SelectRequested?.Invoke(target);
        _feeds[id] = feed;
        _grid.AddChild(feed);
        Changed();
    }

    private void OnFeedClosed(int id)
    {
        Remove(id);
    }

    private void Remove(int id)
    {
        if (!_feeds.Remove(id, out var feed))
            return;

        feed.Release();
        _grid.RemoveChild(feed);
        Changed();
    }

    private void Changed()
    {
        _empty.Visible = _feeds.Count == 0;
        _free.Disabled = _feeds.Count >= MaxFeeds;
    }
}
