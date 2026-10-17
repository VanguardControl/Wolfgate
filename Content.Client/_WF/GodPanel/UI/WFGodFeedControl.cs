using System.Numerics;
using Content.Client.Eye;
using Content.Client.Viewport;
using Content.Shared._WF.GodPanel;
using Robust.Client.Graphics;
using Robust.Client.Timing;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WF.GodPanel.UI;

/// <summary>
/// One camera feed: the admin camera's eye in a viewport, with a title and buttons. A free camera pans by dragging.
/// </summary>
public sealed partial class WFGodFeedControl : PanelContainer
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IClientGameTiming _timing = default!;

    private const float MinimumZoom = 0.1f;
    private const float MaximumZoom = 4f;

    private static readonly TimeSpan MoveInterval = TimeSpan.FromMilliseconds(100);

    private readonly EyeLerpingSystem _eyeLerping;
    private readonly WFGodPanelSystem _system;
    private readonly FixedEye _defaultEye = new();
    private readonly ScalingViewport _viewport = new() { MouseFilter = MouseFilterMode.Ignore };
    private readonly Label _title = new() { ClipText = true, HorizontalExpand = true, StyleClasses = { "LabelKeyText" } };

    private EntityUid? _camera;
    private float _zoom = 1f;
    private Vector2 _dragTarget;
    private bool _dragging;
    private bool _moveDirty;
    private TimeSpan _nextMove;

    /// <summary>What the server told us about this feed.</summary>
    public readonly WFGodFeedInfo Info;

    /// <summary>The panel should select the feed's target.</summary>
    public event Action<NetEntity>? SelectRequested;

    /// <summary>The feed's close button was pressed.</summary>
    public event Action<int>? CloseRequested;

    public WFGodFeedControl(WFGodFeedInfo info)
    {
        IoCManager.InjectDependencies(this);
        Info = info;
        _eyeLerping = _entities.System<EyeLerpingSystem>();
        _system = _entities.System<WFGodPanelSystem>();
        PanelOverride = new StyleBoxFlat { BackgroundColor = WFGodPanelText.PanelColor };
        HorizontalExpand = true;
        Margin = new Thickness(2);

        _viewport.Eye = _defaultEye;
        _viewport.OnResized += OnViewportResized;

        var view = new FeedView { HorizontalExpand = true, VerticalExpand = true, MinSize = new Vector2(220, 150) };
        view.AddChild(_viewport);
        view.Wheel += OnWheel;
        view.DragStarted += OnDragStarted;
        view.Dragged += OnDragged;
        view.DragEnded += OnDragEnded;

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        if (info.Target is { } target)
        {
            buttons.AddChild(Button("wf-godpanel-feed-follow", () => _system.Act(WFGodAction.Follow, target)));
            buttons.AddChild(Button("wf-godpanel-feed-goto", () => _system.Act(WFGodAction.GoTo, target)));
            buttons.AddChild(Button("wf-godpanel-feed-select", () => SelectRequested?.Invoke(target)));
        }
        else
        {
            buttons.AddChild(new Label
            {
                Text = Loc.GetString("wf-godpanel-feed-drag-hint"),
                StyleClasses = { "LabelSubText" },
                VerticalAlignment = VAlignment.Center,
            });
        }

        buttons.AddChild(new Control { HorizontalExpand = true });
        buttons.AddChild(Button("wf-godpanel-feed-close", () => CloseRequested?.Invoke(info.Id)));

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(4) };
        box.AddChild(_title);
        box.AddChild(view);
        box.AddChild(buttons);
        AddChild(box);

        _title.Text = info.Target == null ? Loc.GetString("wf-godpanel-feed-free") : Loc.GetString("wf-godpanel-feed-loading");
    }

    private static Button Button(string loc, Action action)
    {
        var button = WFGodPanelText.SmallButton(Loc.GetString(loc));
        button.OnPressed += _ => action();
        return button;
    }

    /// <summary>Names the feed after what it shows.</summary>
    public void SetTitle(string title)
    {
        _title.Text = title;
    }

    private void OnViewportResized()
    {
        var width = Math.Max(_viewport.PixelWidth, (int) Math.Floor(_viewport.MinWidth));
        var height = Math.Max(_viewport.PixelHeight, (int) Math.Floor(_viewport.MinHeight));
        _viewport.ViewportSize = new Vector2i(Math.Max(1, width), Math.Max(1, height));
    }

    private void OnWheel(float delta)
    {
        if (_viewport.Eye == null)
            return;

        _zoom = Math.Clamp(_zoom - delta * 0.15f * _zoom, MinimumZoom, MaximumZoom);
        _viewport.Eye.Zoom = new Vector2(_zoom, _zoom);
    }

    private void OnDragStarted()
    {
        // The camera only reaches the client's position again after the server moves it, so the drag keeps its own.
        if (Info.Target != null || _camera is not { } camera || !_entities.TryGetComponent(camera, out TransformComponent? xform))
            return;

        _dragTarget = xform.LocalPosition;
        _dragging = true;
    }

    private void OnDragged(Vector2 relative)
    {
        if (!_dragging)
            return;

        // A world unit is PixelsPerMeter pixels at zoom 1, and a larger zoom shows more of the world.
        var perPixel = _zoom * UIScale / EyeManager.PixelsPerMeter;
        _dragTarget += new Vector2(-relative.X, relative.Y) * perPixel;
        _moveDirty = true;
    }

    private void OnDragEnded()
    {
        _dragging = false;
        Flush(true);
    }

    private void Flush(bool force)
    {
        if (!_moveDirty || _camera is not { } camera || !_entities.TryGetComponent(camera, out TransformComponent? xform))
            return;

        if (!force && _timing.RealTime < _nextMove)
            return;

        _moveDirty = false;
        _nextMove = _timing.RealTime + MoveInterval;
        _system.MoveFeed(Info.Id, new EntityCoordinates(xform.ParentUid, _dragTarget));
    }

    // EUIs and this panel's events arrive before the game state is applied, so the camera entity may not exist
    // on the client yet; wait for the tick the server reported.
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_dragging)
            Flush(false);

        if (_timing.LastRealTick < Info.Tick)
            return;

        if (!_entities.TryGetEntity(Info.Camera, out var cameraUid))
            return;

        if (_camera != cameraUid)
        {
            if (_camera is { } old)
                _eyeLerping.RemoveEye(old);

            _eyeLerping.AddEye(cameraUid.Value);
            _camera = cameraUid;
        }

        if (_entities.TryGetComponent<EyeComponent>(_camera, out var eye))
            _viewport.Eye = eye.Eye ?? _defaultEye;
    }

    /// <summary>Lets go of the camera's eye. Called when the feed is removed.</summary>
    public void Release()
    {
        if (_camera is { } camera && _entities.EntityExists(camera))
            _eyeLerping.RemoveEye(camera);

        _camera = null;
        _viewport.Eye = _defaultEye;
    }

    /// <summary>The box the camera is drawn in; it takes the mouse for zooming and panning.</summary>
    private sealed class FeedView : Control
    {
        private bool _held;

        public event Action<float>? Wheel;
        public event Action? DragStarted;
        public event Action<Vector2>? Dragged;
        public event Action? DragEnded;

        public FeedView()
        {
            MouseFilter = MouseFilterMode.Stop;
            RectClipContent = true;
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            _held = true;
            DragStarted?.Invoke();
            args.Handle();
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);
            if (args.Function != EngineKeyFunctions.UIClick || !_held)
                return;

            _held = false;
            DragEnded?.Invoke();
            args.Handle();
        }

        protected override void MouseMove(GUIMouseMoveEventArgs args)
        {
            base.MouseMove(args);
            if (_held)
                Dragged?.Invoke(args.Relative);
        }

        protected override void MouseWheel(GUIMouseWheelEventArgs args)
        {
            base.MouseWheel(args);
            Wheel?.Invoke(args.Delta.Y);
            args.Handle();
        }
    }
}
