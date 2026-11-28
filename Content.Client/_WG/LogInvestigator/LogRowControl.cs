using Content.Shared._WG.LogInvestigator;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WG.LogInvestigator;

/// <summary>
/// One log as the investigator lists it, in the result list and in reports alike: round time, type in its impact
/// colour, and the message wrapped to the width it has.
/// </summary>
public sealed class LogRowControl : ContainerButton
{
    private static readonly Color HoverColor = Color.FromHex("#ffffff14");
    private static readonly Color SelectedColor = Color.FromHex("#3d6fb666");

    private readonly StyleBoxFlat _background = new() { BackgroundColor = Color.Transparent };
    private bool _selected;

    /// <param name="time">The log's time as the window shows it.</param>
    /// <param name="row">The log.</param>
    /// <param name="prefix">Text before the message, such as how many logs a folded row holds.</param>
    public LogRowControl(string time, LogRow row, string? prefix = null)
    {
        Row = row;
        HorizontalExpand = true;

        var text = new FormattedMessage();
        text.PushColor(Color.Gray);
        text.AddText(time + " ");
        text.Pop();
        text.PushColor(LogInvestigatorStyle.ImpactColor(row.Impact));
        text.AddText($"[{row.Type}] ");
        text.Pop();
        if (prefix != null)
        {
            text.PushColor(Color.FromHex("#9ec5ff"));
            text.AddText(prefix + " ");
            text.Pop();
        }

        text.AddText(row.Message);

        var label = new RichTextLabel { HorizontalExpand = true, Margin = new Thickness(4, 1) };
        label.SetMessage(text);

        var panel = new PanelContainer { PanelOverride = _background, HorizontalExpand = true };
        panel.AddChild(label);
        AddChild(panel);
    }

    /// <summary>The log this row shows.</summary>
    public LogRow Row { get; }

    /// <summary>Whether this is the log shown in the detail pane.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            UpdateBackground();
        }
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        UpdateBackground();
    }

    private void UpdateBackground()
    {
        _background.BackgroundColor = _selected
            ? SelectedColor
            : DrawMode == DrawModeEnum.Hover ? HoverColor : Color.Transparent;
    }
}
