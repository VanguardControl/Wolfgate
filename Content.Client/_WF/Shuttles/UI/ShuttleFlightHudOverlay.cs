using System.Numerics;
using Content.Shared._WF.Shuttles;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Physics.Components;

namespace Content.Client._WF.Shuttles.UI;

/// <summary>
/// Flight readouts in a corner of the pilot's external view: a dial with the ship's velocity as the
/// screen shows it, and its speed, heading, drift and turn rate.
/// </summary>
public sealed partial class ShuttleFlightHudOverlay : Overlay
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    /// <summary>
    /// How far in from the view's left edge the dial's centre sits, in UI pixels. It rides half way
    /// down that edge, clear of the menu and action bars above and the inventory below.
    /// </summary>
    private const float DialInset = 84f;

    private const float DialRadius = 48f;
    private const float TickLength = 6f;
    private const float ArrowHead = 7f;
    private const float LineHeight = 15f;
    private const int FontSize = 10;

    /// <summary>
    /// Speed at which the dial's needle reaches half way out. It never quite reaches the rim.
    /// </summary>
    private const float HalfScaleSpeed = 25f;

    /// <summary>
    /// Below this the ship counts as stopped, and has no direction to speak of.
    /// </summary>
    private const float MinSpeed = 0.1f;

    private static readonly Color DialColor = Color.FromHex("#00ff2a").WithAlpha(0.55f);
    private static readonly Color BackColor = Color.Black.WithAlpha(0.35f);
    private static readonly Color NeedleColor = Color.FromHex("#ffd24a");
    private static readonly Color TextColor = Color.FromHex("#00ff2a");

    private readonly Font _font;
    private readonly Vector2[] _head = new Vector2[3];

    public ShuttleFlightHudOverlay()
    {
        IoCManager.InjectDependencies(this);

        _transform = _entManager.System<SharedTransformSystem>();
        _font = new VectorFont(_resCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), FontSize);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _entManager.TryGetComponent<ShuttleCameraComponent>(_player.LocalEntity, out var camera) &&
               camera.View == ShuttleCameraView.External &&
               camera.Grid != null &&
               _entManager.TryGetComponent<EyeComponent>(_player.LocalEntity, out var eye) &&
               ShuttleHullRoofOverlay.IsPilotPass(args.Viewport.Eye, eye.Eye, primaryOnly: true);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_entManager.TryGetComponent<ShuttleCameraComponent>(_player.LocalEntity, out var camera) ||
            camera.Grid is not { } gridUid ||
            !_entManager.TryGetComponent<PhysicsComponent>(gridUid, out var body))
        {
            return;
        }

        var handle = args.ScreenHandle;
        var scale = _uiManager.RootControl.UIScale;
        var view = VisibleBounds(args);
        var centre = new Vector2(view.Left + DialInset * scale, (view.Top + view.Bottom) / 2f);
        var radius = DialRadius * scale;

        var velocity = body.LinearVelocity;
        var speed = velocity.Length();
        var (position, rotation) = _transform.GetWorldPositionRotation(gridUid);

        handle.DrawCircle(centre, radius, BackColor);
        handle.DrawCircle(centre, radius, DialColor, false);

        for (var i = 0; i < 8; i++)
        {
            var spoke = new Angle(i * Math.PI / 4).ToVec();
            var length = (i % 2 == 0 ? TickLength : TickLength / 2f) * scale;
            handle.DrawLine(centre + spoke * (radius - length), centre + spoke * radius, DialColor);
        }

        if (speed > MinSpeed)
        {
            // Through the eye, so the needle points the way the ship is seen to slide across the screen.
            var along = _eyeManager.WorldToScreen(position + velocity / speed) - _eyeManager.WorldToScreen(position);

            if (along.LengthSquared() > 0.0001f)
            {
                along = Vector2.Normalize(along);
                var tip = centre + along * radius * (speed / (speed + HalfScaleSpeed));
                var side = new Vector2(-along.Y, along.X) * ArrowHead * scale / 2f;

                handle.DrawLine(centre, tip, NeedleColor);
                _head[0] = tip + along * ArrowHead * scale;
                _head[1] = tip + side;
                _head[2] = tip - side;
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _head, NeedleColor);
            }
        }

        // Bow is the grid's north. Compass bearings run clockwise, angles the other way.
        var heading = Wrap(-rotation.Degrees);
        var local = (-rotation).RotateVec(velocity);
        var drift = speed > MinSpeed ? MathHelper.RadiansToDegrees(MathF.Atan2(local.X, local.Y)) : 0f;
        var turn = -MathHelper.RadiansToDegrees(body.AngularVelocity);

        var text = centre + new Vector2(-radius, radius + 8f * scale);
        var line = new Vector2(0f, LineHeight * scale);

        handle.DrawString(_font, text, Loc.GetString("shuttle-camera-hud-speed", ("speed", $"{speed:0.0}")), scale, TextColor);
        handle.DrawString(_font, text + line, Loc.GetString("shuttle-camera-hud-heading", ("heading", $"{heading:000}")), scale, TextColor);
        handle.DrawString(_font, text + line * 2f, Loc.GetString("shuttle-camera-hud-drift", ("angle", $"{drift:+0;-0;0}")), scale, TextColor);
        handle.DrawString(_font, text + line * 3f, Loc.GetString("shuttle-camera-hud-turn", ("rate", $"{turn:+0.0;-0.0;0.0}")), scale, TextColor);
    }

    /// <summary>
    /// The part of the viewport's picture that is on the screen. The picture itself can run past the
    /// edges of the control it's shown in.
    /// </summary>
    public static UIBox2 VisibleBounds(in OverlayDrawArgs args)
    {
        UIBox2 bounds = args.ViewportBounds;

        if (args.ViewportControl is not Control control)
            return bounds;

        UIBox2 shown = control.GlobalPixelRect;

        return new UIBox2(
            Math.Max(bounds.Left, shown.Left),
            Math.Max(bounds.Top, shown.Top),
            Math.Min(bounds.Right, shown.Right),
            Math.Min(bounds.Bottom, shown.Bottom));
    }

    private static double Wrap(double degrees)
    {
        degrees %= 360d;
        return degrees < 0d ? degrees + 360d : degrees;
    }
}
