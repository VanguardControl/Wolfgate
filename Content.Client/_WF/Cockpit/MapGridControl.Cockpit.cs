using System.Numerics;
using Content.Client._WF.Cockpit;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client.UserInterface.Controls;

public partial class MapGridControl
{
    [Dependency] private IInputManager _wfCockpitInput = default!;

    private bool _wfCockpitPanDown;
    private bool _wfCockpitPanning;
    private Vector2? _wfCockpitMouse;

    /// <summary>Uses shared cockpit panning and compact map annotations.</summary>
    public bool WfCockpitControls { get; private set; }

    /// <summary>Enables middle-mouse panning until the borrowed map returns to its console.</summary>
    public void WfCockpitInteraction(WFCockpitLease lease)
    {
        var enabled = WfCockpitControls;
        WfCockpitControls = true;
        _draggin = false;
        WfEndCockpitPan();
        lease.Remember(() =>
        {
            WfCockpitControls = enabled;
            _draggin = false;
            WfEndCockpitPan();
        });
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        WfUpdateCockpitPan();
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (WfCockpitControls)
            WfEndCockpitPan();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (WfCockpitControls)
            WfEndCockpitPan();
    }

    private void WfUpdateCockpitPan()
    {
        if (!WfCockpitControls)
            return;

        var mouse = _wfCockpitInput.MouseScreenPosition;
        var down = _wfCockpitInput.IsKeyDown(Keyboard.Key.MouseMiddle);
        var pressed = down && !_wfCockpitPanDown;
        _wfCockpitPanDown = down;
        if (!VisibleInTree || !DisplayManager.IsFocused || !down)
        {
            _wfCockpitPanning = false;
            _wfCockpitMouse = null;
            return;
        }

        if (pressed && mouse.IsValid &&
            !_wfCockpitInput.IsKeyDown(Keyboard.Key.Shift) &&
            !_wfCockpitInput.IsKeyDown(Keyboard.Key.Control) &&
            !_wfCockpitInput.IsKeyDown(Keyboard.Key.Alt))
        {
            for (var hovered = UserInterfaceManager.MouseGetControl(mouse); hovered != null; hovered = hovered.Parent)
            {
                if (hovered != this)
                    continue;
                _wfCockpitPanning = true;
                _wfCockpitMouse = mouse.Position;
                break;
            }
        }

        if (!_wfCockpitPanning)
            return;
        if (!mouse.IsValid || mouse.Window != Window?.Id || MinimapScale <= 0f)
        {
            _wfCockpitMouse = null;
            return;
        }

        if (_wfCockpitMouse is { } last)
        {
            var delta = mouse.Position - last;
            if (delta != Vector2.Zero)
            {
                Recentering = false;
                Offset -= new Vector2(delta.X, -delta.Y) / MinimapScale;
                WfCockpitPanMoved();
            }
        }
        _wfCockpitMouse = mouse.Position;
    }

    /// <summary>Updates map-specific tracking after a middle-mouse pan.</summary>
    protected virtual void WfCockpitPanMoved()
    {
    }

    private void WfEndCockpitPan()
    {
        _wfCockpitPanDown = _wfCockpitInput.IsKeyDown(Keyboard.Key.MouseMiddle);
        _wfCockpitPanning = false;
        _wfCockpitMouse = null;
    }
}
