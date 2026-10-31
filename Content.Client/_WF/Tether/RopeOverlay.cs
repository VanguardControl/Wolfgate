using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WF.Tether;

/// <summary>
/// Draws every simulated rope as a mitred quad strip: a darker, wider outline pass under a
/// lighter core. Taut rope thins, tints towards the type's taut colour and vibrates.
/// </summary>
public sealed class RopeOverlay : Overlay
{
    private readonly Dictionary<EntityUid, RopeChain> _chains;
    private readonly IGameTiming _timing;

    /// <summary>Managed buffers: the client may not stackalloc, and Draw must not allocate.</summary>
    private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[RopeVerlet.MaxPoints * 6];

    /// <summary>Strain at which a rope starts to buzz.</summary>
    private const float VibrationStrain = 0.8f;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public RopeOverlay(Dictionary<EntityUid, RopeChain> chains, IGameTiming timing)
    {
        _chains = chains;
        _timing = timing;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        var handle = args.WorldHandle;
        var time = (float) (_timing.CurTime.TotalSeconds % 1000);
        foreach (var chain in _chains.Values)
        {
            if (!chain.Visible || chain.Count < 2 || chain.MapId != args.MapId)
                continue;

            var strain = Math.Clamp(chain.Strain, 0f, 2f);
            // A rope near its limit is a thinner, brighter, humming line.
            var width = chain.Width * (1f - 0.3f * MathF.Min(strain, 1f));
            var buzz = MathF.Max(0f, strain - VibrationStrain) / (1f - VibrationStrain);
            var vibration = MathF.Min(buzz, 1f) * chain.Width * 0.9f;

            if (!args.WorldAABB.Intersects(Bounds(chain, width * 2f)))
                continue;

            // A textured rope (a tentacle) is one pass of its texture, tinted by strain.
            if (chain.Texture is { } texture)
            {
                var drawn = BuildStrip(chain, width, vibration, time, Color.White.WithAlpha(1f), true);
                if (drawn > 0)
                    handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, texture, new ReadOnlySpan<DrawVertexUV2DColor>(_vertices, 0, drawn));
                continue;
            }

            var outline = Color.FromSrgb(new Color(chain.Color.R * 0.35f, chain.Color.G * 0.35f,
                chain.Color.B * 0.35f, 0.85f));
            var core = Color.FromSrgb(chain.Color.WithAlpha(1f));

            var count = BuildStrip(chain, width * 1.8f, vibration, time, outline);
            if (count == 0)
                continue;

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
                new ReadOnlySpan<DrawVertexUV2DColor>(_vertices, 0, count));
            count = BuildStrip(chain, width, vibration, time, core);
            if (count > 0)
            {
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
                    new ReadOnlySpan<DrawVertexUV2DColor>(_vertices, 0, count));
            }
        }
    }

    /// <summary>Two triangles per segment, with the shared edge mitred along the averaged normal.</summary>
    private int BuildStrip(RopeChain chain, float width, float vibration, float time, Color color, bool textured = false)
    {
        var count = Math.Min(chain.Count, chain.Points.Length);
        if (count < 2 || !float.IsFinite(width) || width <= 0f)
            return 0;

        var half = width * 0.5f;
        var written = 0;
        var previousLeft = Vector2.Zero;
        var previousRight = Vector2.Zero;
        for (var i = 0; i < count; i++)
        {
            var point = chain.Points[i];
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                return 0;

            var tangent = Tangent(chain.Points, count, i);
            var normal = new Vector2(-tangent.Y, tangent.X);
            if (vibration > 0f && i > 0 && i < count - 1)
                point += normal * (MathF.Sin(time * 45f + i * 1.7f) * vibration);

            // Narrows from end A to end B by the type's taper.
            var along = count > 1 ? (float) i / (count - 1) : 0f;
            var halfHere = half * (1f + (chain.Taper - 1f) * along);
            var left = point + normal * halfHere;
            var right = point - normal * halfHere;
            if (i > 0 && written + 6 <= _vertices.Length)
            {
                if (textured)
                {
                    // One copy of the texture per segment.
                    _vertices[written++] = new DrawVertexUV2DColor(previousLeft, new Vector2(0f, 0f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(previousRight, new Vector2(1f, 0f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(left, new Vector2(0f, 1f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(previousRight, new Vector2(1f, 0f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(right, new Vector2(1f, 1f), color);
                    _vertices[written++] = new DrawVertexUV2DColor(left, new Vector2(0f, 1f), color);
                }
                else
                {
                    _vertices[written++] = new DrawVertexUV2DColor(previousLeft, color);
                    _vertices[written++] = new DrawVertexUV2DColor(previousRight, color);
                    _vertices[written++] = new DrawVertexUV2DColor(left, color);
                    _vertices[written++] = new DrawVertexUV2DColor(previousRight, color);
                    _vertices[written++] = new DrawVertexUV2DColor(right, color);
                    _vertices[written++] = new DrawVertexUV2DColor(left, color);
                }
            }

            previousLeft = left;
            previousRight = right;
        }

        return written;
    }

    /// <summary>Averaged neighbour direction, so the strip mitres instead of pinching at corners.</summary>
    private static Vector2 Tangent(Vector2[] points, int count, int index)
    {
        var before = points[Math.Max(index - 1, 0)];
        var after = points[Math.Min(index + 1, count - 1)];
        var delta = after - before;
        var length = delta.Length();
        return float.IsFinite(length) && length > 0.00001f ? delta / length : Vector2.UnitX;
    }

    private static Box2 Bounds(RopeChain chain, float margin)
    {
        var min = chain.Points[0];
        var max = min;
        for (var i = 1; i < Math.Min(chain.Count, chain.Points.Length); i++)
        {
            min = Vector2.Min(min, chain.Points[i]);
            max = Vector2.Max(max, chain.Points[i]);
        }

        return new Box2(min, max).Enlarged(margin);
    }
}
