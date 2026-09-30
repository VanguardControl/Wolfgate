using System.Numerics;
using System.Linq;

namespace Content.Shared._WF.ShipShields;

/// <summary>Builds the exterior of a padded union of occupied hull tiles.</summary>
public static class WFShipShieldGeometry
{
    /// <summary>Returns counterclockwise exterior loops without repeated closing vertices.</summary>
    public static Vector2[][] CreateContours(IEnumerable<Vector2i> tiles, float tileSize = 1f, int padding = 3)
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
                    var beveled = new List<Vector2>();
                    for (var i = 0; i < simplified.Count; i++)
                    {
                        var vertex = simplified[i];
                        var incoming = simplified[(i + simplified.Count - 1) % simplified.Count] - vertex;
                        var outgoing = simplified[(i + 1) % simplified.Count] - vertex;
                        var inset = Math.Min(tileSize * 0.35f, Math.Min(incoming.Length(), outgoing.Length()) * 0.25f);
                        beveled.Add(vertex + Vector2.Normalize(incoming) * inset);
                        beveled.Add(vertex + Vector2.Normalize(outgoing) * inset);
                    }
                    contours.Add(beveled.ToArray());
                }
            }
        }
        return contours.ToArray();
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
