using System.Collections.Generic;
using Content.Client._WF.Roofs;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.Roofs;

/// <summary>Standing under a roof lifts the room you're in and its walls, not the room next door or open ground.</summary>
[TestFixture]
[TestOf(typeof(WFRoofReveal))]
public sealed class RoofRevealTest
{
    // Two rooms side by side under one roof, a wall between them with a door in it; open ground round them.
    //   x: 0 wall, 1-3 room A, 4 wall/door, 5-7 room B, 8 wall; y: 0 and 4 walls, 1-3 inside.
    private static bool Roofed(Vector2i tile) => tile.X is >= 0 and <= 8 && tile.Y is >= 0 and <= 4;

    private static bool Wall(Vector2i tile, bool doorShut) =>
        tile.X is 0 or 8 || tile.Y is 0 or 4 || tile.X == 4 && (tile.Y != 2 || doorShut);

    [Test]
    public void TheRoomLiftsUpToItsWalls()
    {
        var revealed = new HashSet<Vector2i>();
        WFRoofReveal.Fill(new Vector2i(2, 2), Roofed, tile => Wall(tile, true), 4096, revealed);

        Assert.That(revealed, Does.Contain(new Vector2i(1, 1)), "The room itself should lift.");
        Assert.That(revealed, Does.Contain(new Vector2i(0, 2)), "...and the walls round it, to be seen from inside.");
        Assert.That(revealed, Does.Contain(new Vector2i(4, 2)), "The shut door is part of the wall.");
        Assert.That(revealed, Does.Not.Contain(new Vector2i(6, 2)), "The room past the shut door stays covered.");
        Assert.That(revealed, Does.Not.Contain(new Vector2i(-1, 2)), "Open ground has no roof to lift.");
    }

    [Test]
    public void AnOpenDoorLetsYouSeeThrough()
    {
        var revealed = new HashSet<Vector2i>();
        WFRoofReveal.Fill(new Vector2i(2, 2), Roofed, tile => Wall(tile, false), 4096, revealed);
        Assert.That(revealed, Does.Contain(new Vector2i(6, 2)), "Through an open door the next room lifts too.");
    }

    [Test]
    public void OutsideNothingLifts()
    {
        var revealed = new HashSet<Vector2i>();
        WFRoofReveal.Fill(new Vector2i(-3, 2), Roofed, tile => Wall(tile, true), 4096, revealed);
        Assert.That(revealed, Is.Empty);
    }

    // A ship deck 0-10 by 0-6: walls round a room at 2-8 by 1-5 with a door at (5, 1), open plating outside the walls.
    private static HashSet<Vector2i> Deck()
    {
        var floor = new HashSet<Vector2i>();
        for (var x = 0; x <= 10; x++)
        for (var y = 0; y <= 6; y++)
            floor.Add(new Vector2i(x, y));
        return floor;
    }

    private static bool RoomWall(Vector2i tile) =>
        tile.X is >= 2 and <= 8 && tile.Y is >= 1 and <= 5 && (tile.X is 2 or 8 || tile.Y is 1 or 5);

    [Test]
    public void OnlyTheEnclosedRoomIsRoofed()
    {
        var roofed = new HashSet<Vector2i>();
        WFRoofReveal.Enclosed(Deck(), _ => false, RoomWall, roofed);

        Assert.That(roofed, Does.Contain(new Vector2i(5, 3)), "The room inside the walls is roofed.");
        Assert.That(roofed, Does.Not.Contain(new Vector2i(2, 3)), "Its walls stay uncovered, to be seen from outside.");
        Assert.That(roofed, Does.Not.Contain(new Vector2i(5, 1)), "...and so does the door in them.");
        Assert.That(roofed, Does.Not.Contain(new Vector2i(0, 0)), "Open plating outside is left bare.");
        Assert.That(roofed, Does.Not.Contain(new Vector2i(9, 3)), "...even right against the wall.");
    }

    [Test]
    public void ABreachOpensTheRoomToTheSky()
    {
        var roofed = new HashSet<Vector2i>();
        WFRoofReveal.Enclosed(Deck(), _ => false, tile => RoomWall(tile) && tile != new Vector2i(8, 3), roofed);
        Assert.That(roofed, Does.Not.Contain(new Vector2i(5, 3)), "A hole in the wall leaves the room open.");
    }

    [Test]
    public void OnlyOpenFloorIsCovered()
    {
        Assert.That(WFRoofReveal.Covers(roofed: true, structure: false, lifted: false), Is.True, "Roofed floor is covered.");
        Assert.That(WFRoofReveal.Covers(roofed: true, structure: true, lifted: false), Is.False,
            "A wall, window or door under the roof is never covered.");
        Assert.That(WFRoofReveal.Covers(roofed: true, structure: false, lifted: true), Is.False, "A lifted room is open.");
        Assert.That(WFRoofReveal.Covers(roofed: false, structure: false, lifted: false), Is.False, "Open ground has no roof.");
    }

    [Test]
    public void LatticeCountsAsOutside()
    {
        var floor = new HashSet<Vector2i> { new(0, 0), new(1, 0), new(2, 0) };
        var roofed = new HashSet<Vector2i>();
        // Fully surrounded by more deck in a ring, but the tile is lattice.
        WFRoofReveal.Enclosed(floor, tile => tile == new Vector2i(1, 0), _ => false, roofed);
        Assert.That(roofed, Is.Empty);
    }
}
