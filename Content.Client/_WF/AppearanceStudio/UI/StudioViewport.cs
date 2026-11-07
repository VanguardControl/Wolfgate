using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._WF.AppearanceStudio.UI;

/// <summary>The studio's view of the doll: a sprite view the selected part can be dragged around in, a pixel at a time.</summary>
public sealed class StudioViewport : PanelContainer
{
    public readonly SpriteView View;

    private bool _dragging;
    private Vector2 _carried;

    /// <summary>Raised for each whole sprite pixel dragged: +X right, +Y up.</summary>
    public event Action<Vector2i>? Dragged;

    /// <summary>Raised when a drag ends, to commit what it moved.</summary>
    public event Action? DragEnded;

    public StudioViewport()
    {
        MouseFilter = MouseFilterMode.Stop;
        View = new SpriteView
        {
            Scale = new Vector2(8, 8),
            Stretch = SpriteView.StretchMode.None,
            MouseFilter = MouseFilterMode.Ignore,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        AddChild(View);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _dragging = true;
        _carried = Vector2.Zero;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick || !_dragging)
            return;

        _dragging = false;
        DragEnded?.Invoke();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (!_dragging)
            return;

        // One sprite pixel is Scale real pixels on screen.
        _carried += args.Relative / (View.Scale.X * UIScale);
        var step = new Vector2i((int) MathF.Truncate(_carried.X), (int) MathF.Truncate(_carried.Y));
        if (step == Vector2i.Zero)
            return;

        _carried -= step;
        Dragged?.Invoke(new Vector2i(step.X, -step.Y));
    }
}
