using System.Numerics;
using Content.Shared._WF.PlanetPois;

namespace Content.Server._WF.PlanetPois;

/// <summary>Quarter turns of an authored site: its tiles, footprint and entrances, the way the grid merge turns them.</summary>
public sealed partial class WFPlanetPoiSystem
{
    private static Angle Turns(int turns) => Angle.FromDegrees(90 * turns);

    /// <summary>Where a tile lands after the merge turns its grid by <paramref name="turns"/> quarter turns about the origin.</summary>
    internal static Vector2i TurnTile(Vector2i tile, int turns)
    {
        if (turns == 0)
            return tile;
        return Turns(turns).RotateVec(new Vector2(tile.X + 0.5f, tile.Y + 0.5f)).Floored();
    }

    internal static Box2i TurnBox(Box2i box, int turns)
    {
        if (turns == 0)
            return box;
        var a = TurnTile(new Vector2i(box.Left, box.Bottom), turns);
        var b = TurnTile(new Vector2i(box.Right - 1, box.Top - 1), turns);
        return new Box2i(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
    }

    internal static WFPlanetPoiDirection TurnDirection(WFPlanetPoiDirection direction, int turns)
    {
        var vector = Turns(turns).RotateVec((Vector2) Direction(direction));
        var rounded = new Vector2i((int) MathF.Round(vector.X), (int) MathF.Round(vector.Y));
        return rounded.X > 0 ? WFPlanetPoiDirection.East
            : rounded.X < 0 ? WFPlanetPoiDirection.West
            : rounded.Y > 0 ? WFPlanetPoiDirection.North
            : WFPlanetPoiDirection.South;
    }

    /// <summary>
    /// The grid merge turns every entity with the grid, even one that never rotates (noRot), and that one only breaks
    /// when it is deleted. Puts those back to no rotation.
    /// </summary>
    internal void UnturnFixedRotation(IEnumerable<EntityUid> entities)
    {
        foreach (var uid in entities)
        {
            if (TerminatingOrDeleted(uid) || Transform(uid) is not { NoLocalRotation: true } xform || xform.LocalRotation == Angle.Zero)
                continue;
            // Rotation writes are ignored while the flag is up; raising it again zeroes the rotation.
            _transform.SetNoLocalRotation((uid, xform), false);
            _transform.SetNoLocalRotation((uid, xform), true);
        }
    }

    /// <summary>The entry as placed after a turn: its footprint and entrances turned with the map.</summary>
    private static WFPlanetPoiEntry Turned(WFPlanetPoiEntry entry, int turns)
    {
        var sockets = new List<WFPlanetPoiSocket>();
        foreach (var socket in entry.Sockets)
        {
            sockets.Add(new WFPlanetPoiSocket
            {
                Offset = TurnTile(socket.Offset, turns),
                Direction = TurnDirection(socket.Direction, turns),
            });
        }

        return new WFPlanetPoiEntry
        {
            Dungeon = entry.Dungeon,
            MapPath = entry.MapPath,
            Footprint = entry.Footprint is { } footprint ? TurnBox(footprint, turns) : null,
            MinCount = entry.MinCount,
            MaxCount = entry.MaxCount,
            Connection = entry.Connection,
            Sockets = sockets,
            Name = entry.Name,
            Weight = entry.Weight,
            Expeditions = entry.Expeditions,
            Difficulty = entry.Difficulty,
            Rotate = entry.Rotate,
        };
    }
}
