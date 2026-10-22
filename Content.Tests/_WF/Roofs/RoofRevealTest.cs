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
}
