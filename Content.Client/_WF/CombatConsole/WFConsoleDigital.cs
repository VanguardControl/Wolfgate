using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WF.CombatConsole;

/// <summary>Draws thin luminous geometry for Futurist instruments without physical chrome.</summary>
public static class WFConsoleDigital
{
    /// <summary>Draws a clipped-corner glass panel with accent brackets and a restrained top highlight.</summary>
    public static void Panel(DrawingHandleScreen handle, UIBox2 box, float scale, Color fill, Color edge)
    {
        var cut = MathF.Min(8 * scale, MathF.Min(box.Width, box.Height) / 4);
        var a = box.TopLeft + new Vector2(cut, 0);
        var b = new Vector2(box.Right - cut, box.Top);
        var c = new Vector2(box.Right, box.Top + cut);
        var d = new Vector2(box.Right, box.Bottom - cut);
        var e = new Vector2(box.Right - cut, box.Bottom);
        var f = new Vector2(box.Left + cut, box.Bottom);
        var g = new Vector2(box.Left, box.Bottom - cut);
        var h = new Vector2(box.Left, box.Top + cut);
        var face = fill * handle.Modulate;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, Texture.White,
            new[] { new DrawVertexUV2DColor(a, face), new DrawVertexUV2DColor(b, face),
                new DrawVertexUV2DColor(c, face), new DrawVertexUV2DColor(d, face),
                new DrawVertexUV2DColor(e, face), new DrawVertexUV2DColor(f, face),
                new DrawVertexUV2DColor(g, face), new DrawVertexUV2DColor(h, face) });
        var points = new[] { a, b, c, d, e, f, g, h, a };
        for (var i = 0; i < points.Length - 1; i++)
            handle.DrawLine(points[i], points[i + 1], edge);
        var accent = WFInstrumentTheme.Skin.Accent;
        handle.DrawLine(a, a + new Vector2(MathF.Min(28 * scale, box.Width / 4), 0), accent);
        handle.DrawLine(e, e - new Vector2(MathF.Min(28 * scale, box.Width / 4), 0), accent.WithAlpha(0.5f));
    }

    /// <summary>Draws a smooth ring with a one-pixel coverage fringe on both edges and end caps.</summary>
    public static void Arc(DrawingHandleScreen handle, Vector2 center, float radius, float width, float start, float end, Color color, ref DrawVertexUV2DColor[] vertices)
    {
        if (end <= start || radius <= 0 || width <= 0)
            return;
        const float fringe = 1.15f;
        var fullCircle = end - start >= MathF.Tau - 0.001f;
        var cap = fullCircle ? 0 : fringe / radius;
        var count = Math.Max(2, (int) MathF.Ceiling((end - start + cap * 2) * radius / 2));
        var half = MathF.Min(0.5f, width / 2);
        var radii = new[] { radius + fringe - half, radius - half, radius - width + half, radius - width - fringe + half };
        if (vertices.Length < count * 18)
            vertices = new DrawVertexUV2DColor[count * 18];
        // Match the modulation space used by UI text and DrawRect so the arc retains its palette colour.
        var tint = color * handle.Modulate;
        var index = 0;
        for (var i = 0; i < count; i++)
        {
            var first = start - cap + (end - start + cap * 2) * i / count;
            var second = start - cap + (end - start + cap * 2) * (i + 1) / count;
            var a = new Vector2(MathF.Cos(first), MathF.Sin(first));
            var b = new Vector2(MathF.Cos(second), MathF.Sin(second));
            var firstAlpha = fullCircle || i > 0 ? tint.A : 0;
            var secondAlpha = fullCircle || i < count - 1 ? tint.A : 0;
            for (var band = 0; band < 3; band++)
            {
                var outerA = new DrawVertexUV2DColor(center + a * radii[band], tint.WithAlpha(band == 0 ? 0 : firstAlpha));
                var outerB = new DrawVertexUV2DColor(center + b * radii[band], tint.WithAlpha(band == 0 ? 0 : secondAlpha));
                var innerA = new DrawVertexUV2DColor(center + a * radii[band + 1], tint.WithAlpha(band == 2 ? 0 : firstAlpha));
                var innerB = new DrawVertexUV2DColor(center + b * radii[band + 1], tint.WithAlpha(band == 2 ? 0 : secondAlpha));
                vertices[index++] = outerA;
                vertices[index++] = outerB;
                vertices[index++] = innerB;
                vertices[index++] = outerA;
                vertices[index++] = innerB;
                vertices[index++] = innerA;
            }
        }
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(vertices, 0, index));
    }

    /// <summary>Draws an antialiased position marker at the end of a digital scale.</summary>
    public static void Dot(DrawingHandleScreen handle, Vector2 center, float radius, Color color, ref DrawVertexUV2DColor[] vertices)
    {
        const int segments = 32;
        if (vertices.Length < segments * 9)
            vertices = new DrawVertexUV2DColor[segments * 9];
        var tint = color * handle.Modulate;
        var clear = tint.WithAlpha(0);
        var index = 0;
        for (var i = 0; i < segments; i++)
        {
            var first = i * MathF.Tau / segments;
            var second = (i + 1) * MathF.Tau / segments;
            var a = new Vector2(MathF.Cos(first), MathF.Sin(first));
            var b = new Vector2(MathF.Cos(second), MathF.Sin(second));
            var innerA = new DrawVertexUV2DColor(center + a * (radius - 0.5f), tint);
            var innerB = new DrawVertexUV2DColor(center + b * (radius - 0.5f), tint);
            var outerA = new DrawVertexUV2DColor(center + a * (radius + 0.65f), clear);
            var outerB = new DrawVertexUV2DColor(center + b * (radius + 0.65f), clear);
            vertices[index++] = new DrawVertexUV2DColor(center, tint);
            vertices[index++] = innerA;
            vertices[index++] = innerB;
            vertices[index++] = innerA;
            vertices[index++] = outerA;
            vertices[index++] = outerB;
            vertices[index++] = innerA;
            vertices[index++] = outerB;
            vertices[index++] = innerB;
        }
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(vertices, 0, index));
    }
}
