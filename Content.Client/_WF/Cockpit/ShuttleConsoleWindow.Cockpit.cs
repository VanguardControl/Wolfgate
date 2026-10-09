using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private Button? _wfCockpitButton;
    /// <summary>The BUI owner whose piloting session supplies the cockpit.</summary>
    public EntityUid? WfCockpitConsole { get; private set; }
    /// <summary>The console is currently presented around the world view.</summary>
    public bool WfCockpitActive { get; private set; }

    private Button WfCreateCockpitButton()
    {
        _wfCockpitButton = WFInstrumentTheme.Button("wf-cockpit-enter");
        _wfCockpitButton.HorizontalExpand = false;
        _wfCockpitButton.SetWidth = 170;
        _wfCockpitButton.Disabled = true;
        _wfCockpitButton.ToolTip = Loc.GetString("wf-cockpit-seat-required");
        _wfCockpitButton.OnPressed += _ => UserInterfaceManager.GetUIController<WFCockpitUIController>().Enter(this);
        OnClose += () => UserInterfaceManager.GetUIController<WFCockpitUIController>().Exit(this);
        return _wfCockpitButton;
    }

    /// <summary>Associates the entry switch with the authoritative helm session.</summary>
    public void WfSetCockpitConsole(EntityUid console) => WfCockpitConsole = console;

    /// <summary>Keeps hull telemetry subscribed while the cockpit's permanent gauges use it.</summary>
    public void WfSetCockpitActive(bool active)
    {
        if (active && !WfCockpitActive)
            CameraBar.WfCockpitDefaultView();
        WfCockpitActive = active;
        CameraBar.Visible = active || NavContainer.Visible;
        ShipStatusActiveChanged?.Invoke(active || _mode == ShuttleConsoleMode.Ship, ShipContainer.Overlays);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_wfCockpitButton != null)
            _wfCockpitButton.Disabled = !UserInterfaceManager.GetUIController<WFCockpitUIController>().CanEnter(WfCockpitConsole);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            UserInterfaceManager.GetUIController<WFCockpitUIController>().Exit(this);
        base.Dispose(disposing);
    }
}
