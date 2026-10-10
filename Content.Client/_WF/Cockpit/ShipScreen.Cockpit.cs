using System.Linq;
using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Shuttles.UI;

public sealed partial class ShipScreen
{
    /// <summary>Leaves a permanent hull integrity strip beside the flight instruments.</summary>
    public Control WfCockpitHull() => new WFGlassGauge("wf-cockpit-hull", () => WFGaugeReading.Number(
        _wfHull?.HullIntegrity * 100, 0, 100, "wf-gauge-unit-percent", decimals: 1,
        tint: _wfHull?.HullIntegrity < 0.5f ? Red : Green), true) { SetHeight = 56 };

    /// <summary>Uses extra MFD width for telemetry and overlays beside the live hull display.</summary>
    public Control WfCockpitShip(WFCockpitLease lease)
    {
        var departments = WFCockpitLease.Descendants(this).OfType<CheckBox>().Single(toggle => toggle.Name == "DepartmentToggle");
        var gauges = WFCockpitLease.Descendants(this).OfType<WFGlassGauge>().ToArray();
        lease.Take(this);
        lease.Clear(this);
        var telemetry = new GridContainer { Name = "CockpitShipTelemetry", Columns = 2, HorizontalExpand = true };
        foreach (var gauge in gauges)
        {
            lease.Take(gauge);
            gauge.MinWidth = 88;
            WFCockpitInstrumentSizing.Bind(gauge, lease, 100, 160);
            telemetry.AddChild(gauge);
        }
        Orientation = LayoutOrientation.Vertical;
        ShipView.WfCockpitInteraction(lease);
        var plot = Scope("wf-console-hull-scope", lease.Take(ShipView), Vector2.Zero);
        var overlays = new GridContainer { Name = "CockpitShipOverlays", Columns = 4, HorizontalExpand = true };
        foreach (var control in new Control[] { DamageToggle, FireToggle, PressureToggle, PowerToggle })
            overlays.AddChild(lease.Take(control));
        var mapControls = Row(lease.Take(departments), lease.Take(FitButton));
        mapControls.Name = "CockpitShipMapControls";
        var details = Column(new WFGlassReadout(lease.Take(ShipNameLabel)), telemetry, overlays, mapControls, lease.Take(TruncatedLabel));
        AddChild(new WFCockpitShipLayout(plot, WFCockpitMfdLayout.Details(details)));
        return this;
    }

    /// <summary>Moves announcements and alarm controls into their own MFD page.</summary>
    public Control WfCockpitAnnouncements(WFCockpitLease lease) => WFCockpitMfdLayout.Details(lease.Take(AlarmPanel));
}
