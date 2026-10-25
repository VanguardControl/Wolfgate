namespace Content.Client._WF.Roofs;

/// <summary>Which roofed tiles lift for someone standing under them: the room they're in, up to its walls.</summary>
public static class WFRoofReveal
{
    private static readonly Vector2i[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>
    /// The roofed part of a vessel: floor that the open outside can't reach without passing a wall, window or door. The
    /// walls themselves and anything open to space or the sky, such as exterior plating, catwalks and lattice, stay bare.
    /// </summary>
    public static void Enclosed(HashSet<Vector2i> floor, Func<Vector2i, bool> open, Func<Vector2i, bool> solid, HashSet<Vector2i> into)
    {
        into.Clear();
        var walls = new HashSet<Vector2i>();
        foreach (var tile in floor)
        {
            if (solid(tile))
                walls.Add(tile);
        }

        // Out from every bit of floor at the hull's edge or open to the outside.
        var outside = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();
        foreach (var tile in floor)
        {
            if (walls.Contains(tile))
                continue;
            var edge = open(tile);
            foreach (var step in Neighbours)
                edge |= !floor.Contains(tile + step);
            if (edge && outside.Add(tile))
                queue.Enqueue(tile);
        }
        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            foreach (var step in Neighbours)
            {
                var next = tile + step;
                if (floor.Contains(next) && !walls.Contains(next) && outside.Add(next))
                    queue.Enqueue(next);
            }
        }

        foreach (var tile in floor)
        {
            if (!walls.Contains(tile) && !outside.Contains(tile))
                into.Add(tile);
        }
    }

    /// <summary>
    /// Whether a roof is drawn over a tile: it is roofed, nothing structural stands on it and it isn't lifted. Walls,
    /// windows and doors are never covered, so a building shows its walls and doors with roof only over its floors.
    /// </summary>
    public static bool Covers(bool roofed, bool structure, bool lifted)
    {
        return roofed && !structure && !lifted;
    }

    /// <summary>
    /// Fills out from <paramref name="start"/> through roofed tiles that nothing solid stands on. The walls it meets
    /// lift with the room so they show from inside; open ground and anything past a wall stay as they are.
    /// </summary>
    public static void Fill(Vector2i start, Func<Vector2i, bool> roofed, Func<Vector2i, bool> solid, int cap, HashSet<Vector2i> into)
    {
        into.Clear();
        if (!roofed(start))
            return;

        var open = new Queue<Vector2i>();
        open.Enqueue(start);
        into.Add(start);
        while (open.Count > 0 && into.Count < cap)
        {
            var tile = open.Dequeue();
            foreach (var step in Neighbours)
            {
                var next = tile + step;
                if (into.Contains(next) || !roofed(next))
                    continue;
                into.Add(next);
                if (!solid(next))
                    open.Enqueue(next);
            }
        }
    }
}
