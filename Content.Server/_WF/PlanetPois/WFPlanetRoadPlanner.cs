using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.PlanetPois;
using Robust.Shared.Maths;

namespace Content.Server._WF.PlanetPois;

/// <summary>A reserved site and its absolute road entrances.</summary>
public sealed record WFPlanetRoadSite(Box2i Bounds, WFPlanetPoiConnection Connection, IReadOnlyList<WFPlanetRoadSocket> Sockets);

/// <summary>An absolute entrance tile with an outward direction.</summary>
public sealed record WFPlanetRoadSocket(Vector2i Position, WFPlanetPoiDirection Direction);

/// <summary>A connected road network, or an explicit routing failure with no paintable tiles.</summary>
public sealed record WFPlanetRoadPlan(bool Success, string? Error, HashSet<Vector2i> RoadTiles, HashSet<Vector2i> ShoulderTiles)
{
    /// <summary>The tiles down the middle of the routed roads; the short approaches into the sites are not among them.</summary>
    public HashSet<Vector2i> Centreline { get; init; } = new();

    /// <summary>On a failure that is one entrance's alone, that entrance: the rest may route without its site.</summary>
    public Vector2i? FailedEntrance { get; init; }

    /// <summary>The centreline of the arterial rings and trunks laid before any site was joined; empty without them.</summary>
    public HashSet<Vector2i> Backbone { get; init; } = new();
}

/// <summary>
/// Routes a world's roads without crossing reserved buildings, pinned terrain or water. On a world big enough, an
/// arterial network goes down first: an inner ring round the landing zone, an outer ring short of the world's edge, and
/// straight trunks between them, each an octagon of long straight sides with 45° corners. Each site then joins the
/// nearest road by a spur that keeps straight and turns square. A smaller world is just the spurs, site to site.
/// </summary>
public static class WFPlanetRoadPlanner
{
    /// <summary>A* expansions one route may use.</summary>
    private const int SearchLimit = 100000;

    /// <summary>A* expansions the whole plan may use.</summary>
    private const int TotalSearchLimit = 1500000;

    /// <summary>What a turn costs a route over a straight step, so routes run straight and turn rarely.</summary>
    private const int TurnCost = 6;

    /// <summary>What a step costs beside a road the route hasn't joined, so spurs meet roads square, not alongside.</summary>
    private const int HugCost = 4;

    /// <summary>Tiles between the landing zone's edge and the inner ring's road.</summary>
    private const int RingGap = 12;

    /// <summary>Tiles kept between the outer ring's road and the world's edge.</summary>
    private const int EdgeGap = 12;

    /// <summary>The fewest tiles between the two rings; a world without room for both gets no arterial network.</summary>
    private const int MinRingSpacing = 48;

    /// <summary>A band between the rings at least this wide gets two trunks a side instead of one.</summary>
    private const int TwinTrunkBand = 160;

    /// <summary>How much of each half-side a ring's corner cuts off at 45°.</summary>
    private const float ChamferRatio = 0.3f;

    /// <summary>A piece of ring or trunk cut off this short by an obstacle is dropped rather than joined back.</summary>
    private const int MinPiece = 24;

    private static readonly Vector2i[] Steps = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

    /// <summary>
    /// Plans all entrances atomically; a failed entrance rejects the complete network. <paramref name="impassable"/>
    /// marks more ground no road may cross, such as water, and <paramref name="backbone"/> turns the arterial network
    /// off.
    /// </summary>
    public static WFPlanetRoadPlan Plan(Vector2 centre, float radius, float centreClear,
        IReadOnlyList<WFPlanetRoadSite> sites, IReadOnlySet<Vector2i> blocked,
        int width, int shoulderWidth, int seed, Func<Vector2i, bool>? impassable = null, bool backbone = true)
    {
        WFPlanetRoadPlan Fail(string reason, Vector2i? entrance = null) =>
            new(false, reason, new(), new()) { FailedEntrance = entrance };
        if (!float.IsFinite(radius) || !float.IsFinite(centreClear) || !float.IsFinite(centre.X) || !float.IsFinite(centre.Y)
            || width < 1 || width > 15 || width % 2 == 0 || shoulderWidth < 0 || shoulderWidth > 8 || radius <= 0 || radius > 4096 || centreClear < 0)
            return Fail("Invalid road dimensions or world bounds.");

        var low = -(width / 2);
        var high = low + width - 1;
        var clearance = Math.Max(-low, high) + shoulderWidth;
        var entrances = new List<WFPlanetRoadSocket>();
        var approaches = new Dictionary<WFPlanetRoadSite, HashSet<Vector2i>>();
        foreach (var site in sites)
        {
            if (site.Bounds.Right <= site.Bounds.Left || site.Bounds.Top <= site.Bounds.Bottom || !Enum.IsDefined(site.Connection))
                return Fail("A site has invalid reserved bounds or connection mode.");
            if (site.Connection == WFPlanetPoiConnection.Isolated)
                continue;
            IReadOnlyList<WFPlanetRoadSocket> sockets = site.Connection == WFPlanetPoiConnection.Through ? site.Sockets : site.Sockets.Take(1).ToArray();
            if (sockets.Count == 0 || site.Connection == WFPlanetPoiConnection.Through && (sockets.Select(s => s.Position).Distinct().Count() < 2 || sockets.Select(s => s.Direction).Distinct().Count() < 2))
                return Fail("A connected site has insufficient distinct entrances.");
            var siteApproaches = new HashSet<Vector2i>();
            if (!approaches.TryAdd(site, siteApproaches))
                return Fail("A connected site is listed more than once.");
            foreach (var socket in sockets)
            {
                if (!Enum.IsDefined(socket.Direction))
                    return Fail("An entrance has an invalid outward direction.");
                var direction = Direction(socket.Direction);
                // A socket may start on the outermost reserved tile, never deep inside a building.
                if (!Inside(socket.Position, site.Bounds) || Inside(socket.Position + direction, site.Bounds))
                    return Fail("An entrance does not face out of its reserved footprint.");
                entrances.Add(socket);
                for (var step = 0; step <= clearance + 1; step++)
                {
                    var point = socket.Position + direction * step;
                    for (var side = -clearance; side <= clearance; side++)
                        siteApproaches.Add(point + new Vector2i(-direction.Y, direction.X) * side);
                }
            }
        }

        var tileCache = new Dictionary<Vector2i, bool>();
        bool TileAllowed(Vector2i tile)
        {
            if (tileCache.TryGetValue(tile, out var known))
                return known;
            var position = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
            var distance = Vector2.Distance(position, centre);
            // Include the complete tile square inside the world circle.
            var allowed = !(distance + 0.707107f > radius || centreClear > 0 && distance - 0.707107f < centreClear || blocked.Contains(tile))
                          && sites.All(site => !Inside(tile, site.Bounds) || approaches.TryGetValue(site, out var near) && near.Contains(tile))
                          && impassable?.Invoke(tile) != true;
            return tileCache[tile] = allowed;
        }
        var clearanceCache = new Dictionary<Vector2i, bool>();
        bool Allowed(Vector2i point)
        {
            if (clearanceCache.TryGetValue(point, out var cached))
                return cached;
            for (var x = low - shoulderWidth; x <= high + shoulderWidth; x++)
            for (var y = low - shoulderWidth; y <= high + shoulderWidth; y++)
                if (!TileAllowed(point + new Vector2i(x, y)))
                    return clearanceCache[point] = false;
            return clearanceCache[point] = true;
        }

        var random = new Random(seed);
        var directions = new[] { 0, 1, 2, 3 };
        for (var i = directions.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (directions[i], directions[j]) = (directions[j], directions[i]);
        }
        var budget = TotalSearchLimit;
        var network = new HashSet<Vector2i>();
        // Tiles under a road's width and shoulders, for the cost of running alongside one.
        var beside = new HashSet<Vector2i>();
        void Join(IEnumerable<Vector2i> points)
        {
            foreach (var point in points)
            {
                if (!network.Add(point))
                    continue;
                for (var x = -clearance; x <= clearance; x++)
                for (var y = -clearance; y <= clearance; y++)
                    beside.Add(point + new Vector2i(x, y));
            }
        }

        var arterial = backbone ? Backbone(centre, radius, centreClear, clearance, random, directions, Allowed, ref budget) : new HashSet<Vector2i>();
        Join(arterial);

        foreach (var socket in entrances)
        {
            // Keep the complete width outside the building after the approach.
            var direction = Direction(socket.Direction);
            var start = socket.Position + direction * (clearance + 1);
            for (var step = 0; step <= clearance + 1; step++)
            {
                var point = socket.Position + direction * step;
                // At the entrance, width runs across the socket; it must not paint back into the building.
                for (var side = low - shoulderWidth; side <= high + shoulderWidth; side++)
                {
                    var lane = point + new Vector2i(-direction.Y, direction.X) * side;
                    if (!TileAllowed(lane))
                        return Fail($"The approach from the entrance at {socket.Position} is blocked or outside the world at {lane}.", socket.Position);
                }
            }
            if (!Allowed(start))
                return Fail($"The entrance at {socket.Position} lacks road clearance at {start}.", socket.Position);
            if (network.Count > 0 && !network.Contains(start))
            {
                var goal = Nearest(network, start);
                var spur = Route(new[] { start }, network.Contains, goal, Allowed,
                    tile => beside.Contains(tile) && !network.Contains(tile) ? HugCost : 0, directions, SearchLimit, ref budget);
                if (spur == null)
                    return Fail($"The entrance at {socket.Position} cannot reach the road network within the routing budget.", socket.Position);
                Join(spur);
            }
            Join(new[] { start });
            // Approach centerlines are painted separately and never used as routing targets.
        }
        if (network.Count == 0)
            return Fail("No road could be laid.");

        var roads = new HashSet<Vector2i>();
        var shoulders = new HashSet<Vector2i>();
        foreach (var point in network)
        {
            for (var x = low - shoulderWidth; x <= high + shoulderWidth; x++)
            for (var y = low - shoulderWidth; y <= high + shoulderWidth; y++)
            {
                var tile = point + new Vector2i(x, y);
                if (!TileAllowed(tile))
                    return Fail("The completed network violates its clearance.");
                if (x >= low && x <= high && y >= low && y <= high) roads.Add(tile);
                else shoulders.Add(tile);
            }
        }
        foreach (var socket in entrances)
        {
            var direction = Direction(socket.Direction);
            for (var step = 0; step <= clearance + 1; step++)
            for (var side = low - shoulderWidth; side <= high + shoulderWidth; side++)
            {
                var tile = socket.Position + direction * step + new Vector2i(-direction.Y, direction.X) * side;
                if (side >= low && side <= high) roads.Add(tile);
                else shoulders.Add(tile);
            }
        }
        shoulders.ExceptWith(roads);
        return new(true, null, roads, shoulders) { Centreline = network, Backbone = arterial };
    }

    /// <summary>
    /// Lays the arterial network, connected: both rings and the trunks between them, routed round whatever is in the
    /// way. A stretch that can't get round is cut, and a piece cut off from the rest that can't be joined back is
    /// dropped. Empty on a world with no room for both rings.
    /// </summary>
    private static HashSet<Vector2i> Backbone(Vector2 centre, float radius, float centreClear, int clearance, Random random,
        int[] directions, Func<Vector2i, bool> allowed, ref int budget)
    {
        var result = new HashSet<Vector2i>();
        var origin = new Vector2i((int) MathF.Floor(centre.X), (int) MathF.Floor(centre.Y));
        // Whole tiles off the true centre, so the network moves with the world.
        var limit = radius - clearance - EdgeGap - 1f;
        bool Fits(int half) => MathF.Sqrt(half * half + (float) Math.Pow(half - Chamfer(half), 2)) <= limit;
        var inner = (int) MathF.Ceiling(centreClear) + clearance + RingGap + random.Next(0, 6);
        var outer = (int) MathF.Floor(limit / MathF.Sqrt(1f + (1f - ChamferRatio) * (1f - ChamferRatio))) + 1;
        while (outer > inner && !Fits(outer))
            outer--;
        outer -= random.Next(0, 6);
        if (!Fits(inner) || outer - inner < MinRingSpacing)
            return result;

        var pieces = new List<List<Vector2i>>();
        foreach (var half in new[] { inner, outer })
            pieces.AddRange(Lay(Octagon(origin, half), true, allowed, directions, ref budget));

        // Trunks run square out of a ring's straight sides, so they meet both rings at a T.
        var straight = Math.Min(inner - Chamfer(inner), outer - Chamfer(outer)) - clearance - 2;
        var twin = outer - inner >= TwinTrunkBand;
        for (var side = 0; side < 4; side++)
        {
            var offsets = new List<int>();
            if (twin)
            {
                var spread = straight * 0.6f;
                offsets.Add((int) MathF.Round(-spread + (float) (random.NextDouble() - 0.5) * straight * 0.3f));
                offsets.Add((int) MathF.Round(spread + (float) (random.NextDouble() - 0.5) * straight * 0.3f));
            }
            else
                offsets.Add((int) MathF.Round((float) (random.NextDouble() - 0.5) * straight));
            foreach (var offset in offsets)
            {
                var trunk = new List<Vector2i>();
                for (var along = inner; along <= outer; along++)
                {
                    trunk.Add(origin + side switch
                    {
                        0 => new Vector2i(along, offset),
                        1 => new Vector2i(offset, along),
                        2 => new Vector2i(-along, offset),
                        _ => new Vector2i(offset, -along),
                    });
                }
                pieces.AddRange(Lay(trunk, false, allowed, directions, ref budget));
            }
        }

        foreach (var piece in pieces)
            result.UnionWith(piece);

        // Join every piece cut off from the largest back to it, or drop it.
        var components = Components(result).OrderByDescending(c => c.Count).ThenBy(c => Lowest(c).X).ThenBy(c => Lowest(c).Y).ToList();
        if (components.Count == 0)
            return result;
        var network = components[0];
        foreach (var component in components.Skip(1))
        {
            if (component.Count < MinPiece)
                continue;
            var starts = component.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
            var link = Route(starts, network.Contains, Nearest(network, starts[0]), allowed, _ => 0, directions, SearchLimit, ref budget);
            if (link == null)
                continue;
            network.UnionWith(component);
            network.UnionWith(link);
        }
        return network;
    }

    /// <summary>Lays a ring or trunk's tiles, detouring round what blocks it; the pieces left where a detour failed.</summary>
    private static List<List<Vector2i>> Lay(List<Vector2i> line, bool loop, Func<Vector2i, bool> allowed, int[] directions, ref int budget)
    {
        var pieces = new List<List<Vector2i>>();
        var count = line.Count;
        var open = line.Select(allowed).ToArray();
        if (!open.Contains(true))
            return pieces;
        if (!open.Contains(false))
        {
            pieces.Add(new List<Vector2i>(line));
            return pieces;
        }

        if (loop)
        {
            // Start just after a blocked stretch, and come back round to the start to close the loop.
            var first = Enumerable.Range(0, count).First(i => open[i] && !open[(i + count - 1) % count]);
            line = line.Skip(first).Concat(line.Take(first)).Append(line[first]).ToList();
            open = line.Select(allowed).ToArray();
            count = line.Count;
        }

        var piece = new List<Vector2i>();
        var index = 0;
        while (index < count)
        {
            if (open[index])
            {
                piece.Add(line[index]);
                index++;
                continue;
            }
            var resume = index;
            while (resume < count && !open[resume])
                resume++;
            if (resume >= count || index == 0)
            {
                // A blocked end: the trunk stops short of it.
                if (piece.Count > 0)
                    pieces.Add(piece);
                piece = new List<Vector2i>();
                index = resume;
                continue;
            }
            var target = line[resume];
            var detour = Route(new[] { line[index - 1] }, tile => tile == target, target, allowed, _ => 0, directions, SearchLimit / 2, ref budget);
            if (detour != null)
                piece.AddRange(detour);
            else
            {
                pieces.Add(piece);
                piece = new List<Vector2i>();
            }
            index = resume;
        }
        if (piece.Count > 0)
            pieces.Add(piece);
        return pieces;
    }

    /// <summary>The tiles round an octagon about <paramref name="origin"/>: straight sides <paramref name="half"/> out, corners cut at 45°.</summary>
    private static List<Vector2i> Octagon(Vector2i origin, int half)
    {
        var cut = half - Chamfer(half);
        var corners = new[]
        {
            new Vector2i(half, -cut), new Vector2i(half, cut), new Vector2i(cut, half), new Vector2i(-cut, half),
            new Vector2i(-half, cut), new Vector2i(-half, -cut), new Vector2i(-cut, -half), new Vector2i(cut, -half),
        };
        var tiles = new List<Vector2i>();
        for (var i = 0; i < corners.Length; i++)
        {
            var from = corners[i];
            var to = corners[(i + 1) % corners.Length];
            var point = from;
            // Diagonals go as a staircase, so every tile touches the next side on.
            var stepX = Math.Sign(to.X - from.X);
            var stepY = Math.Sign(to.Y - from.Y);
            var horizontal = true;
            while (point != to)
            {
                tiles.Add(origin + point);
                if (point.X != to.X && (horizontal || point.Y == to.Y))
                    point += new Vector2i(stepX, 0);
                else
                    point += new Vector2i(0, stepY);
                horizontal = !horizontal;
            }
        }
        return tiles;
    }

    private static int Chamfer(int half) => (int) MathF.Round(half * ChamferRatio);

    /// <summary>
    /// The cheapest route from any start to a goal tile over allowed tiles, where turning and <paramref name="extra"/>
    /// cost more than going straight; null when none is found within the budgets.
    /// </summary>
    private static List<Vector2i>? Route(IReadOnlyList<Vector2i> starts, Func<Vector2i, bool> goal, Vector2i aim,
        Func<Vector2i, bool> allowed, Func<Vector2i, int> extra, int[] directions, int limit, ref int budget)
    {
        var frontier = new PriorityQueue<(Vector2i Tile, int Heading), (int Cost, int Order)>();
        var costs = new Dictionary<(Vector2i, int), int>();
        var previous = new Dictionary<(Vector2i, int), (Vector2i, int)>();
        var order = 0;
        int Estimate(Vector2i tile) => Math.Abs(tile.X - aim.X) + Math.Abs(tile.Y - aim.Y);
        foreach (var start in starts)
        {
            if (!costs.TryAdd((start, -1), 0))
                continue;
            frontier.Enqueue((start, -1), (Estimate(start), order++));
        }

        var visited = 0;
        while (frontier.TryDequeue(out var state, out var priority))
        {
            var cost = costs[state];
            if (priority.Cost - Estimate(state.Tile) > cost)
                continue;
            if (goal(state.Tile))
            {
                var path = new List<Vector2i> { state.Tile };
                for (var at = state; previous.TryGetValue(at, out var back); at = back)
                    path.Add(back.Item1);
                path.Reverse();
                return path;
            }
            if (visited++ >= limit || budget-- <= 0)
                return null;

            foreach (var heading in directions)
            {
                // Never straight back the way it came.
                if (state.Heading >= 0 && heading == (state.Heading + 2) % 4)
                    continue;
                var next = state.Tile + Steps[heading];
                if (!allowed(next))
                    continue;
                var nextCost = cost + 1 + (state.Heading >= 0 && heading != state.Heading ? TurnCost : 0) + extra(next);
                var key = (next, heading);
                if (costs.TryGetValue(key, out var old) && old <= nextCost)
                    continue;
                costs[key] = nextCost;
                previous[key] = state;
                frontier.Enqueue(key, (nextCost + Estimate(next), order++));
            }
        }
        return null;
    }

    /// <summary>The tile of a set nearest another, ties broken the same way wherever the world is.</summary>
    private static Vector2i Nearest(IEnumerable<Vector2i> tiles, Vector2i to)
    {
        var best = default(Vector2i);
        var bestDistance = int.MaxValue;
        foreach (var tile in tiles)
        {
            var distance = Math.Abs(tile.X - to.X) + Math.Abs(tile.Y - to.Y);
            if (distance > bestDistance || distance == bestDistance && (tile.X > best.X || tile.X == best.X && tile.Y >= best.Y))
                continue;
            best = tile;
            bestDistance = distance;
        }
        return best;
    }

    private static Vector2i Lowest(HashSet<Vector2i> tiles) => tiles.OrderBy(p => p.X).ThenBy(p => p.Y).First();

    /// <summary>The cardinally connected groups of a tile set.</summary>
    public static List<HashSet<Vector2i>> Components(IReadOnlySet<Vector2i> tiles)
    {
        var groups = new List<HashSet<Vector2i>>();
        var unseen = new HashSet<Vector2i>(tiles);
        while (unseen.Count > 0)
        {
            var first = unseen.First();
            unseen.Remove(first);
            var group = new HashSet<Vector2i> { first };
            var pending = new Queue<Vector2i>();
            pending.Enqueue(first);
            while (pending.TryDequeue(out var tile))
            {
                foreach (var step in Steps)
                {
                    if (!unseen.Remove(tile + step))
                        continue;
                    group.Add(tile + step);
                    pending.Enqueue(tile + step);
                }
            }
            groups.Add(group);
        }
        return groups;
    }

    private static bool Inside(Vector2i p, Box2i bounds) => p.X >= bounds.Left && p.X < bounds.Right && p.Y >= bounds.Bottom && p.Y < bounds.Top;

    /// <summary>Converts an entrance direction to its unit tile offset.</summary>
    public static Vector2i Direction(WFPlanetPoiDirection direction) => direction switch
    {
        WFPlanetPoiDirection.North => new(0, 1),
        WFPlanetPoiDirection.East => new(1, 0),
        WFPlanetPoiDirection.South => new(0, -1),
        WFPlanetPoiDirection.West => new(-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };
}
