using Content.Client._WF.Cockpit;
using Content.Shared._WF.Shuttles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Shuttles.UI;

public sealed partial class ShuttleCameraBar
{
    /// <summary>Starts the cockpit outside the ship while keeping its zoom and low-light settings.</summary>
    public void WfCockpitDefaultView() => Request(ShuttleCameraView.External, _zoom, _lowLight);

    /// <summary>Fits permanent camera selection and zoom above the cockpit flight controls.</summary>
    public Control WfCockpitControls(WFCockpitLease lease)
    {
        lease.Take(this);
        lease.Clear(this);
        var views = new GridContainer { Columns = 3, HorizontalExpand = true };
        var captions = new[] { "wf-cockpit-camera-helm", "wf-cockpit-camera-front", "wf-cockpit-camera-rear",
            "wf-cockpit-camera-left", "wf-cockpit-camera-right", "wf-cockpit-camera-external" };
        for (var i = 0; i < _views.Length; i++)
            views.AddChild(Compact(_views[i].Button, captions[i]));
        var night = Compact(LowLightButton, "wf-cockpit-camera-night");
        night.SetWidth = 36;
        night.HorizontalExpand = false;
        var value = lease.Take(ZoomLabel);
        value.SetWidth = 44;
        value.HorizontalExpand = false;
        var controls = Column(views, Row(night, lease.Take(ZoomSlider), value));
        controls.SeparationOverride = 4;
        AddChild(controls);
        return this;

        Button Compact(Button button, string caption)
        {
            lease.Take(button);
            var text = button.Text;
            var tooltip = button.ToolTip;
            var alignment = button.Label.Align;
            var compact = button.HasStyleClass("WfCompact");
            lease.Remember(() =>
            {
                button.Text = text;
                button.ToolTip = tooltip;
                button.Label.Align = alignment;
                if (!compact)
                    button.RemoveStyleClass("WfCompact");
                Switch(button);
            });
            button.Text = Loc.GetString(caption);
            button.ToolTip = tooltip ?? text;
            button.AddStyleClass("WfCompact");
            return button;
        }
    }
}
