using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._WF.GodPanel.UI;

/// <summary>
/// One roster row: a status dot, a title with tags, a second line and an optional health bar. Rows are made once
/// per player or grid and refreshed in place, so scroll position and selection survive state updates.
/// </summary>
public sealed partial class WFGodRow : PanelContainer
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan DoubleClick = TimeSpan.FromMilliseconds(400);

    private readonly List<Label> _tags = new();
    private readonly BoxContainer _tagBox = new() { Orientation = BoxContainer.LayoutOrientation.Horizontal };
    private readonly StyleBoxFlat _normal = new() { BackgroundColor = WFGodPanelText.PanelColor };
    private readonly StyleBoxFlat _selected = new() { BackgroundColor = WFGodPanelText.SelectedColor };
    private TimeSpan _lastClick;
    private bool _isSelected;

    /// <summary>The entity this row stands for, if it has one.</summary>
    public NetEntity? Entity;

    /// <summary>Whether <see cref="Entity"/> is a grid rather than a body.</summary>
    public bool IsGrid;

    public readonly WFGodStatusDot Dot = new();
    public readonly Label Title = new() { ClipText = true, HorizontalExpand = true, StyleClasses = { "LabelKeyText" } };
    public readonly Label Sub = new() { ClipText = true, StyleClasses = { "LabelSubText" } };
    public readonly ProgressBar Health = new()
    {
        MinValue = 0,
        MaxValue = 1,
        SetHeight = 5,
        Margin = new Thickness(0, 2, 0, 0),
        Visible = false,
    };

    /// <summary>The right-hand column, for a row's own buttons.</summary>
    public readonly BoxContainer Side = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalAlignment = VAlignment.Center };

    /// <summary>The row was clicked.</summary>
    public event Action<WFGodRow>? Clicked;

    /// <summary>The row was clicked twice in quick succession.</summary>
    public event Action<WFGodRow>? DoubleClicked;

    public WFGodRow()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        PanelOverride = _normal;

        Health.BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#101012") };
        Health.ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = WFGodPanelText.Green };

        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        top.AddChild(Title);
        top.AddChild(_tagBox);

        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        text.AddChild(top);
        text.AddChild(Sub);
        text.AddChild(Health);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(6, 3),
            SeparationOverride = 6,
        };
        row.AddChild(Dot);
        row.AddChild(text);
        row.AddChild(Side);
        AddChild(row);
    }

    /// <summary>Whether the row is drawn as the panel's selection.</summary>
    public bool Selected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PanelOverride = value ? _selected : _normal;
        }
    }

    /// <summary>Shows the health bar at a fraction, or hides it for null.</summary>
    public void SetHealth(float? fraction)
    {
        Health.Visible = fraction != null;
        if (fraction is not { } value)
            return;

        Health.Value = Math.Clamp(value, 0f, 1f);
        if (Health.ForegroundStyleBoxOverride is StyleBoxFlat box)
            box.BackgroundColor = value > 0.5f ? WFGodPanelText.Green : value > 0.25f ? WFGodPanelText.Orange : WFGodPanelText.Red;
    }

    /// <summary>Reuses the row's tag labels for a new set, hiding the spare ones.</summary>
    public void SetTags(List<(string Text, Color Color, bool Italic)> tags)
    {
        while (_tags.Count < tags.Count)
        {
            var label = WFGodPanelText.Tag(string.Empty, Color.White);
            _tags.Add(label);
            _tagBox.AddChild(label);
        }

        for (var i = 0; i < _tags.Count; i++)
        {
            var label = _tags[i];
            label.Visible = i < tags.Count;
            if (i >= tags.Count)
                continue;

            label.Text = tags[i].Text;
            label.FontColorOverride = tags[i].Color;
            if (tags[i].Italic)
                label.AddStyleClass("Italic");
            else
                label.RemoveStyleClass("Italic");
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        var now = _timing.RealTime;
        Clicked?.Invoke(this);
        if (now - _lastClick < DoubleClick)
        {
            _lastClick = default;
            DoubleClicked?.Invoke(this);
        }
        else
        {
            _lastClick = now;
        }

        args.Handle();
    }
}
