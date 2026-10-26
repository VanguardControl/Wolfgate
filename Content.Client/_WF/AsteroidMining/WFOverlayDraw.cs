using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WF.AsteroidMining;

/// <summary>Drawing helpers shared by the asteroid mining overlays.</summary>
public static class WFOverlayDraw
{
    /// <summary>A disc fading from its centre colour to clear at its rim, with one side per three vertices of the buffer.</summary>
    public static void Glow(DrawingHandleWorld handle, DrawVertexUV2DColor[] fan, Vector2 center, float radius, Color color)
    {
        var sides = fan.Length / 3;
        var clear = color.WithAlpha(0f);
        var count = 0;
        for (var i = 0; i < sides; i++)
        {
            var a = MathF.Tau * i / sides;
            var b = MathF.Tau * (i + 1) / sides;
            fan[count++] = new DrawVertexUV2DColor(center, color);
            fan[count++] = new DrawVertexUV2DColor(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius, clear);
            fan[count++] = new DrawVertexUV2DColor(center + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, clear);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(fan, 0, count));
    }

    /// <summary>A cheap repeatable pseudo-random value in [0, 1) for a seed.</summary>
    public static float Hash(float value)
    {
        var x = MathF.Sin(value * 12.9898f) * 43758.5453f;
        return x - MathF.Floor(x);
    }
}
