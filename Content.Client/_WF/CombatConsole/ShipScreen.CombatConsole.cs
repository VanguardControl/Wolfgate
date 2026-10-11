using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.Shuttles;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Shuttles.UI;

public sealed partial class ShipScreen
{
    private ShipStatusSummary? _wfHull;
    private int? _wfTileCount;
    /// <summary>Spreads hull telemetry and ship controls beside a narrower, independently interactive plot.</summary>
    public void WfRefitInstruments()
    {
        var mapControls = ShipView.WfDetachInstrumentToolbar();
        var departments = (CheckBox) mapControls.GetChild(0);
        var overlayButtons = new[] { DamageToggle, FireToggle, PressureToggle, PowerToggle, departments };
        foreach (var button in overlayButtons)
        {
            button.Margin = new Thickness(0);
            button.AddStyleClass("WfCompact");
            button.HorizontalExpand = true;
        }
        departments.HorizontalExpand = false;
        departments.SetWidth = 170;
        FitButton.AddStyleClass("WfCompact");
        FitButton.Margin = new Thickness(0);
        FitButton.HorizontalExpand = false;
        FitButton.SetWidth = 100;
        FitButton.MinHeight = 32;
        var controls = new WFInstrumentPanel { Name = "WfHullControls", HorizontalExpand = true };
        controls.AddChild(Row(DamageToggle, FireToggle, PressureToggle, PowerToggle, departments, FitButton));
        var plot = Column(Scope("wf-console-hull-scope", ShipView, new Vector2(208, 150)), mapControls, NoDataLabel);
        plot.Name = "WfHullPlot";
        plot.HorizontalExpand = plot.VerticalExpand = true;
        plot.SizeFlagsStretchRatio = 0.3f;
        NoDataLabel.HorizontalAlignment = HAlignment.Center;
        NoDataLabel.VerticalExpand = false;
        var telemetry = ShipNameLabel.Parent!;
        telemetry.Margin = new Thickness(0);
        foreach (var control in telemetry.Children)
        {
            if (control is Label { HorizontalExpand: true } value)
                value.ClipText = true;
        }
        telemetry.Visible = false;
        var gauges = new GridContainer { Columns = 3, HorizontalExpand = true, VerticalExpand = true };
        foreach (var gauge in new[]
        {
            new WFGlassGauge("wf-gauge-hull", () => WFGaugeReading.Number(_wfHull?.WorstIntegrity * 100, 0, 100,
                "wf-gauge-unit-percent", tint: _wfHull?.WorstIntegrity < 0.5f ? Red : Green)),
            new WFGlassGauge("wf-gauge-tiles", () => WFGaugeReading.Number(_wfTileCount, 0,
                WFGaugeScale.Ceiling(_wfTileCount ?? 0, 100), "wf-gauge-unit-tiles")),
            WfHullCount("wf-gauge-damaged", () => _wfHull?.DamagedTiles),
            WfHullCount("wf-gauge-fire", () => _wfHull?.FireTiles),
            WfHullCount("wf-gauge-vented", () => _wfHull?.VentedTiles),
            WfHullCount("wf-gauge-power-loss", () => _wfHull?.UnpoweredTiles),
        })
        {
            gauge.SetHeight = float.NaN;
            gauge.MinHeight = 110;
            gauge.VerticalExpand = true;
            gauges.AddChild(gauge);
        }
        var hull = Column(new WFGlassReadout(ShipNameLabel), TruncatedLabel, gauges, telemetry);
        hull.VerticalExpand = true;
        var status = Panel("wf-console-hull-status", hull, true);
        status.Name = "WfHullStatus";
        status.SizeFlagsStretchRatio = 0.36f;
        AlarmPanel.WfRefitInstruments();
        var announcements = Panel("wf-console-hull-announcements", AlarmPanel, true);
        announcements.Name = "WfHullAnnouncements";
        announcements.SizeFlagsStretchRatio = 0.34f;
        var workspace = Row(plot, status, announcements);
        workspace.VerticalExpand = true;
        workspace.SeparationOverride = 8;
        DisposeAllChildren();
        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;
        SeparationOverride = 8;
        AddChild(controls);
        AddChild(workspace);
    }

    private WFGlassGauge WfHullCount(string caption, Func<int?> read) => new(caption, () =>
    {
        var count = read();
        return WFGaugeReading.Number(count, 0, Math.Max(1, _wfTileCount ?? WFGaugeScale.Ceiling(count ?? 0)),
            "wf-gauge-unit-tiles", tint: count > 0 ? Red : Green);
    });
}
