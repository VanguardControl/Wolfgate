using Robust.Client.Graphics;

namespace Content.Client._WF.CombatConsole;

/// <summary>Fits instrument text with a finite set of font raster sizes.</summary>
public static class WFInstrumentText
{
    /// <summary>Returns a bounded scale fitting the text's width and optional full height, or zero if too small.</summary>
    public static float FitScale(DrawingHandleScreen handle, Font font, string text, float maximum, float width,
        float height = float.PositiveInfinity)
    {
        var scale = WFGaugeScale.FontScale(maximum);
        if (scale == 0 || width <= 0 || height <= 0)
            return 0;
        var measured = handle.GetDimensions(font, text, scale).X;
        scale = WFGaugeScale.FontScale(scale * MathF.Min(1, MathF.Min(width / MathF.Max(1, measured),
            height / Math.Max(1, font.GetHeight(scale)))));
        // Raster rounding can add a pixel even when the proportional fit fits exactly.
        while (scale > 0 && (handle.GetDimensions(font, text, scale).X > width || font.GetHeight(scale) > height))
            scale = WFGaugeScale.FontScale(scale - 0.125f);
        return scale;
    }
}
