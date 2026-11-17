using System.Numerics;
using Content.Client._WF.LockOn;
using Content.Client.Shuttles.UI;
using Content.Shared._WF.LockOn;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private WFLockOnOverlay? _wfLockOn;
    private ShuttleNavControl? _wfLockRadar;
    private IInputManager? _wfLockInput;
    private IUserInterfaceManager? _wfLockUi;

    /// <summary>Adds the lock overlay to the helm radar and routes lock clicks to the server.</summary>
    private void WfLockOnOpen()
    {
        if (_window == null)
            return;

        _wfLockRadar = _window.FindControl<NavScreen>("NavContainer")?.FindControl<ShuttleNavControl>("NavRadar");
        if (_wfLockRadar == null)
            return;

        _wfLockOn = new WFLockOnOverlay(Owner, _wfLockRadar, static () => Array.Empty<NetEntity>());
        _wfLockRadar.AddChild(_wfLockOn);
        LayoutContainer.SetAnchorPreset(_wfLockOn, LayoutContainer.LayoutPreset.Wide);
        _wfLockRadar.OnRadarClick += WfLockOnClick;
        _wfLockUi = IoCManager.Resolve<IUserInterfaceManager>();
        _wfLockInput = IoCManager.Resolve<IInputManager>();
        _wfLockInput.FirstChanceOnKeyEvent += WfLockOnKeyEvent;
        _window.OnClose += WfLockOnClose;
    }

    /// <summary>Releases the input hook when the helm window closes.</summary>
    private void WfLockOnClose()
    {
        if (_wfLockInput != null)
            _wfLockInput.FirstChanceOnKeyEvent -= WfLockOnKeyEvent;
        _wfLockInput = null;
        _wfLockUi = null;
        _wfLockRadar?.OnRadarClick -= WfLockOnClick;
        _wfLockOn?.Orphan();
        _wfLockOn = null;
        _wfLockRadar = null;
    }

    /// <summary>Middle click always locks; the helm has no guns, so left click locks on release.</summary>
    private void WfLockOnKeyEvent(KeyEventArgs args, KeyEventType type)
    {
        if (args.Key != Keyboard.Key.MouseMiddle || type != KeyEventType.Down || args.Handled)
            return;

        if (!WfLockOnHovered() || _wfLockUi?.KeyboardFocused != null)
            return;

        WfLockOnSend(_wfLockUi!.MousePositionScaled.Position - _wfLockRadar!.GlobalPosition);
        args.Handle();
    }

    private bool WfLockOnHovered()
    {
        if (_wfLockRadar == null || !_wfLockRadar.VisibleInTree)
            return false;

        var pointer = _wfLockUi!.MousePositionScaled;
        return pointer.IsValid &&
               pointer.Window == _wfLockRadar.Window?.Id &&
               _wfLockUi.MouseGetControl(pointer) == _wfLockRadar;
    }

    private void WfLockOnClick(EntityCoordinates coordinates)
    {
        if (_wfLockRadar == null || _wfLockRadar.WfLockOnMouseDown())
            return;

        SendMessage(new WFLockOnRequestMessage(EntMan.GetNetCoordinates(coordinates)));
    }

    private void WfLockOnSend(Vector2 relativePosition)
    {
        if (_wfLockRadar == null)
            return;

        var coordinates = _wfLockRadar.WfLockOnCoordinates(relativePosition);
        if (!coordinates.IsValid(EntMan))
            return;

        SendMessage(new WFLockOnRequestMessage(EntMan.GetNetCoordinates(coordinates)));
    }
}
