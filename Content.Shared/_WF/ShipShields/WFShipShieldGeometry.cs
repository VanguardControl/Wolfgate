using System.Numerics;
using System.Linq;

namespace Content.Shared._WF.ShipShields;

/// <summary>Builds the exterior of a padded union of occupied hull tiles.</summary>
public static class WFShipShieldGeometry
{
    /// <summary>Returns counterclockwise exterior loops without repeated closing vertices.</summary>
    public static Vector2[][] CreateContours(IEnumerable<Vector2i> tiles, float tileSize = 1f, int padding = 6)
    {
        var occupied = new HashSet<Vector2i>();
        foreach (var tile in tiles)
        {
            for (var x = -padding; x <= padding; x++)
            for (var y = -padding; y <= padding; y++)
            {
                if (x * x + y * y <= padding * padding)
                    occupied.Add(tile + new Vector2i(x, y));
            }
        }

        var edges = new Dictionary<Vector2i, List<Vector2i>>();
        void Add(Vector2i start, Vector2i end)
        {
            if (!edges.TryGetValue(start, out var ends))
                edges[start] = ends = new List<Vector2i>();
            ends.Add(end);
        }

        foreach (var cell in occupied)
        {
            var right = cell + new Vector2i(1, 0);
            var top = cell + new Vector2i(0, 1);
            var corner = cell + new Vector2i(1, 1);
            if (!occupied.Contains(cell + new Vector2i(0, -1))) Add(cell, right);
            if (!occupied.Contains(right)) Add(right, corner);
            if (!occupied.Contains(top)) Add(corner, top);
            if (!occupied.Contains(cell + new Vector2i(-1, 0))) Add(top, cell);
        }

        var starts = edges.Keys.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        var contours = new List<Vector2[]>();
        foreach (var start in starts)
        {
            while (edges.TryGetValue(start, out var first) && first.Count > 0)
            {
                var loop = new List<Vector2i>();
                var current = start;
                var previous = start + new Vector2i(-1, 0);
                do
                {
                    loop.Add(current);
                    if (!edges.TryGetValue(current, out var nexts) || nexts.Count == 0)
                        break;
                    // At touching corners, turn left to keep each occupied region on the left.
                    var direction = current - previous;
                    var next = nexts.OrderByDescending(n => direction.X * (n.Y - current.Y) - direction.Y * (n.X - current.X)).First();
                    nexts.Remove(next);
                    previous = current;
                    current = next;
                } while (current != start);

                if (current != start || loop.Count < 3)
                    continue;
                var area = 0L;
                var simplified = new List<Vector2>();
                for (var i = 0; i < loop.Count; i++)
                {
                    var a = loop[(i + loop.Count - 1) % loop.Count];
                    var b = loop[i];
                    var c = loop[(i + 1) % loop.Count];
                    area += (long)b.X * c.Y - (long)c.X * b.Y;
                    if ((b.X - a.X) * (c.Y - b.Y) != (b.Y - a.Y) * (c.X - b.X))
                        simplified.Add(new Vector2(b.X, b.Y) * tileSize);
                }
                if (area > 0 && simplified.Count >= 3)
                {
                    var outline = padding >= 2 ? SimplifyLoop(simplified, tileSize * 1.25f) : simplified;
                    var rounded = RoundCorners(outline, tileSize * Math.Min(3f, padding * 0.5f));
                    if (HasCrossings(rounded, tileSize * 8f))
                        rounded = simplified.ToArray();
                    contours.Add(rounded);
                }
            }
        }
        return contours.ToArray();
    }

    /// <summary>Removes tile stair steps within a fixed distance of the traced hull.</summary>
    private static List<Vector2> SimplifyLoop(List<Vector2> points, float tolerance)
    {
        var opposite = 1;
        for (var i = 2; i < points.Count; i++)
        {
            if (Vector2.DistanceSquared(points[0], points[i]) > Vector2.DistanceSquared(points[0], points[opposite]))
                opposite = i;
        }
        var closed = new List<Vector2>(points);
        closed.Add(points[0]);
        var keep = new bool[closed.Count];
        keep[0] = keep[opposite] = keep[^1] = true;
        var pending = new Stack<(int Start, int End)>();
        pending.Push((0, opposite));
        pending.Push((opposite, points.Count));
        while (pending.TryPop(out var range))
        {
            var a = closed[range.Start];
            var edge = closed[range.End] - a;
            var longest = tolerance * tolerance;
            var split = -1;
            for (var i = range.Start + 1; i < range.End; i++)
            {
                var projected = a + edge * Math.Clamp(Vector2.Dot(closed[i] - a, edge) / edge.LengthSquared(), 0f, 1f);
                var distance = Vector2.DistanceSquared(closed[i], projected);
                if (distance <= longest)
                    continue;
                longest = distance;
                split = i;
            }
            if (split < 0)
                continue;
            keep[split] = true;
            pending.Push((range.Start, split));
            pending.Push((split, range.End));
        }
        var result = new List<Vector2>();
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i])
                result.Add(points[i]);
        }
        return result.Count >= 3 ? result : points;
    }

    /// <summary>Rounds corners with short quadratic arcs while keeping straight hull runs compact.</summary>
    private static Vector2[] RoundCorners(List<Vector2> points, float maximumInset)
    {
        if (maximumInset <= 0f)
            return points.ToArray();
        var rounded = new List<Vector2>();
        for (var i = 0; i < points.Count; i++)
        {
            var vertex = points[i];
            var incoming = points[(i + points.Count - 1) % points.Count] - vertex;
            var outgoing = points[(i + 1) % points.Count] - vertex;
            var inset = Math.Min(maximumInset, Math.Min(incoming.Length(), outgoing.Length()) * 0.4f);
            var start = vertex + Vector2.Normalize(incoming) * inset;
            var end = vertex + Vector2.Normalize(outgoing) * inset;
            for (var step = 0; step <= 6; step++)
            {
                var t = step / 6f;
                rounded.Add((1f - t) * (1f - t) * start + 2f * (1f - t) * t * vertex + t * t * end);
            }
        }
        return rounded.ToArray();
    }
    /// <summary>Rejects smoothing that would cross a narrow concavity.</summary>
    private static bool HasCrossings(Vector2[] points, float bucketSize)
    {
        var buckets = new Dictionary<Vector2i, List<int>>();
        var checkedEdges = new HashSet<int>();
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            var min = Vector2.Min(a, b) / bucketSize;
            var max = Vector2.Max(a, b) / bucketSize;
            checkedEdges.Clear();
            for (var x = (int)MathF.Floor(min.X); x <= (int)MathF.Floor(max.X); x++)
            for (var y = (int)MathF.Floor(min.Y); y <= (int)MathF.Floor(max.Y); y++)
            {
                var key = new Vector2i(x, y);
                if (!buckets.TryGetValue(key, out var edges))
                    buckets[key] = edges = new List<int>();
                foreach (var other in edges)
                {
                    if (other == i - 1 || i == points.Length - 1 && other == 0 || !checkedEdges.Add(other))
                        continue;
                    var c = points[other];
                    var d = points[(other + 1) % points.Length];
                    var ab = b - a;
                    var cd = d - c;
                    var denominator = ab.X * cd.Y - ab.Y * cd.X;
                    if (Math.Abs(denominator) < 0.00001f)
                        continue;
                    var offset = c - a;
                    var t = (offset.X * cd.Y - offset.Y * cd.X) / denominator;
                    var u = (offset.X * ab.Y - offset.Y * ab.X) / denominator;
                    if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
                        return true;
                }
                edges.Add(i);
            }
        }
        return false;
    }
    /// <summary>Projects an impact onto the closest shield edge.</summary>
    public static Vector2 ClosestPoint(Vector2[][] contours, Vector2 point)
    {
        var closest = point;
        var distance = float.PositiveInfinity;
        foreach (var contour in contours)
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i];
            var edge = contour[(i + 1) % contour.Length] - a;
            var projected = a + edge * Math.Clamp(Vector2.Dot(point - a, edge) / edge.LengthSquared(), 0f, 1f);
            var candidate = Vector2.DistanceSquared(projected, point);
            if (candidate >= distance)
                continue;
            closest = projected;
            distance = candidate;
        }
        return closest;
    }
}
