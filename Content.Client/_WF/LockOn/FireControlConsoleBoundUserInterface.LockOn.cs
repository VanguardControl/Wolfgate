using System.Numerics;
using Content.Client._WF.LockOn;
using Content.Shared._WF.LockOn;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlConsoleBoundUserInterface
{
    private WFLockOnOverlay? _wfLockOn;
    private IInputManager? _wfLockInput;
    private IUserInterfaceManager? _wfLockUi;

    /// <summary>Adds the lock overlay to the gunnery radar and routes lock clicks to the server.</summary>
    private void WfLockOnOpen()
    {
        if (_window == null)
            return;

        _wfLockOn = new WFLockOnOverlay(Owner, _window.Radar, _window.WfLockOnWeapons);
        _window.Radar.AddChild(_wfLockOn);
        LayoutContainer.SetAnchorPreset(_wfLockOn, LayoutContainer.LayoutPreset.Wide);
        _window.Radar.OnRadarClick += WfLockOnClick;
        _wfLockUi = IoCManager.Resolve<IUserInterfaceManager>();
        _wfLockInput = IoCManager.Resolve<IInputManager>();
        _wfLockInput.FirstChanceOnKeyEvent += WfLockOnKeyEvent;
        _window.OnClose += WfLockOnClose;
    }

    /// <summary>Releases the input hook when the gunnery window closes.</summary>
    private void WfLockOnClose()
    {
        if (_wfLockInput != null)
            _wfLockInput.FirstChanceOnKeyEvent -= WfLockOnKeyEvent;
        _wfLockInput = null;
        _wfLockUi = null;
        if (_window != null)
            _window.Radar.OnRadarClick -= WfLockOnClick;
        _wfLockOn?.Orphan();
        _wfLockOn = null;
    }

    /// <summary>Middle click always locks; with no weapon selected, left click locks on release instead of firing.</summary>
    private void WfLockOnKeyEvent(KeyEventArgs args, KeyEventType type)
    {
        if (args.Key != Keyboard.Key.MouseMiddle || type != KeyEventType.Down || args.Handled || _window == null)
            return;

        if (!WfLockOnHovered() || _wfLockUi?.KeyboardFocused != null)
            return;

        WfLockOnSend(_wfLockUi!.MousePositionScaled.Position - _window.Radar.GlobalPosition);
        args.Handle();
    }

    private bool WfLockOnHovered()
    {
        if (_window == null || !_window.Radar.VisibleInTree)
            return false;

        var pointer = _wfLockUi!.MousePositionScaled;
        return pointer.IsValid &&
               pointer.Window == _window.Radar.Window?.Id &&
               _wfLockUi.MouseGetControl(pointer) == _window.Radar;
    }

    private void WfLockOnClick(EntityCoordinates coordinates)
    {
        if (_window == null || _window.Radar.WfLockOnMouseDown() || _window.WfLockOnHasSelection())
            return;

        SendMessage(new WFLockOnRequestMessage(EntMan.GetNetCoordinates(coordinates)));
    }

    private void WfLockOnSend(Vector2 relativePosition)
    {
        if (_window == null)
            return;

        var coordinates = _window.Radar.WfLockOnCoordinates(relativePosition);
        if (!coordinates.IsValid(EntMan))
            return;

        SendMessage(new WFLockOnRequestMessage(EntMan.GetNetCoordinates(coordinates)));
    }
}
