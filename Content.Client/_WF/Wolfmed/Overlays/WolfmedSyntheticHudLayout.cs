using System.Numerics;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// Where the synthetic readout is allowed to draw. The overlay takes no input, so nothing here can steal a
/// click. Readouts sit inside the playfield, below the toolbar and chat, with a clear central sightline.
/// <see cref="Reserved"/> models the surrounding game UI rather than just the viewport edges.
/// </summary>
public static class WolfmedSyntheticHudLayout
{
    /// <summary>Gap from the screen edge to any block.</summary>
    public const float Margin = 16f;

    /// <summary>Gap from a block's frame to its text.</summary>
    public const float Padding = 6f;

    /// <summary>Font size at scale 1.</summary>
    public const float BaseFont = 11f;

    /// <summary>Rows in the SYSTEM block: heading, four gauges, fault count, status line.</summary>
    public const int SystemRows = 7;

    public const float PanelTop = 0.40f;
    public const float BottomBand = 0.78f;

    /// <summary>Smallest and largest the player's own text setting may be. A zero would collapse the blocks.</summary>
    public const float MinScale = 0.6f;

    public const float MaxScale = 2.5f;

    /// <summary>
    /// The viewport in the coordinates a screen-space overlay actually draws in.
    /// <c>OverlayDrawArgs.ViewportBounds</c> is the viewport control's draw box in GLOBAL physical pixels,
    /// while the handle handed to that control's Draw is already translated to its own top-left, so the
    /// global origin has to come back off or every block lands a viewport away from the corner it wants.
    /// </summary>
    public static UIBox2 Screen(UIBox2i bounds, Vector2 origin) => new(
        bounds.Left - origin.X,
        bounds.Top - origin.Y,
        bounds.Right - origin.X,
        bounds.Bottom - origin.Y);

    /// <summary>
    /// The text scale as drawn: the player's setting times the control's UI scale, because the handle works
    /// in physical pixels and everything else on screen is sized that way too.
    /// </summary>
    public static float Scale(float setting, float uiScale) =>
        Math.Clamp(setting, MinScale, MaxScale) * MathF.Max(0.1f, uiScale);

    public static float Font(float scale) => MathF.Max(6f, MathF.Round(BaseFont * scale));

    public static float LineHeight(float scale) => Font(scale) * 1.4f + 4f;

    /// <summary>Fit the requested size into the inset panels, leaving the central sightline open.</summary>
    public static float FitScale(UIBox2 screen, float requested)
    {
        var scale = requested;
        while (scale > 0.1f &&
               (Padding * 2f + SystemRows * LineHeight(scale) > screen.Height * (BottomBand - PanelTop) ||
                31f * CharWidth(scale) * 1.4f + Padding * 2f > screen.Width * 0.26f ||
                42f * CharWidth(scale) * 1.4f + Padding * 2f > screen.Width * 0.30f))
            scale = MathF.Max(0.1f, scale - 0.05f);
        return scale;
    }

    /// <summary>Monospace advance. RobotoMono is close enough to 0.62 em that the columns line up.</summary>
    public static float CharWidth(float scale) => Font(scale) * 0.62f;

    /// <summary>The chassis block, inset from the left-side action buttons.</summary>
    public static UIBox2 System(UIBox2 screen, float scale)
    {
        var width = MathF.Min(31f * CharWidth(scale) * 1.4f + Padding * 2f,
            screen.Width * 0.26f);
        var height = SystemRows * LineHeight(scale) + Padding * 2f;
        var top = screen.Top + screen.Height * PanelTop;
        return new UIBox2(
            screen.Left + screen.Width * 0.14f,
            top,
            screen.Left + screen.Width * 0.14f + width,
            top + height);
    }

    /// <summary>The fault list, below chat and inset from the right-side alerts.</summary>
    public static UIBox2 Diagnostics(UIBox2 screen, float scale, int lines)
    {
        var width = MathF.Min(42f * CharWidth(scale) * 1.4f + Padding * 2f,
            screen.Width * 0.30f);
        var height = (1 + Math.Max(lines, 1)) * LineHeight(scale) + Padding * 2f;
        var top = screen.Top + screen.Height * PanelTop;
        return new UIBox2(
            screen.Left + screen.Width * 0.90f - width,
            top,
            screen.Left + screen.Width * 0.90f,
            top + height);
    }

    /// <summary>Fault lines that fit above the game HUD, never more than the wire carries.</summary>
    public static int MaxLines(UIBox2 screen, float scale, int cap)
    {
        var room = screen.Height * (BottomBand - PanelTop) - Padding * 2f - LineHeight(scale);
        return Math.Clamp((int) MathF.Floor(room / LineHeight(scale)), 1, cap);
    }

    /// <summary>
    /// A dedicated warning strip above both blocks; it cannot collapse into the gap between them.
    /// </summary>
    public static UIBox2 Banner(UIBox2 screen, float scale)
    {
        var height = LineHeight(scale) + Padding * 2f;
        var top = screen.Top + screen.Height * 0.25f;
        return new UIBox2(screen.Left + screen.Width * 0.32f, top,
            screen.Left + screen.Width * 0.68f, top + height);
    }

    /// <summary>
    /// The parts of the screen the game HUD owns. Approximate but deliberately generous: the hotbar and the
    /// held-item row along the bottom, the alerts column and the targeting doll on the right, and the chat
    /// pane beside them.
    /// </summary>
    public static IEnumerable<(string Name, UIBox2 Box)> Reserved(UIBox2 screen)
    {
        var w = screen.Width;
        var h = screen.Height;
        yield return ("toolbar", new UIBox2(screen.Left, screen.Top,
            screen.Left + w * 0.45f, screen.Top + h * 0.16f));
        yield return ("action buttons", new UIBox2(screen.Left, screen.Top + h * 0.16f,
            screen.Left + w * 0.12f, screen.Top + h * 0.82f));
        yield return ("hotbar", new UIBox2(
            screen.Left + w * 0.18f, screen.Top + h * 0.84f, screen.Left + w * 0.82f, screen.Bottom));
        yield return ("alerts", new UIBox2(
            screen.Left + w * 0.94f, screen.Top + h * 0.34f, screen.Right, screen.Top + h * 0.86f));
        yield return ("chat", new UIBox2(
            screen.Left + w * 0.70f, screen.Top, screen.Right, screen.Top + h * 0.38f));
        yield return ("doll", new UIBox2(
            screen.Left + w * 0.92f, screen.Top + h * 0.84f, screen.Right, screen.Bottom));
        yield return ("central sightline", new UIBox2(screen.Left + w * 0.42f, screen.Top + h * 0.42f,
            screen.Left + w * 0.58f, screen.Top + h * 0.64f));
    }

    /// <summary>Whether two rectangles share any area at all.</summary>
    public static bool Overlaps(UIBox2 left, UIBox2 right) =>
        left.Left < right.Right && right.Left < left.Right &&
        left.Top < right.Bottom && right.Top < left.Bottom;

    /// <summary>Top-left corner of a block's first text row.</summary>
    public static Vector2 TextOrigin(UIBox2 block, float scale) =>
        new(block.Left + Padding, block.Top + Padding);
}
