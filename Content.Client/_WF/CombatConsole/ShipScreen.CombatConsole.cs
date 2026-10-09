using Robust.Client.UserInterface.Controls;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.Shuttles;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Shuttles.UI;

public sealed partial class ShipScreen
{
    private ShipStatusSummary? _wfHull;
    private int? _wfTileCount;
    /// <summary>Frames the hull map beside its telemetry and overlay controls.</summary>
    public void WfRefitInstruments()
    {
        var mapControls = ShipView.WfDetachInstrumentToolbar();
        var plot = Column(Scope("wf-console-hull-scope", ShipView), NoDataLabel);
        plot.HorizontalExpand = plot.VerticalExpand = true;
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
        var hull = Column(new WFGlassReadout(ShipNameLabel), TruncatedLabel,
            Row(new WFGlassGauge("wf-gauge-hull", () => WFGaugeReading.Number(_wfHull?.WorstIntegrity * 100, 0, 100,
                    "wf-gauge-unit-percent", tint: _wfHull?.WorstIntegrity < 0.5f ? Red : Green)),
                new WFGlassGauge("wf-gauge-tiles", () => WFGaugeReading.Number(_wfTileCount, 0,
                    WFGaugeScale.Ceiling(_wfTileCount ?? 0, 100), "wf-gauge-unit-tiles"))),
            Row(WfHullCount("wf-gauge-damaged", () => _wfHull?.DamagedTiles), WfHullCount("wf-gauge-fire", () => _wfHull?.FireTiles)),
            Row(WfHullCount("wf-gauge-vented", () => _wfHull?.VentedTiles), WfHullCount("wf-gauge-power-loss", () => _wfHull?.UnpoweredTiles)), telemetry);
        var side = Column(Panel("wf-console-hull-status", hull),
            Panel("wf-console-hull-overlays", Column(DamageToggle, FireToggle, PressureToggle, PowerToggle,
                mapControls, FitButton)), Panel("wf-console-hull-announcements", AlarmPanel));
        side.MinWidth = side.MaxWidth = 300;
        var sideScroll = Scroll(side);
        sideScroll.SetWidth = 300;
        sideScroll.HorizontalExpand = false;
        DisposeAllChildren();
        VerticalExpand = true;
        SeparationOverride = 8;
        AddChild(plot);
        AddChild(sideScroll);
    }

    private WFGlassGauge WfHullCount(string caption, Func<int?> read) => new(caption, () =>
    {
        var count = read();
        return WFGaugeReading.Number(count, 0, Math.Max(1, _wfTileCount ?? WFGaugeScale.Ceiling(count ?? 0)),
            "wf-gauge-unit-tiles", tint: count > 0 ? Red : Green);
    });
}
