namespace Content.Client._WF.Roofs;

/// <summary>Which roofed tiles lift for someone standing under them: the room they're in, up to its walls.</summary>
public static class WFRoofReveal
{
    private static readonly Vector2i[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

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
