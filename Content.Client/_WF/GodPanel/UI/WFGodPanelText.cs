using System.Numerics;
using Content.Shared._WF.GodPanel;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.GodPanel.UI;

/// <summary>Colours and names the God Panel's panes share.</summary>
public static class WFGodPanelText
{
    public static readonly Color PanelColor = Color.FromHex("#1B1B1F");
    public static readonly Color SelectedColor = Color.FromHex("#2D3E57");
    public static readonly Color Dim = Color.FromHex("#8A8A90");
    public static readonly Color Red = Color.FromHex("#E53935");
    public static readonly Color Gold = Color.FromHex("#FFC107");
    public static readonly Color Green = Color.FromHex("#4CAF50");
    public static readonly Color Orange = Color.FromHex("#FF9800");
    public static readonly Color Blue = Color.FromHex("#4FC3F7");

    public static Color StatusColor(WFGodPlayerStatus status)
    {
        return status switch
        {
            WFGodPlayerStatus.Alive => Green,
            WFGodPlayerStatus.Critical => Orange,
            WFGodPlayerStatus.Dead => Red,
            WFGodPlayerStatus.Ghost => Color.FromHex("#9E9E9E"),
            WFGodPlayerStatus.Lobby => Color.FromHex("#5F6368"),
            _ => Color.FromHex("#4A4A4A"),
        };
    }

    public static string StatusName(WFGodPlayerStatus status)
    {
        return status switch
        {
            WFGodPlayerStatus.Alive => Loc.GetString("wf-godpanel-status-alive"),
            WFGodPlayerStatus.Critical => Loc.GetString("wf-godpanel-status-critical"),
            WFGodPlayerStatus.Dead => Loc.GetString("wf-godpanel-status-dead"),
            WFGodPlayerStatus.Ghost => Loc.GetString("wf-godpanel-status-ghost"),
            WFGodPlayerStatus.Lobby => Loc.GetString("wf-godpanel-status-lobby"),
            _ => Loc.GetString("wf-godpanel-status-disconnected"),
        };
    }

    public static Color CategoryColor(WFGodEventCategory category)
    {
        return category switch
        {
            WFGodEventCategory.Death => Red,
            WFGodEventCategory.Player => Blue,
            WFGodEventCategory.Ship => Color.FromHex("#FFB74D"),
            WFGodEventCategory.Encounter => Color.FromHex("#BA68C8"),
            WFGodEventCategory.Sector => Color.FromHex("#81C784"),
            WFGodEventCategory.Round => Color.FromHex("#B0BEC5"),
            _ => Gold,
        };
    }

    public static string CategoryName(WFGodEventCategory category)
    {
        return category switch
        {
            WFGodEventCategory.Death => Loc.GetString("wf-godpanel-category-death"),
            WFGodEventCategory.Player => Loc.GetString("wf-godpanel-category-player"),
            WFGodEventCategory.Ship => Loc.GetString("wf-godpanel-category-ship"),
            WFGodEventCategory.Encounter => Loc.GetString("wf-godpanel-category-encounter"),
            WFGodEventCategory.Sector => Loc.GetString("wf-godpanel-category-sector"),
            WFGodEventCategory.Round => Loc.GetString("wf-godpanel-category-round"),
            _ => Loc.GetString("wf-godpanel-category-gm"),
        };
    }

    public static Color KindColor(WFGodGridKind kind)
    {
        return kind switch
        {
            WFGodGridKind.Station => Blue,
            WFGodGridKind.Ship => Green,
            WFGodGridKind.Encounter => Color.FromHex("#BA68C8"),
            _ => Dim,
        };
    }

    public static string KindName(WFGodGridKind kind)
    {
        return kind switch
        {
            WFGodGridKind.Station => Loc.GetString("wf-godpanel-kind-station"),
            WFGodGridKind.Ship => Loc.GetString("wf-godpanel-kind-ship"),
            WFGodGridKind.Encounter => Loc.GetString("wf-godpanel-kind-encounter"),
            _ => Loc.GetString("wf-godpanel-kind-debris"),
        };
    }

    /// <summary>Round time as hh:mm:ss.</summary>
    public static string Clock(TimeSpan time)
    {
        return $"{(int) time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }

    /// <summary>Round time as mm:ss, minutes running past 59.</summary>
    public static string Minutes(TimeSpan time)
    {
        return $"{(int) time.TotalMinutes:00}:{time.Seconds:00}";
    }

    public static string Percent(float fraction)
    {
        return $"{(int) MathF.Round(Math.Clamp(fraction, 0f, 1f) * 100f)}%";
    }

    /// <summary>A flat button, small enough to sit in a bar of them.</summary>
    public static Button SmallButton(string text, string? tooltip = null)
    {
        return new Button
        {
            Text = text,
            ToolTip = tooltip,
            StyleClasses = { "ButtonSquare" },
        };
    }

    /// <summary>A short coloured word.</summary>
    public static Label Tag(string text, Color color)
    {
        return new Label
        {
            Text = text,
            FontColorOverride = color,
            StyleClasses = { "LabelSubText" },
            Margin = new Thickness(0, 0, 6, 0),
        };
    }
}

/// <summary>A small round dot in a colour.</summary>
public sealed class WFGodStatusDot : Control
{
    private Color _color = Color.White;

    public WFGodStatusDot()
    {
        MinSize = new Vector2(14, 14);
        VerticalAlignment = VAlignment.Center;
    }

    public Color Color
    {
        get => _color;
        set => _color = value;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        handle.DrawCircle(PixelSize / 2f, 4f * UIScale, _color);
    }
}
