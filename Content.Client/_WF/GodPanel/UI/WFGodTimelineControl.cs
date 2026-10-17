using System.Numerics;
using Content.Shared._WF.GodPanel;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._WF.GodPanel.UI;

/// <summary>
/// The panel's bottom strip: what just happened, newest at the bottom, with a filter per kind of event.
/// </summary>
public sealed partial class WFGodTimelineControl : BoxContainer
{
    [Dependency] private IEntityManager _entities = default!;

    private const float StickyMargin = 24f;
    private const int StickyFrames = 3;

    private readonly WFGodPanelSystem _system;
    private readonly ScrollContainer _scroll = new() { VerticalExpand = true, HScrollEnabled = false };
    private readonly BoxContainer _list = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly Label _empty = new() { StyleClasses = { "LabelSubText" }, Margin = new Thickness(6) };
    private readonly List<(WFGodEvent Event, Control Row)> _rows = new();

    private WFGodEventCategory? _filter;
    private int _generation;
    private int _lastId;
    private int _stickFrames;

    /// <summary>A row with a subject or a place was clicked.</summary>
    public event Action<WFGodEvent>? EventClicked;

    public WFGodTimelineControl()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        MinHeight = 80;
        _system = _entities.System<WFGodPanelSystem>();

        var group = new ButtonGroup(false);
        var filters = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(4, 2) };
        filters.AddChild(new Label
        {
            Text = Loc.GetString("wf-godpanel-timeline-title"),
            StyleClasses = { "LabelHeading" },
            Margin = new Thickness(0, 0, 10, 0),
        });
        filters.AddChild(Filter(Loc.GetString("wf-godpanel-category-all"), null, group, true));
        foreach (var category in Enum.GetValues<WFGodEventCategory>())
        {
            filters.AddChild(Filter(WFGodPanelText.CategoryName(category), category, group, false));
        }

        AddChild(filters);

        _empty.Text = Loc.GetString("wf-godpanel-timeline-empty");
        _scroll.AddChild(_list);
        AddChild(_scroll);
        Rebuild();
    }

    private Button Filter(string text, WFGodEventCategory? category, ButtonGroup group, bool pressed)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            Group = group,
            Pressed = pressed,
            StyleClasses = { "ButtonSquare" },
        };
        button.OnPressed += _ =>
        {
            _filter = category;
            Rebuild();
        };
        return button;
    }

    /// <summary>Adds the events that arrived since the last call, or starts over after the log was cleared.</summary>
    public void Update()
    {
        if (_generation != _system.LogGeneration)
        {
            Rebuild();
            return;
        }

        var added = false;
        var atBottom = AtBottom();
        foreach (var entry in _system.Timeline)
        {
            if (entry.Id <= _lastId)
                continue;

            _lastId = entry.Id;
            if (_filter != null && entry.Category != _filter)
                continue;

            AddRow(entry);
            added = true;
        }

        while (_rows.Count > WFGodPanelSystem.MaxLog)
        {
            _list.RemoveChild(_rows[0].Row);
            _rows.RemoveAt(0);
        }

        if (added)
            _empty.Visible = false;

        if (added && atBottom)
            _stickFrames = StickyFrames;
    }

    private bool AtBottom()
    {
        return _list.Size.Y - _scroll.Size.Y - _scroll.VScroll < StickyMargin;
    }

    private void Rebuild()
    {
        _generation = _system.LogGeneration;
        _rows.Clear();
        _list.RemoveAllChildren();
        _list.AddChild(_empty);

        _lastId = 0;
        foreach (var entry in _system.Timeline)
        {
            _lastId = Math.Max(_lastId, entry.Id);
            if (_filter == null || entry.Category == _filter)
                AddRow(entry);
        }

        _empty.Visible = _rows.Count == 0;
        _stickFrames = StickyFrames;
    }

    private void AddRow(WFGodEvent entry)
    {
        var row = new TimelineRow(entry);
        row.Clicked += e => EventClicked?.Invoke(e);
        _rows.Add((entry, row));
        _list.AddChild(row);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // The scroll range only grows once the new rows have been laid out.
        if (_stickFrames <= 0)
            return;

        _stickFrames--;
        _scroll.VScroll = float.MaxValue;
    }

    /// <summary>One line of the log.</summary>
    private sealed class TimelineRow : PanelContainer
    {
        private readonly WFGodEvent _event;

        public event Action<WFGodEvent>? Clicked;

        public TimelineRow(WFGodEvent entry)
        {
            _event = entry;
            HorizontalExpand = true;
            MouseFilter = entry.Subject != null || entry.Coordinates != null ? MouseFilterMode.Stop : MouseFilterMode.Pass;

            var line = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(6, 1) };
            line.AddChild(new Label
            {
                Text = WFGodPanelText.Minutes(entry.Time),
                MinWidth = 52,
                FontColorOverride = WFGodPanelText.Dim,
            });
            line.AddChild(new Label
            {
                Text = WFGodPanelText.CategoryName(entry.Category),
                MinWidth = 88,
                FontColorOverride = WFGodPanelText.CategoryColor(entry.Category),
            });
            line.AddChild(new Label { Text = entry.Text, ClipText = true, HorizontalExpand = true });
            AddChild(line);
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (args.Function != EngineKeyFunctions.UIClick || _event.Subject == null && _event.Coordinates == null)
                return;

            Clicked?.Invoke(_event);
            args.Handle();
        }
    }
}
