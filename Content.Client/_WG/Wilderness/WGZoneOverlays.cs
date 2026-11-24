using System.Numerics;
using Content.Client.StatusIcon;
using Content.Shared._WG.Wilderness;
using Content.Shared.CCVar;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.Wilderness;

/// <summary>The part of a viewport that shows on screen, in the coordinates a screen-space overlay draws in.</summary>
internal static class WGZoneScreen
{
    // The engine resets the handle to the window's origin before each overlay, so ViewportBounds (window pixels) is
    // already in its coordinates, whether the view fills the window or sits in a console panel. A stretched view can
    // be drawn wider than its control, so it is clipped to the control.
    public static UIBox2 Of(in OverlayDrawArgs args)
    {
        var bounds = args.ViewportBounds;
        if (args.ViewportControl is not Control control)
            return bounds;

        return bounds.Intersection(UIBox2i.FromDimensions(control.GlobalPixelPosition, control.PixelSize)) ?? bounds;
    }
}

/// <summary>A faint, still coloured frame round the game view, fading inward.</summary>
public sealed class WGZoneBorderOverlay : Overlay
{
    private const int Bands = 8;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public Color Color = Color.Red;

    /// <summary>0 to 1, the player's border intensity; drawn only above zero.</summary>
    public float Alpha;

    protected override bool BeforeDraw(in OverlayDrawArgs args) => Alpha > 0.001f;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var box = WGZoneScreen.Of(args);
        var band = MathF.Max(2f, MathF.Min(box.Width, box.Height) * 0.006f);
        for (var i = 0; i < Bands; i++)
        {
            var inset = band * i;
            var fade = 1f - i / (float) Bands;
            var color = Color.WithAlpha(Alpha * 0.3f * fade * fade);
            var outer = new UIBox2(box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
            handle.DrawRect(new UIBox2(outer.Left, outer.Top, outer.Right, outer.Top + band), color);
            handle.DrawRect(new UIBox2(outer.Left, outer.Bottom - band, outer.Right, outer.Bottom), color);
            handle.DrawRect(new UIBox2(outer.Left, outer.Top + band, outer.Left + band, outer.Bottom - band), color);
            handle.DrawRect(new UIBox2(outer.Right - band, outer.Top + band, outer.Right, outer.Bottom - band), color);
        }
    }
}

/// <summary>
/// The skull over every marked person, centred above their sprite at the size of the other status icons. It honours
/// the status-icon settings and hides where they hide, as in a locker or under stealth.
/// </summary>
public sealed class WGSkullOverlay : Overlay
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly ProtoId<FactionIconPrototype> SkullIcon = "WGSkullIcon";

    private readonly SpriteSystem _sprite;
    private readonly TransformSystem _transform;
    private readonly StatusIconSystem _statusIcon;
    private readonly ShaderInstance _unshaded;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WGSkullOverlay()
    {
        IoCManager.InjectDependencies(this);
        _sprite = _entity.System<SpriteSystem>();
        _transform = _entity.System<TransformSystem>();
        _statusIcon = _entity.System<StatusIconSystem>();
        _unshaded = _proto.Index<ShaderPrototype>("unshaded").Instance();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _cfg.GetCVar(CCVars.LocalStatusIconsEnabled) && _cfg.GetCVar(CCVars.GlobalStatusIconsEnabled);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_proto.TryIndex(SkullIcon, out var icon))
            return;

        var handle = args.WorldHandle;
        var rotation = Matrix3Helpers.CreateRotation(-(args.Viewport.Eye?.Rotation ?? default));
        var texture = _sprite.GetFrame(icon.Icon, _timing.RealTime);
        var left = -texture.Width / 2f / EyeManager.PixelsPerMeter;

        handle.UseShader(_unshaded);
        var query = _entity.AllEntityQueryEnumerator<WGSkullComponent, SpriteComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite, out var xform, out var meta))
        {
            if (xform.MapID != args.MapId || !sprite.Visible || !_statusIcon.IsVisible((uid, meta), icon))
                continue;

            var bounds = _entity.GetComponentOrNull<StatusIconComponent>(uid)?.Bounds ?? sprite.Bounds;
            var world = _transform.GetWorldPosition(xform);
            if (!bounds.Translated(world).Intersects(args.WorldAABB))
                continue;

            // Turned with the eye, like the status icons, so it stays upright on screen.
            handle.SetTransform(Matrix3x2.Multiply(rotation, Matrix3Helpers.CreateTranslation(world)));
            handle.DrawTexture(texture, new Vector2(left, (bounds.Height + sprite.Offset.Y) / 2f));
        }

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }
}

/// <summary>A large title across the upper middle of the game view on crossing into or out of a zone.</summary>
public sealed class WGZoneTitleOverlay : Overlay
{
    [Dependency] private IResourceCache _cache = default!;

    private const int FontSize = 26;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Font _font;
    private readonly Font _subFont;

    public string Title = string.Empty;
    public string Sub = string.Empty;
    public Color Color = Color.White;
    public float Alpha;

    public WGZoneTitleOverlay()
    {
        IoCManager.InjectDependencies(this);
        _font = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), FontSize);
        _subFont = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), FontSize / 3);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args) => Alpha > 0.001f && Title.Length > 0;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var box = WGZoneScreen.Of(args);
        var scale = Math.Clamp(box.Height / 1080f, 0.5f, 1.2f);
        var centre = box.Left + box.Width / 2f;
        var y = box.Top + box.Height * 0.16f;

        var size = handle.GetDimensions(_font, Title, scale);
        // Narrow views shrink the title to fit rather than cut it off.
        var widest = box.Width * 0.7f;
        if (size.X > widest)
        {
            scale *= widest / size.X;
            size = handle.GetDimensions(_font, Title, scale);
        }

        var subSize = Sub.Length > 0 ? handle.GetDimensions(_subFont, Sub, scale) : Vector2.Zero;
        var width = MathF.Max(size.X, subSize.X) + 48f * scale;
        var back = new UIBox2(centre - width / 2f, y - 12f * scale, centre + width / 2f, y + size.Y + subSize.Y + 16f * scale);
        handle.DrawRect(back, Color.Black.WithAlpha(0.6f * Alpha));
        handle.DrawRect(new UIBox2(back.Left, back.Top, back.Right, back.Top + 4f * scale), Color.WithAlpha(Alpha));
        handle.DrawRect(new UIBox2(back.Left, back.Bottom - 4f * scale, back.Right, back.Bottom), Color.WithAlpha(Alpha));

        handle.DrawString(_font, new Vector2(centre - size.X / 2f, y), Title, scale, Color.WithAlpha(Alpha));
        if (Sub.Length > 0)
            handle.DrawString(_subFont, new Vector2(centre - subSize.X / 2f, y + size.Y), Sub, scale, Color.White.WithAlpha(Alpha * 0.9f));
    }
}
