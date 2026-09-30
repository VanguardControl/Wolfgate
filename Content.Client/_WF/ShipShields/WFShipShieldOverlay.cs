using System.Numerics;
using Content.Shared._WF.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.ShipShields;

/// <summary>Draws translucent hull contours with traveling impact waves and localized heat.</summary>
public sealed class WFShipShieldOverlay : Overlay
{
    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transforms;
    private readonly IGameTiming _timing;
    private readonly Dictionary<EntityUid, List<WFShipShieldOverlaySystem.Impact>> _impacts;
    private readonly ShaderInstance _shader;
    private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[8190];
    private readonly DrawVertexUV2DColor[] _hexVertices = new DrawVertexUV2DColor[8190];
    private int _count;
    private int _hexCount;
    private static readonly float[] Offsets = { -0.35f, 0f, 0.18f, 0.65f, 1.4f };
    private static readonly float[] Opacities = { 0f, 0.38f, 0.20f, 0.055f, 0f };

    /// <summary>Places the field beneath ships while retaining its unlit energy color.</summary>
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    /// <summary>Creates reusable geometry buffers and binds the client impact cache.</summary>
    public WFShipShieldOverlay(IEntityManager entities, IPrototypeManager prototypes, IGameTiming timing,
        Dictionary<EntityUid, List<WFShipShieldOverlaySystem.Impact>> impacts)
    {
        _entities = entities;
        _transforms = entities.System<SharedTransformSystem>();
        _timing = timing;
        _impacts = impacts;
        _shader = prototypes.Index<ShaderPrototype>("unshaded").Instance();
        ZIndex = 8;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        var query = _entities.EntityQueryEnumerator<WFShipShieldVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var shield, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;
            var matrix = _transforms.GetWorldMatrix(xform);
            var tint = WFShipShieldEffects.HealthColor(shield.Health);
            _impacts.TryGetValue(uid, out var impacts);
            foreach (var contour in shield.Contours)
            {
                if (contour.Length < 3)
                    continue;
                var min = Vector2.Transform(contour[0], matrix);
                var max = min;
                var area = 0f;
                for (var i = 0; i < contour.Length; i++)
                {
                    var world = Vector2.Transform(contour[i], matrix);
                    min = Vector2.Min(min, world);
                    max = Vector2.Max(max, world);
                    var next = contour[(i + 1) % contour.Length];
                    area += contour[i].X * next.Y - next.X * contour[i].Y;
                }
                if (!args.WorldAABB.Intersects(new Box2(min - new Vector2(1.5f), max + new Vector2(1.5f))))
                    continue;
                var winding = area >= 0f ? 1f : -1f;
                var perimeter = 0f;
                for (var i = 0; i < contour.Length; i++)
                {
                    var nextIndex = (i + 1) % contour.Length;
                    var start = contour[i];
                    var end = contour[nextIndex];
                    var normalStart = Normal(contour, i, winding);
                    var normalEnd = Normal(contour, nextIndex, winding);
                    DrawHexes(handle, start, end, normalStart, normalEnd, perimeter, matrix, tint, impacts);
                    perimeter += Vector2.Distance(start, end);
                    var divisions = Math.Clamp((int) MathF.Ceiling(Vector2.Distance(start, end) / 0.65f), 1, 256);
                    for (var section = 0; section < divisions; section++)
                    {
                        var t0 = (float) section / divisions;
                        var t1 = (float) (section + 1) / divisions;
                        var a = Vector2.Lerp(start, end, t0);
                        var b = Vector2.Lerp(start, end, t1);
                        var na = Vector2.Lerp(normalStart, normalEnd, t0);
                        var nb = Vector2.Lerp(normalStart, normalEnd, t1);
                        var effectA = Surface(a, tint, impacts);
                        var effectB = Surface(b, tint, impacts);
                        for (var band = 0; band < Offsets.Length - 1; band++)
                        {
                            if (_count + 6 > _vertices.Length)
                                Flush(handle);
                            var a0 = Vertex(a + na * Offsets[band], matrix, effectA, Opacities[band]);
                            var b0 = Vertex(b + nb * Offsets[band], matrix, effectB, Opacities[band]);
                            var a1 = Vertex(a + na * Offsets[band + 1], matrix, effectA, Opacities[band + 1]);
                            var b1 = Vertex(b + nb * Offsets[band + 1], matrix, effectB, Opacities[band + 1]);
                            _vertices[_count++] = a0;
                            _vertices[_count++] = b0;
                            _vertices[_count++] = a1;
                            _vertices[_count++] = b0;
                            _vertices[_count++] = b1;
                            _vertices[_count++] = a1;
                        }
                    }
                }
            }
        }
        Flush(handle);
        FlushHexes(handle);
        handle.UseShader(null);
    }

    /// <summary>Wraps a clipped honeycomb lattice around the contour's translucent inner band.</summary>
    private void DrawHexes(DrawingHandleWorld handle, Vector2 start, Vector2 end, Vector2 normalStart,
        Vector2 normalEnd, float perimeter, Matrix3x2 matrix, Color tint,
        List<WFShipShieldOverlaySystem.Impact>? impacts)
    {
        const float radius = 0.45f;
        const float step = radius * 1.5f;
        const float height = radius * 1.7320508f;
        var length = Vector2.Distance(start, end);
        if (length < 0.001f)
            return;
        var first = (int) MathF.Floor((perimeter - radius) / step);
        var last = (int) MathF.Ceiling((perimeter + length + radius) / step);
        for (var column = first; column <= last; column++)
        {
            for (var row = -1; row <= 2; row++)
            {
                var center = new Vector2(column * step, row * height + (column % 2 == 0 ? 0f : height * 0.5f));
                var sample = new Vector2(Math.Clamp(center.X, perimeter, perimeter + length),
                    Math.Clamp(center.Y, 0.04f, 1.3f));
                var effect = Surface(RibbonPoint(sample), tint, impacts);
                // Three edges per cell form the entire lattice without drawing shared edges twice.
                for (var edge = 0; edge < 3; edge++)
                {
                    var angleA = edge * MathF.PI / 3f;
                    var angleB = (edge + 1) * MathF.PI / 3f;
                    var a = center + new Vector2(MathF.Cos(angleA), MathF.Sin(angleA)) * radius;
                    var b = center + new Vector2(MathF.Cos(angleB), MathF.Sin(angleB)) * radius;
                    if (!Clip(ref a, ref b, perimeter, perimeter + length, 0.04f, 1.3f))
                        continue;
                    var pointA = RibbonPoint(a);
                    var pointB = RibbonPoint(b);
                    if (_hexCount + 2 > _hexVertices.Length)
                        FlushHexes(handle);
                    var opacity = 0.075f * (1f - (a.Y + b.Y) / 3f);
                    _hexVertices[_hexCount++] = Vertex(pointA, matrix, effect, opacity);
                    _hexVertices[_hexCount++] = Vertex(pointB, matrix, effect, opacity);
                }
            }
        }
        return;

        Vector2 RibbonPoint(Vector2 point)
        {
            var fraction = (point.X - perimeter) / length;
            return Vector2.Lerp(start, end, fraction) + Vector2.Lerp(normalStart, normalEnd, fraction) * point.Y;
        }
    }

    /// <summary>Clips lattice edges before wrapping them onto the shield surface.</summary>
    private static bool Clip(ref Vector2 a, ref Vector2 b, float left, float right, float bottom, float top)
    {
        var delta = b - a;
        var low = 0f;
        var high = 1f;
        if (!Axis(a.X, delta.X, left, right, ref low, ref high) ||
            !Axis(a.Y, delta.Y, bottom, top, ref low, ref high))
            return false;
        b = a + delta * high;
        a += delta * low;
        return true;
    }

    private static bool Axis(float position, float delta, float minimum, float maximum, ref float low, ref float high)
    {
        if (MathF.Abs(delta) < 0.0001f)
            return position >= minimum && position <= maximum;
        var first = (minimum - position) / delta;
        var second = (maximum - position) / delta;
        low = MathF.Max(low, MathF.Min(first, second));
        high = MathF.Min(high, MathF.Max(first, second));
        return low <= high;
    }

    /// <summary>Uses joined inward normals so glow follows concave hull corners.</summary>
    private static Vector2 Normal(Vector2[] contour, int index, float winding)
    {
        var previous = contour[index] - contour[(index + contour.Length - 1) % contour.Length];
        var next = contour[(index + 1) % contour.Length] - contour[index];
        if (previous.LengthSquared() < 0.0001f || next.LengthSquared() < 0.0001f)
            return Vector2.Zero;
        previous = Vector2.Normalize(previous);
        next = Vector2.Normalize(next);
        var sum = new Vector2(-previous.Y - next.Y, previous.X + next.X) * winding;
        if (sum.LengthSquared() < 0.0001f)
            return new Vector2(-next.Y, next.X) * winding;
        var normal = Vector2.Normalize(sum);
        var edgeNormal = new Vector2(-next.Y, next.X) * winding;
        return normal / MathF.Max(0.5f, Vector2.Dot(normal, edgeNormal));
    }

    /// <summary>Accumulates cooling hotspots and expanding rings at each surface sample.</summary>
    private Color Surface(Vector2 point, Color tint,
        List<WFShipShieldOverlaySystem.Impact>? impacts)
    {
        var heat = 0f;
        var wave = 0f;
        if (impacts != null)
        {
            foreach (var impact in impacts)
            {
                var age = (float) (_timing.CurTime - impact.Time).TotalSeconds;
                if (age < 0f || age > 8f)
                    continue;
                var distance = Vector2.Distance(point, impact.Position);
                heat += WFShipShieldEffects.Heat(distance, age, impact.Strength);
                wave += WFShipShieldEffects.Wave(distance, age, impact.Strength);
            }
        }
        tint = Color.InterpolateBetween(tint, new Color(1f, 0.025f, 0.06f), Math.Clamp(heat, 0f, 1f));
        tint = Color.InterpolateBetween(tint, new Color(0.7f, 0.92f, 1f), Math.Clamp(wave * 0.65f, 0f, 0.8f));
        return Color.FromSrgb(tint).WithAlpha(1f + wave * 2.5f + heat * 0.6f);
    }

    private static DrawVertexUV2DColor Vertex(Vector2 point, Matrix3x2 matrix, Color effect, float alpha)
    {
        return new DrawVertexUV2DColor(Vector2.Transform(point, matrix),
            effect.WithAlpha(Math.Clamp(alpha * effect.A, 0f, 0.85f)));
    }

    private void Flush(DrawingHandleWorld handle)
    {
        if (_count == 0)
            return;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(_vertices, 0, _count));
        _count = 0;
    }

    private void FlushHexes(DrawingHandleWorld handle)
    {
        if (_hexCount == 0)
            return;
        handle.DrawPrimitives(DrawPrimitiveTopology.LineList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(_hexVertices, 0, _hexCount));
        _hexCount = 0;
    }
}
