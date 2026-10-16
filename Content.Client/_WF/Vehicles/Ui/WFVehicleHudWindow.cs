using System.Numerics;
using Content.Client._WF.UserInterface.WindowPopout;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>A borderless instrument window with a draggable header and the standard native popout host.</summary>
public sealed class WFVehicleHudWindow : BaseWindow
{
    public bool PositionedByUser { get; private set; }
    public WolfgatePopoutButton Popout { get; } = new();

    public WFVehicleHudWindow()
    {
        Name = "WFVehicleHud";
        Resizable = false;
        SetWidth = 760;
        Popout.OnPressed += _ => UserInterfaceManager.GetUIController<WindowPopoutUIController>().Toggle(this);
    }

    protected override DragMode GetDragModeFor(Vector2 position)
    {
        if (UserInterfaceManager.GetUIController<WindowPopoutUIController>().IsPoppedOut(this) || position.Y > 24)
            return DragMode.None;
        PositionedByUser = true;
        return DragMode.Move;
    }

    public void RefreshPopout()
    {
        Popout.PoppedOut = UserInterfaceManager.GetUIController<WindowPopoutUIController>().IsPoppedOut(this);
    }
}
