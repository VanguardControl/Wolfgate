using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleDockControl
{
    private BoxContainer? _wfCockpitDockActions;
    private readonly Dictionary<DockingPortState, int> _wfCockpitDockNumbers = new();
    private readonly List<UIBox2> _wfCockpitDockLabels = new();

    /// <summary>Translates the dock drawing while retaining its real docking coordinates.</summary>
    private Vector2 WfCockpitDockPan => WfCockpitControls
        ? new Vector2(Offset.X, -Offset.Y) * MinimapScale
        : Vector2.Zero;

    /// <summary>Places existing port actions below the plot, retaining their docking checks and events.</summary>
    public Control WfCockpitDockActions(WFCockpitLease lease)
    {
        var actions = new BoxContainer
        {
            Name = "CockpitDockPortActions",
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        _wfCockpitDockActions = actions;
        BuildDocks(GridEntity);
        lease.Remember(() =>
        {
            _wfCockpitDockActions = null;
            Offset = Vector2.Zero;
            BuildDocks(GridEntity);
        });
        return actions;
    }

    private void WfBeginCockpitDockDraw()
    {
        _wfCockpitDockLabels.Clear();
    }

    private bool WfBuildCockpitDockRow(DockingPortState dock, Button action, PanelContainer original)
    {
        if (_wfCockpitDockActions == null)
            return false;

        action.Parent!.RemoveChild(action);
        original.Dispose();
        var number = _wfCockpitDockNumbers.Count + 1;
        _wfCockpitDockNumbers.Add(dock, number);
        var name = dock.LabelName ?? dock.Name;
        var caption = new Label
        {
            Text = Loc.GetString("wf-cockpit-dock-port", ("number", number), ("name", name)),
            ClipText = true,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        action.Name = "CockpitDockAction";
        action.SetWidth = action.MinWidth = 110;
        action.HorizontalExpand = false;
        action.HorizontalAlignment = HAlignment.Stretch;
        action.AddStyleClass("WfCompact");
        var row = WFInstrumentTheme.Row(caption, action);
        // Labels and containers ignore the mouse by default, so the row takes it for the tooltip and hover.
        row.MouseFilter = MouseFilterMode.Pass;
        row.ToolTip = name;
        row.Visible = false;
        Action<GUIMouseHoverEventArgs> highlight = _ => HighlightedDock = dock.Entity;
        Action<GUIMouseHoverEventArgs> clear = _ => HighlightedDock = null;
        row.OnMouseEntered += highlight;
        row.OnMouseExited += clear;
        action.OnMouseEntered += highlight;
        action.OnMouseExited += clear;
        _wfCockpitDockActions.AddChild(row);
        _dockContainers[dock] = row;
        WFInstrumentTheme.Apply(row);
        return true;
    }

    private bool WfDrawCockpitDockMarker(DrawingHandleScreen handle, DockingPortState dock,
        bool visible, Matrix3x2 gridToView)
    {
        if (_wfCockpitDockActions == null)
            return false;

        _dockContainers[dock].Visible = visible;
        if (!visible)
            return true;

        var position = Vector2.Transform(dock.Coordinates.Position, gridToView);
        var text = _wfCockpitDockNumbers[dock].ToString();
        var scale = UIScale * 0.85f;
        var dimensions = handle.GetDimensions(Font, text, scale);
        var markerSize = Vector2.Max(new Vector2(20, 18) * UIScale, dimensions + new Vector2(6, 4) * UIScale);
        var marker = WFDockMarkerLayout.Place(position, markerSize, PixelSize, _wfCockpitDockLabels);
        if (marker is not { } bounds)
            return true;
        _wfCockpitDockLabels.Add(bounds);
        var color = HighlightedDock == dock.Entity ? WFInstrumentTheme.Cream : WFInstrumentTheme.Accent;
        handle.DrawLine(position, bounds.Center, color.WithAlpha(0.65f));
        handle.DrawRect(bounds, WFInstrumentTheme.Ink.WithAlpha(0.95f));
        handle.DrawRect(bounds, color, false);
        handle.DrawString(Font, bounds.Center - dimensions / 2, text, scale, color);
        return true;
    }
}
