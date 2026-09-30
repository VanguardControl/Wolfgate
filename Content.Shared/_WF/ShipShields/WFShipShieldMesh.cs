using System.Numerics;

namespace Content.Shared._WF.ShipShields;

/// <summary>Builds reusable local-space ribbons and honeycomb lines only when hull contours change.</summary>
public sealed class WFShipShieldMesh
{
    /// <summary>Cached gradient triangles in shield-local coordinates.</summary>
    public readonly List<Vertex> Triangles = new();
    /// <summary>Cached unique edges of the honeycomb lattice.</summary>
    public readonly List<Vertex> HexLines = new();
    /// <summary>Impact colors are evaluated once per ribbon section.</summary>
    public readonly List<Vector2> Samples = new();
    /// <summary>Local bounds used before any per-frame surface sampling.</summary>
    public Box2 Bounds;

    /// <summary>A local vertex referencing one shared impact-color sample.</summary>
    public readonly record struct Vertex(Vector2 Position, float Alpha, int Sample);

    /// <summary>Ignores fresh network arrays when contour coordinates have not changed.</summary>
    public static bool ContoursEqual(Vector2[][] first, Vector2[][] second)
    {
        if (ReferenceEquals(first, second))
            return true;
        if (first.Length != second.Length)
            return false;
        for (var i = 0; i < first.Length; i++)
        {
            if (first[i].Length != second[i].Length)
                return false;
            for (var j = 0; j < first[i].Length; j++)
            {
                if (first[i][j] != second[i][j])
                    return false;
            }
        }
        return true;
    }

    /// <summary>Builds detailed geometry or a sparse contour for distant ships.</summary>
    public WFShipShieldMesh(Vector2[][] contours, bool distant)
    {
        var initialized = false;
        foreach (var points in contours)
        {
            if (points.Length < 3)
                continue;
            var perimeter = 0f;
            for (var i = 0; i < points.Length; i++)
            {
                var start = points[i];
                var end = points[(i + 1) % points.Length];
                var length = Vector2.Distance(start, end);
                if (length < 0.001f)
                    continue;
                if (!initialized)
                {
                    Bounds = new Box2(start, start);
                    initialized = true;
                }
                Bounds = Bounds.ExtendToContain(start);
                var n0 = Normal(points, i);
                var n1 = Normal(points, (i + 1) % points.Length);
                var width = distant ? 0.3f : 1.6f;
                var sections = Math.Clamp((int) MathF.Ceiling(length / (distant ? 5f : 1.5f)), 1, 128);
                var firstSample = Samples.Count;
                for (var part = 0; part < sections; part++)
                {
                    var t0 = (float) part / sections;
                    var t1 = (float) (part + 1) / sections;
                    var a = Vector2.Lerp(start, end, t0);
                    var b = Vector2.Lerp(start, end, t1);
                    var na = EdgeNormal(start, end, n0, n1, t0);
                    var nb = EdgeNormal(start, end, n0, n1, t1);
                    var sample = Samples.Count;
                    Samples.Add((a + b) * 0.5f);
                    AddBand(a, b, na, nb, -0.12f, 0f, 0f, 0.52f, sample);
                    AddBand(a, b, na, nb, 0f, width * 0.25f, 0.52f, 0.19f, sample);
                    AddBand(a, b, na, nb, width * 0.25f, width, 0.19f, 0f, sample);
                }
                if (!distant)
                    Hexes(start, end, n0, n1, perimeter, length, firstSample, sections, width);
                perimeter += length;
            }
        }
        Bounds = new Box2(Bounds.BottomLeft - new Vector2(2f), Bounds.TopRight + new Vector2(2f));
    }

    /// <summary>Limits inward joins to avoid triangle folds on short or sharply curved edges.</summary>
    public static Vector2 Normal(Vector2[] points, int index)
    {
        var previous = points[index] - points[(index + points.Length - 1) % points.Length];
        var next = points[(index + 1) % points.Length] - points[index];
        if (previous.LengthSquared() < 0.0001f || next.LengthSquared() < 0.0001f)
            return Vector2.Zero;
        var adjacent = MathF.Min(previous.Length(), next.Length());
        previous = Vector2.Normalize(previous);
        next = Vector2.Normalize(next);
        var bend = MathF.Sqrt(MathF.Max(0f, 1f - Vector2.Dot(previous, next)));
        var limit = bend < 0.0001f ? 1f : MathF.Min(1f, adjacent * 0.5f / (1.6f * bend));
        var sum = new Vector2(-previous.Y - next.Y, previous.X + next.X);
        return sum.LengthSquared() < 0.0001f ? Vector2.Zero : Vector2.Normalize(sum) * limit;
    }

    /// <summary>Restores full width along straight edges while tapering only near tight corner joins.</summary>
    public static Vector2 EdgeNormal(Vector2 start, Vector2 end, Vector2 startNormal, Vector2 endNormal, float fraction)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length < 0.001f)
            return Vector2.Zero;
        var straight = new Vector2(-delta.Y, delta.X) / length;
        var nearStart = MathF.Max(0f, 1f - fraction * length / 2.4f);
        var nearEnd = MathF.Max(0f, 1f - (1f - fraction) * length / 2.4f);
        if (nearStart + nearEnd > 1f)
            return Vector2.Lerp(startNormal, endNormal, fraction);
        return straight * (1f - nearStart - nearEnd) + startNormal * nearStart + endNormal * nearEnd;
    }

    private void AddBand(Vector2 a, Vector2 b, Vector2 na, Vector2 nb, float outer, float inner,
        float outerAlpha, float innerAlpha, int sample)
    {
        var a0 = new Vertex(a + na * outer, outerAlpha, sample);
        var b0 = new Vertex(b + nb * outer, outerAlpha, sample);
        var a1 = new Vertex(a + na * inner, innerAlpha, sample);
        var b1 = new Vertex(b + nb * inner, innerAlpha, sample);
        Triangles.Add(a0); Triangles.Add(b0); Triangles.Add(a1);
        Triangles.Add(b0); Triangles.Add(b1); Triangles.Add(a1);
    }

    private void Hexes(Vector2 start, Vector2 end, Vector2 n0, Vector2 n1, float perimeter,
        float length, int firstSample, int sections, float width)
    {
        const float radius = 0.85f;
        const float step = radius * 1.5f;
        const float height = radius * 1.7320508f;
        var first = (int) MathF.Floor((perimeter - radius) / step);
        var last = (int) MathF.Ceiling((perimeter + length + radius) / step);
        for (var column = first; column <= last; column++)
        for (var row = -1; row <= 1; row++)
        {
            var center = new Vector2(column * step, row * height + (column % 2 == 0 ? 0f : height * 0.5f));
            for (var edge = 0; edge < 3; edge++)
            {
                var angleA = edge * MathF.PI / 3f;
                var angleB = (edge + 1) * MathF.PI / 3f;
                var a = center + new Vector2(MathF.Cos(angleA), MathF.Sin(angleA)) * radius;
                var b = center + new Vector2(MathF.Cos(angleB), MathF.Sin(angleB)) * radius;
                if (!Clip(ref a, ref b, perimeter, perimeter + length, 0.04f, width))
                    continue;
                var fraction = Math.Clamp(((a.X + b.X) * 0.5f - perimeter) / length, 0f, 0.9999f);
                var sample = firstSample + (int) (fraction * sections);
                HexLines.Add(new Vertex(Point(a), 0.24f, sample));
                HexLines.Add(new Vertex(Point(b), 0.24f, sample));
            }
        }
        return;
        Vector2 Point(Vector2 point)
        {
            var fraction = (point.X - perimeter) / length;
            return Vector2.Lerp(start, end, fraction) + EdgeNormal(start, end, n0, n1, fraction) * point.Y;
        }
    }

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
}
