using System.Numerics;
using Content.Client._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Hud;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// The helmet readout itself: two bracketed blocks in the top corners, a banner along the top edge, and the
/// standby and kernel-panic screens. Pure screen-space drawing, no controls, so it can never take a click.
/// Every value is owned by <see cref="WolfmedSyntheticHudSystem"/>.
/// </summary>
public sealed class WolfmedSyntheticHudOverlay : Overlay
{
    public static readonly Color Phosphor = Color.FromHex("#6fd6ff");
    public static readonly Color Dim = Color.FromHex("#3f8ea8");
    public static readonly Color Faint = Color.FromHex("#1b3a46");

    private static readonly Color Warn = Color.FromHex("#ffc24d");
    private static readonly Color Crit = Color.FromHex("#ff8a3c");
    private static readonly Color Fail = Color.FromHex("#ff5a5a");

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    /// <summary>One fault as the readout draws it.</summary>
    public readonly record struct Row(string Tag, string Part, string Text, WolfmedSyntheticSeverity Severity, float Slide);

    public float Scale = 1f;
    public WolfmedSyntheticTier Tier;

    public float GlyphAlpha;
    public float SystemAlpha;
    public float DiagnosticsAlpha;
    public float BannerAlpha;
    public float StandbyAlpha;
    public float PanicAlpha;
    public float DeathAlpha;

    public string Glyph = string.Empty;
    public string Spinner = string.Empty;
    public string Status = string.Empty;
    public string Banner = string.Empty;
    public WolfmedSyntheticSeverity BannerSeverity = WolfmedSyntheticSeverity.Crit;
    public bool RebootVisible;
    public string StandbyTitle = string.Empty;
    public string RebootText = string.Empty;
    public string PanicTitle = string.Empty;
    public string DeathTitle = string.Empty;
    public string DeathSub = string.Empty;

    public readonly List<string> SystemRows = new();
    public readonly List<Row> Rows = new();
    public readonly List<string> Panic = new();

    /// <summary>Sideways wobble, in pixels, applied to every block. Zero under reduced motion.</summary>
    public float Jitter;

    protected override bool BeforeDraw(in OverlayDrawArgs args) =>
        GlyphAlpha > 0.004f || SystemAlpha > 0.004f || DiagnosticsAlpha > 0.004f ||
        BannerAlpha > 0.004f || StandbyAlpha > 0.004f || PanicAlpha > 0.004f || DeathAlpha > 0.004f;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var screen = args.ViewportBounds;
        var font = AutodocStyle.Mono((int) WolfmedSyntheticHudLayout.Font(Scale));
        var bold = AutodocStyle.Mono((int) WolfmedSyntheticHudLayout.Font(Scale), true);
        var line = WolfmedSyntheticHudLayout.LineHeight(Scale);

        if (GlyphAlpha > 0.004f)
        {
            handle.DrawString(font,
                new Vector2(screen.Left + WolfmedSyntheticHudLayout.Margin,
                    screen.Top + WolfmedSyntheticHudLayout.Margin + WolfmedSyntheticHudLayout.Font(Scale)),
                Glyph, 1f, Phosphor.WithAlpha(GlyphAlpha * 0.5f));
        }

        if (SystemAlpha > 0.004f)
            DrawSystem(handle, screen, font, bold, line);

        if (DiagnosticsAlpha > 0.004f)
            DrawDiagnostics(handle, screen, font, bold, line);

        if (BannerAlpha > 0.004f)
            DrawBanner(handle, screen, bold, line);

        if (StandbyAlpha > 0.004f)
            DrawStandby(handle, screen, bold, font, line);

        if (PanicAlpha > 0.004f)
            DrawPanic(handle, screen, font, line);

        if (DeathAlpha > 0.004f)
            DrawDeath(handle, screen, bold, font);
    }

    private void DrawSystem(DrawingHandleScreen handle, UIBox2 screen, Font font, Font bold, float line)
    {
        var box = Shift(WolfmedSyntheticHudLayout.System(screen, Scale));
        Block(handle, box, SystemAlpha);

        var origin = WolfmedSyntheticHudLayout.TextOrigin(box, Scale);
        handle.DrawString(bold, origin, Loc.GetString("wolfmed-synthetic-system"), 1f,
            Phosphor.WithAlpha(SystemAlpha));
        handle.DrawString(bold, new Vector2(box.Right - WolfmedSyntheticHudLayout.Padding -
            WolfmedSyntheticHudLayout.CharWidth(Scale), origin.Y), Spinner, 1f, Phosphor.WithAlpha(SystemAlpha));

        var y = origin.Y;
        foreach (var row in SystemRows)
        {
            y += line;
            handle.DrawString(font, new Vector2(origin.X, y), row, 1f, Dim.WithAlpha(SystemAlpha));
        }

        // The status line: harmless flavour while whole, the worst fault's advice once not.
        handle.DrawString(font, new Vector2(origin.X, box.Bottom - WolfmedSyntheticHudLayout.Padding), Status, 1f,
            (Rows.Count > 0 ? Warn : Dim).WithAlpha(SystemAlpha * 0.9f));
    }

    private void DrawDiagnostics(DrawingHandleScreen handle, UIBox2 screen, Font font, Font bold, float line)
    {
        var box = Shift(WolfmedSyntheticHudLayout.Diagnostics(screen, Scale, Rows.Count));
        Block(handle, box, DiagnosticsAlpha);

        var origin = WolfmedSyntheticHudLayout.TextOrigin(box, Scale);
        handle.DrawString(bold, origin, Loc.GetString("wolfmed-synthetic-diagnostics"), 1f,
            Phosphor.WithAlpha(DiagnosticsAlpha));

        var y = origin.Y;
        var width = WolfmedSyntheticHudLayout.CharWidth(Scale);
        foreach (var row in Rows)
        {
            y += line;
            // A new line slides in from the right and fades up with it; both are zero under reduced motion.
            var slide = (1f - row.Slide) * width * 6f;
            var alpha = DiagnosticsAlpha * Math.Clamp(row.Slide, 0.15f, 1f);
            var colour = Tint(row.Severity);
            handle.DrawString(bold, new Vector2(origin.X + slide, y), row.Tag, 1f, colour.WithAlpha(alpha));
            handle.DrawString(font, new Vector2(origin.X + slide + width * 7f, y), row.Part, 1f,
                Dim.WithAlpha(alpha));
            handle.DrawString(font, new Vector2(origin.X + slide + width * 19f, y), row.Text, 1f,
                colour.WithAlpha(alpha));
        }
    }

    private void DrawBanner(DrawingHandleScreen handle, UIBox2 screen, Font bold, float line)
    {
        var box = Shift(WolfmedSyntheticHudLayout.Banner(screen, Scale));
        var colour = Tint(BannerSeverity);
        handle.DrawRect(box, Color.Black.WithAlpha(0.45f * BannerAlpha));
        Frame(handle, box, colour.WithAlpha(0.7f * BannerAlpha));

        var size = handle.GetDimensions(bold, Banner, 1f);
        handle.DrawString(bold,
            new Vector2(box.Left + (box.Width - size.X) / 2f, box.Top + WolfmedSyntheticHudLayout.Padding +
                WolfmedSyntheticHudLayout.Font(Scale)),
            Banner, 1f, colour.WithAlpha(BannerAlpha));
    }

    private void DrawStandby(DrawingHandleScreen handle, UIBox2 screen, Font bold, Font font, float line)
    {
        handle.DrawRect(screen, Color.FromHex("#04090c").WithAlpha(0.62f * StandbyAlpha));

        var centre = new Vector2(screen.Left + screen.Width / 2f, screen.Top + screen.Height / 2f);
        var scale = MathF.Max(1f, screen.Height / 260f);
        var title = handle.GetDimensions(bold, StandbyTitle, scale);
        handle.DrawString(bold, new Vector2(centre.X - title.X / 2f, centre.Y - title.Y), StandbyTitle, scale,
            Phosphor.WithAlpha(StandbyAlpha * 0.85f));

        if (!RebootVisible)
            return;

        var sub = handle.GetDimensions(font, RebootText, 1f);
        handle.DrawString(font, new Vector2(centre.X - sub.X / 2f, centre.Y + line), RebootText, 1f,
            Dim.WithAlpha(StandbyAlpha));
    }

    private void DrawPanic(DrawingHandleScreen handle, UIBox2 screen, Font font, float line)
    {
        handle.DrawRect(screen, Color.Black.WithAlpha(0.88f * PanicAlpha));

        var x = screen.Left + screen.Width * 0.08f;
        var y = screen.Top + screen.Height * 0.16f;
        handle.DrawString(font, new Vector2(x, y), PanicTitle, 1f, Fail.WithAlpha(PanicAlpha));
        foreach (var row in Panic)
        {
            y += line;
            handle.DrawString(font, new Vector2(x, y), row, 1f, Dim.WithAlpha(PanicAlpha * 0.85f));
        }
    }

    private void DrawDeath(DrawingHandleScreen handle, UIBox2 screen, Font bold, Font font)
    {
        var height = screen.Height * 0.16f;
        var middle = screen.Top + screen.Height / 2f;
        var core = new UIBox2(screen.Left, middle - height / 2f, screen.Right, middle + height / 2f);
        handle.DrawRect(core, Color.Black.WithAlpha(0.72f * DeathAlpha));

        var scale = height * 0.4f / WolfmedSyntheticHudLayout.Font(Scale);
        var size = handle.GetDimensions(bold, DeathTitle, scale);
        var centre = screen.Left + screen.Width / 2f;
        handle.DrawString(bold, new Vector2(centre - size.X / 2f, middle - size.Y * 0.62f), DeathTitle, scale,
            Phosphor.WithAlpha(DeathAlpha));

        var sub = handle.GetDimensions(font, DeathSub, 1f);
        handle.DrawString(font, new Vector2(centre - sub.X / 2f, middle + size.Y * 0.36f), DeathSub, 1f,
            Dim.WithAlpha(DeathAlpha * 0.9f));
    }

    /// <summary>A block's backing: dark glass, a one-pixel frame and a faint scanline band.</summary>
    private static void Block(DrawingHandleScreen handle, UIBox2 box, float alpha)
    {
        handle.DrawRect(box, Color.FromHex("#04141b").WithAlpha(0.42f * alpha));
        for (var y = box.Top + 2f; y < box.Bottom; y += 3f)
            handle.DrawRect(new UIBox2(box.Left, y, box.Right, y + 1f), Faint.WithAlpha(0.18f * alpha));

        Frame(handle, box, Phosphor.WithAlpha(0.45f * alpha));
    }

    /// <summary>A one-pixel bracket: the full frame plus brighter corner ticks.</summary>
    private static void Frame(DrawingHandleScreen handle, UIBox2 box, Color colour)
    {
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + 1f), colour);
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - 1f, box.Right, box.Bottom), colour);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + 1f, box.Bottom), colour);
        handle.DrawRect(new UIBox2(box.Right - 1f, box.Top, box.Right, box.Bottom), colour);

        var tick = MathF.Min(10f, box.Width / 4f);
        var bright = colour.WithAlpha(MathF.Min(1f, colour.A * 2f));
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + tick, box.Top + 2f), bright);
        handle.DrawRect(new UIBox2(box.Right - tick, box.Bottom - 2f, box.Right, box.Bottom), bright);
    }

    private UIBox2 Shift(UIBox2 box) =>
        Jitter == 0f ? box : new UIBox2(box.Left + Jitter, box.Top, box.Right + Jitter, box.Bottom);

    private static Color Tint(WolfmedSyntheticSeverity severity) => severity switch
    {
        WolfmedSyntheticSeverity.Warn => Warn,
        WolfmedSyntheticSeverity.Crit => Crit,
        WolfmedSyntheticSeverity.Fail => Fail,
        _ => Phosphor,
    };
}
