using System.Numerics;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// Where the synthetic readout is allowed to draw. The overlay takes no input, so nothing here can steal a
/// click, but it can still cover something the player needs to read: the blocks are pinned to the two top
/// corners and the top edge, and <see cref="Reserved"/> is what the layout test measures them against.
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

    /// <summary>Share of the screen height the readout may use. Everything below it belongs to the game HUD.</summary>
    public const float TopBand = 0.32f;

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

    public static float Font(float scale) => MathF.Max(8f, MathF.Round(BaseFont * scale));

    public static float LineHeight(float scale) => Font(scale) + 4f;

    /// <summary>Monospace advance. RobotoMono is close enough to 0.62 em that the columns line up.</summary>
    public static float CharWidth(float scale) => Font(scale) * 0.62f;

    /// <summary>The top-left chassis block.</summary>
    public static UIBox2 System(UIBox2 screen, float scale)
    {
        var width = 23f * CharWidth(scale) + Padding * 2f;
        var height = SystemRows * LineHeight(scale) + Padding * 2f;
        return new UIBox2(
            screen.Left + Margin,
            screen.Top + Margin,
            screen.Left + Margin + width,
            screen.Top + Margin + height);
    }

    /// <summary>The top-right fault list, sized to the lines it is actually showing.</summary>
    public static UIBox2 Diagnostics(UIBox2 screen, float scale, int lines)
    {
        var width = 38f * CharWidth(scale) + Padding * 2f;
        var height = (1 + Math.Max(lines, 1)) * LineHeight(scale) + Padding * 2f;
        return new UIBox2(
            screen.Right - Margin - width,
            screen.Top + Margin,
            screen.Right - Margin,
            screen.Top + Margin + height);
    }

    /// <summary>Fault lines that fit above the game HUD, never more than the wire carries.</summary>
    public static int MaxLines(UIBox2 screen, float scale, int cap)
    {
        var room = screen.Top + screen.Height * TopBand - (screen.Top + Margin + Padding * 2f) - LineHeight(scale);
        return Math.Clamp((int) MathF.Floor(room / LineHeight(scale)), 1, cap);
    }

    /// <summary>
    /// The banner strip along the top edge. It takes the gap the two corner blocks leave and no more, so a
    /// large text scale on a small screen shortens the banner instead of running it under the fault list.
    /// </summary>
    public static UIBox2 Banner(UIBox2 screen, float scale)
    {
        var height = LineHeight(scale) + Padding * 2f;
        var centre = screen.Left + screen.Width / 2f;
        var gap = MathF.Min(centre - (System(screen, scale).Right + Padding),
            Diagnostics(screen, scale, 1).Left - Padding - centre);
        var half = MathF.Max(0f, MathF.Min(screen.Width * 0.22f, gap));
        return new UIBox2(centre - half, screen.Top + Margin, centre + half, screen.Top + Margin + height);
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
        yield return ("hotbar", new UIBox2(
            screen.Left + w * 0.18f, screen.Top + h * 0.84f, screen.Left + w * 0.82f, screen.Bottom));
        yield return ("alerts", new UIBox2(
            screen.Left + w * 0.78f, screen.Top + h * 0.34f, screen.Right, screen.Top + h * 0.86f));
        yield return ("chat", new UIBox2(
            screen.Left + w * 0.70f, screen.Top + h * 0.40f, screen.Right, screen.Bottom));
        yield return ("doll", new UIBox2(
            screen.Left + w * 0.82f, screen.Top + h * 0.68f, screen.Right, screen.Top + h * 0.90f));
    }

    /// <summary>Whether two rectangles share any area at all.</summary>
    public static bool Overlaps(UIBox2 left, UIBox2 right) =>
        left.Left < right.Right && right.Left < left.Right &&
        left.Top < right.Bottom && right.Top < left.Bottom;

    /// <summary>Top-left corner of a block's first text row.</summary>
    public static Vector2 TextOrigin(UIBox2 block, float scale) =>
        new(block.Left + Padding, block.Top + Padding + Font(scale));
}
