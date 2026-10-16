using System.Numerics;
using Content.Shared._WF.RoundSetup;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

/// <summary>A compact mouse-transparent compass pointing to the current crew leader.</summary>
public sealed class WFCrewLocatorHud : PanelContainer
{
    private readonly CrewArrow _arrow = new();
    private readonly Label _name = new() { ClipText = true, HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
    private readonly Label _status = new() { ClipText = true, HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };

    public WFCrewLocatorHud()
    {
        Name = "CrewLocator";
        SetWidth = 280;
        MouseFilter = MouseFilterMode.Ignore;
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#10232BCC"), BorderColor = Color.FromHex("#70DCC8"),
            BorderThickness = new Thickness(1), ContentMarginLeftOverride = 10, ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 6, ContentMarginBottomOverride = 6,
        };
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10, MouseFilter = MouseFilterMode.Ignore };
        row.AddChild(_arrow);
        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        text.AddChild(_name);
        text.AddChild(_status);
        row.AddChild(text);
        AddChild(row);
    }

    /// <summary>Updates existing controls without changing focus or rebuilding the HUD.</summary>
    public void Refresh(WFCrewSignalEvent signal, Vector2? direction)
    {
        _name.Text = Loc.GetString("wf-round-setup-locator-leader", ("name", signal.Leader));
        _name.ToolTip = _name.Text;
        var key = signal.Status switch
        {
            WFCrewLeaderStatus.Self => "self",
            WFCrewLeaderStatus.Down => "down",
            WFCrewLeaderStatus.Elsewhere => "elsewhere",
            WFCrewLeaderStatus.Tracking => "distance",
            _ => "unavailable",
        };
        _status.Text = Loc.GetString("wf-round-setup-locator-" + key, ("distance", (int) MathF.Round(direction?.Length() ?? 0)));
        _status.ToolTip = Loc.GetString("wf-round-setup-locator-channel", ("crew", signal.Crew));
        _arrow.Direction = direction;
        _arrow.Modulate = signal.Status == WFCrewLeaderStatus.Down ? Color.FromHex("#FF786C") : Color.FromHex("#70DCC8");
    }

    private sealed class CrewArrow : Control
    {
        public Vector2? Direction;

        public CrewArrow()
        {
            SetSize = new Vector2(38, 38);
            VerticalAlignment = VAlignment.Center;
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var center = PixelSize / 2;
            handle.DrawCircle(center, 16 * UIScale, Color.White.WithAlpha(0.35f), false);
            if (Direction is not { } direction || direction.LengthSquared() < 4)
            {
                handle.DrawCircle(center, 4 * UIScale, Color.White);
                return;
            }
            var forward = Vector2.Normalize(direction);
            var side = new Vector2(-forward.Y, forward.X);
            var points = new[] { center + forward * 16 * UIScale,
                center - forward * 8 * UIScale + side * 8 * UIScale,
                center - forward * 3 * UIScale,
                center - forward * 8 * UIScale - side * 8 * UIScale };
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, points, Color.White);
        }
    }
}
