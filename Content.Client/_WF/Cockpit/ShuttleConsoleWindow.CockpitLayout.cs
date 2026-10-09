using System.Linq;
using Content.Client._WF.Cockpit;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>Shares the console's existing command bindings with the cockpit presentation.</summary>
    public WFCockpitParts WfBuildCockpit(WFCockpitLease lease)
    {
        var (navigation, translation) = NavContainer.WfCockpitInstruments(lease);
        var instruments = Column(navigation, ShipContainer.WfCockpitHull());
        var announcements = ShipContainer.WfCockpitAnnouncements(lease);
        var ship = ShipContainer.WfCockpitShip(lease);
        var shield = _shieldScreen.WfCockpitShield(lease);
        var shieldDetails = _shieldScreen.WfCockpitShieldDetails(lease);
        foreach (var plot in WFCockpitLease.Descendants(AccessContainer).OfType<MapGridControl>())
            plot.WfCockpitInteraction(lease);
        lease.Take(AccessContainer);
        var accessChildren = AccessContainer.Children.ToArray();
        lease.Clear(AccessContainer);
        AccessContainer.Orientation = BoxContainer.LayoutOrientation.Vertical;
        AccessContainer.AddChild(WFCockpitMfdLayout.Split(lease.Take(accessChildren[0]), lease.Take(accessChildren[1])));
        var systems = WFCockpitMfdLayout.Details(NavContainer.WfCockpitSystems(lease));
        var pages = new (string Key, Control Content)[]
        {
            ("wf-cockpit-nav", NavContainer.WfCockpitRadar(lease)),
            ("wf-cockpit-ship", ship),
            ("wf-cockpit-map", MapContainer.WfCockpitMap(lease)),
            ("wf-cockpit-dock", DockContainer.WfCockpitDock(lease)),
            ("wf-cockpit-access", AccessContainer),
            ("wf-cockpit-shield-page", shieldDetails),
            ("wf-cockpit-systems", systems),
            ("wf-cockpit-alarms", announcements),
        };
        return new WFCockpitParts(CameraBar.WfCockpitControls(lease), Column(lease.Take(CollisionBanner), lease.Take(CaptureBanner)),
            instruments, translation, NavContainer.WfCockpitFlight(lease), shield, pages);
    }

    /// <summary>Starts strategic data when its MFD page is selected.</summary>
    public void WfCockpitPageSelected(string key)
    {
        if (key == "wf-cockpit-map")
            MapContainer.Startup();
    }

    /// <summary>Refreshes normal navigation telemetry independently of the hidden console window.</summary>
    public void WfCockpitRefresh() => NavContainer.WfCockpitRefresh();
}
