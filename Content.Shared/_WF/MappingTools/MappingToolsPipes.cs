using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingTools;

/// <summary>
/// How a pipe draw lays its pipes: the layer, the colour (or <see cref="AutoColor"/> to match the first pipe it
/// joins) and whether it joins or rebuilds pipes already there.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingPipeSpec(
    AtmosPipeLayer Layer,
    Color? Color,
    bool AutoColor,
    bool AutoJoin,
    bool Replace);

/// <summary>The pipes already on a cell, on the layer being drawn: their open sides and whether they are one plain pipe.</summary>
public readonly record struct MappingExistingPipe(PipeDirection Directions, bool Plain);

/// <summary>A pipe a draw lays or rebuilds, with the sides it opens to.</summary>
public readonly record struct MappingPipeCell(Vector2i Cell, PipeDirection Directions);

/// <summary>What a pipe draw does, worked out from its path and the pipes around it.</summary>
public sealed class MappingPipePlan
{
    /// <summary>Path cells that get a pipe; a plain pipe already there is rebuilt with these sides.</summary>
    public readonly List<MappingPipeCell> Placed = new();

    /// <summary>Plain pipes off the path's ends that gain a side to join it.</summary>
    public readonly List<MappingPipeCell> Upgraded = new();

    /// <summary>Cells of pipes the path joins, in path order.</summary>
    public readonly List<Vector2i> Joined = new();

    /// <summary>Path cells left alone: a pipe is there that may not be rebuilt, or nothing connects to the cell.</summary>
    public int Kept;
}

/// <summary>
/// Pure parts of the mapping screen's pipe mode: the dragged path, the pipe shape for a set of sides, and the plan
/// that joins a path to the pipes around it.
/// </summary>
public static class MappingToolsPipes
{
    /// <summary>The four sides, clockwise from north.</summary>
    public static readonly PipeDirection[] Sides =
    {
        PipeDirection.North,
        PipeDirection.East,
        PipeDirection.South,
        PipeDirection.West,
    };

    /// <summary>The primary-layer prototype of each plain pipe shape; other layers are its alternatives.</summary>
    public static string PrototypeFor(PipeShape shape)
    {
        return shape switch
        {
            PipeShape.Half => "GasPipeHalf",
            PipeShape.Straight => "GasPipeStraight",
            PipeShape.Bend => "GasPipeBend",
            PipeShape.TJunction => "GasPipeTJunction",
            _ => "GasPipeFourway",
        };
    }

    /// <summary>The side of <paramref name="from"/> that <paramref name="to"/> touches, or None when they don't share one.</summary>
    public static PipeDirection Side(Vector2i from, Vector2i to)
    {
        var delta = to - from;
        if (delta == new Vector2i(0, 1))
            return PipeDirection.North;
        if (delta == new Vector2i(0, -1))
            return PipeDirection.South;
        if (delta == new Vector2i(1, 0))
            return PipeDirection.East;
        if (delta == new Vector2i(-1, 0))
            return PipeDirection.West;

        return PipeDirection.None;
    }

    /// <summary>The cell next to <paramref name="cell"/> on one side.</summary>
    public static Vector2i Step(Vector2i cell, PipeDirection side)
    {
        return side switch
        {
            PipeDirection.North => cell + new Vector2i(0, 1),
            PipeDirection.South => cell + new Vector2i(0, -1),
            PipeDirection.East => cell + new Vector2i(1, 0),
            PipeDirection.West => cell + new Vector2i(-1, 0),
            _ => cell,
        };
    }

    /// <summary>How many of the four sides are set.</summary>
    public static int Count(PipeDirection directions)
    {
        var count = 0;
        foreach (var side in Sides)
        {
            if (directions.HasDirection(side))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Adds the cell the cursor reached to a path. A jump is bridged with an L, along the last step's axis first;
    /// reaching a cell already on the path cuts the path back to it. False when the path would pass
    /// <paramref name="max"/> cells; it then holds what fitted.
    /// </summary>
    public static bool Extend(List<Vector2i> path, Vector2i cell, int max)
    {
        if (path.Count == 0)
        {
            if (max < 1)
                return false;

            path.Add(cell);
            return true;
        }

        var current = path[^1];
        var alongX = path.Count < 2 || path[^1].X != path[^2].X;
        while (current != cell)
        {
            var moveX = alongX ? current.X != cell.X : current.Y == cell.Y;
            var next = moveX
                ? current + new Vector2i(Math.Sign(cell.X - current.X), 0)
                : current + new Vector2i(0, Math.Sign(cell.Y - current.Y));

            var index = path.IndexOf(next);
            if (index >= 0)
            {
                path.RemoveRange(index + 1, path.Count - index - 1);
            }
            else
            {
                if (path.Count >= max)
                    return false;

                path.Add(next);
            }

            current = next;
        }

        return true;
    }

    /// <summary>
    /// The path through <paramref name="cells"/> in order, every step to a side neighbour; null when it would pass
    /// <paramref name="max"/> cells or the jumps between the cells are that long.
    /// </summary>
    public static List<Vector2i>? Normalise(IReadOnlyList<Vector2i> cells, int max)
    {
        var walk = 0L;
        for (var i = 1; i < cells.Count; i++)
        {
            var delta = cells[i] - cells[i - 1];
            walk += Math.Abs((long) delta.X) + Math.Abs((long) delta.Y);
        }

        if (walk > 4L * max)
            return null;

        var path = new List<Vector2i>();
        foreach (var cell in cells)
        {
            if (!Extend(path, cell, max))
                return null;
        }

        return path;
    }

    /// <summary>
    /// The plain pipe shape with exactly these open sides and the rotation that turns it there: half for one, straight
    /// for two opposite, bend for two adjacent, T for three and four-way for four.
    /// </summary>
    public static bool TryShape(PipeDirection directions, out PipeShape shape, out Angle rotation)
    {
        rotation = Angle.Zero;
        directions &= PipeDirection.Fourway;
        switch (Count(directions))
        {
            case 1:
                shape = PipeShape.Half;
                break;
            case 2:
                shape = directions is PipeDirection.Longitudinal or PipeDirection.Lateral
                    ? PipeShape.Straight
                    : PipeShape.Bend;
                break;
            case 3:
                shape = PipeShape.TJunction;
                break;
            case 4:
                shape = PipeShape.Fourway;
                return true;
            default:
                shape = default;
                return false;
        }

        var baseDirection = shape.ToBaseDirection();
        for (var turn = 0; turn < 4; turn++)
        {
            var angle = Angle.FromDegrees(90 * turn);
            if (baseDirection.RotatePipeDirection(angle.Theta) != directions)
                continue;

            rotation = angle;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Works out a pipe draw. Each path cell opens to its neighbours along the path. A cell holding pipes on the
    /// layer is rebuilt with its old sides added when <paramref name="replace"/> is on and it is one plain pipe, else
    /// kept. With <paramref name="autoJoin"/> a cell also opens to pipes pointing at it, kept path cells included,
    /// and each end of the path runs into the plain pipe straight past it, which gains the side it needs.
    /// </summary>
    public static MappingPipePlan Plan(IReadOnlyList<Vector2i> path, Func<Vector2i, MappingExistingPipe?> existing,
        bool autoJoin, bool replace)
    {
        var plan = new MappingPipePlan();
        var count = path.Count;
        var index = new Dictionary<Vector2i, int>(count);
        var placed = new bool[count];
        var found = new MappingExistingPipe?[count];
        var directions = new PipeDirection[count];
        var upgrades = new Dictionary<Vector2i, PipeDirection>();
        var joined = new HashSet<Vector2i>();

        void Join(Vector2i cell)
        {
            if (joined.Add(cell))
                plan.Joined.Add(cell);
        }

        for (var i = 0; i < count; i++)
        {
            index[path[i]] = i;
            found[i] = existing(path[i]);
            if (found[i] is not { } pipe)
            {
                placed[i] = true;
            }
            else if (pipe.Plain && replace)
            {
                placed[i] = true;
                directions[i] = pipe.Directions & PipeDirection.Fourway;
                Join(path[i]);
            }
        }

        MappingExistingPipe? PipeAt(Vector2i cell)
        {
            return index.TryGetValue(cell, out var at) ? found[at] : existing(cell);
        }

        for (var i = 0; i < count; i++)
        {
            if (!placed[i])
                continue;

            var cell = path[i];
            for (var j = i - 1; j <= i + 1; j += 2)
            {
                if (j < 0 || j >= count)
                    continue;

                var side = Side(cell, path[j]);
                if (placed[j])
                {
                    directions[i] |= side;
                }
                else if (autoJoin && found[j]!.Value.Directions.HasDirection(side.GetOpposite()))
                {
                    directions[i] |= side;
                    Join(path[j]);
                }
            }

            if (!autoJoin)
                continue;

            foreach (var side in Sides)
            {
                if (directions[i].HasDirection(side))
                    continue;

                var next = Step(cell, side);
                if (index.TryGetValue(next, out var at) && placed[at])
                    continue;

                if (PipeAt(next) is { } other && other.Directions.HasDirection(side.GetOpposite()))
                {
                    directions[i] |= side;
                    Join(next);
                }
            }

            if (count < 2 || i != 0 && i != count - 1)
                continue;

            var ahead = Side(i == 0 ? path[1] : path[count - 2], cell);
            var beyond = Step(cell, ahead);
            if (directions[i].HasDirection(ahead) ||
                index.ContainsKey(beyond) ||
                existing(beyond) is not { Plain: true } plain)
            {
                continue;
            }

            var current = upgrades.TryGetValue(beyond, out var upgraded) ? upgraded : plain.Directions & PipeDirection.Fourway;
            if (!upgrades.ContainsKey(beyond))
                plan.Upgraded.Add(new MappingPipeCell(beyond, PipeDirection.None));

            upgrades[beyond] = current | ahead.GetOpposite();
            directions[i] |= ahead;
            Join(beyond);
        }

        for (var i = 0; i < count; i++)
        {
            if (placed[i] && directions[i] != PipeDirection.None)
                plan.Placed.Add(new MappingPipeCell(path[i], directions[i]));
            else
                plan.Kept++;
        }

        for (var i = 0; i < plan.Upgraded.Count; i++)
        {
            var cell = plan.Upgraded[i].Cell;
            plan.Upgraded[i] = new MappingPipeCell(cell, upgrades[cell]);
        }

        return plan;
    }
}
