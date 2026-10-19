#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.MappingTools;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.MappingTools;

[TestFixture]
public sealed class MappingToolsShapesTest
{
    private static Vector2i V(int x, int y) => new(x, y);

    [Test]
    public void LineAlongAnAxis()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsShapes.Line(V(0, 0), V(3, 0)), Is.EqualTo(new[] { V(0, 0), V(1, 0), V(2, 0), V(3, 0) }));
            Assert.That(MappingToolsShapes.Line(V(2, 1), V(2, 4)), Is.EqualTo(new[] { V(2, 1), V(2, 2), V(2, 3), V(2, 4) }));
        });
    }

    [Test]
    public void LineOnTheDiagonal()
    {
        Assert.That(MappingToolsShapes.Line(V(0, 0), V(3, -3)), Is.EqualTo(new[] { V(0, 0), V(1, -1), V(2, -2), V(3, -3) }));
    }

    [Test]
    public void LineIsConnectedWithoutRepeats()
    {
        var cells = MappingToolsShapes.Line(V(-2, 1), V(7, 5));
        Assert.Multiple(() =>
        {
            Assert.That(cells, Has.Count.EqualTo(10), "One cell per step of the longer axis.");
            Assert.That(cells, Is.Unique);
            Assert.That(cells[0], Is.EqualTo(V(-2, 1)));
            Assert.That(cells[^1], Is.EqualTo(V(7, 5)));
            for (var i = 1; i < cells.Count; i++)
            {
                var step = cells[i] - cells[i - 1];
                Assert.That(Math.Abs(step.X) <= 1 && Math.Abs(step.Y) <= 1, Is.True, $"Gap between {cells[i - 1]} and {cells[i]}.");
            }
        });
    }

    [Test]
    public void LineOfOneCell()
    {
        Assert.That(MappingToolsShapes.Line(V(4, -2), V(4, -2)), Is.EqualTo(new[] { V(4, -2) }));
    }

    [Test]
    public void LineReversedHasTheSameCells()
    {
        var forward = MappingToolsShapes.Line(V(0, 0), V(5, 2));
        var backward = MappingToolsShapes.Line(V(5, 2), V(0, 0));
        Assert.Multiple(() =>
        {
            Assert.That(backward[0], Is.EqualTo(V(5, 2)));
            Assert.That(backward[^1], Is.EqualTo(V(0, 0)));
            Assert.That(backward, Has.Count.EqualTo(forward.Count));
        });
    }

    [Test]
    public void FilledRect()
    {
        var cells = MappingToolsShapes.Rect(V(2, 3), V(0, 1), false);
        var expected = new List<Vector2i>();
        for (var y = 1; y <= 3; y++)
        {
            for (var x = 0; x <= 2; x++)
            {
                expected.Add(V(x, y));
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(cells, Is.EquivalentTo(expected));
            Assert.That(MappingToolsShapes.RectCount(V(2, 3), V(0, 1), false), Is.EqualTo(9));
        });
    }

    [Test]
    public void HollowRect()
    {
        var cells = MappingToolsShapes.Rect(V(0, 0), V(3, 2), true);
        Assert.Multiple(() =>
        {
            Assert.That(cells, Has.Count.EqualTo(10));
            Assert.That(cells, Is.Unique);
            Assert.That(cells, Does.Not.Contain(V(1, 1)));
            Assert.That(cells, Does.Not.Contain(V(2, 1)));
            Assert.That(cells.All(c => c.X is 0 or 3 || c.Y is 0 or 2), Is.True);
            Assert.That(MappingToolsShapes.RectCount(V(0, 0), V(3, 2), true), Is.EqualTo(10));
        });
    }

    [TestCase(0, 0, 0, 0)]
    [TestCase(0, 0, 1, 5)]
    [TestCase(-3, -3, 3, 3)]
    [TestCase(5, 0, 0, 1)]
    public void RectCountMatchesCells(int ax, int ay, int bx, int by)
    {
        Assert.Multiple(() =>
        {
            foreach (var hollow in new[] { false, true })
            {
                var cells = MappingToolsShapes.Rect(V(ax, ay), V(bx, by), hollow);
                Assert.That(MappingToolsShapes.RectCount(V(ax, ay), V(bx, by), hollow), Is.EqualTo(cells.Count),
                    $"hollow {hollow}");
            }
        });
    }

    [Test]
    public void RectCountWithoutBuildingIt()
    {
        Assert.That(MappingToolsShapes.RectCount(V(0, 0), V(99999, 99999), false), Is.EqualTo(10_000_000_000L));
    }

    /// <summary>A 7x7 grid: a ring of walls (type 2) round floor (type 1), with space (0) outside.</summary>
    private static int? Room(Vector2i cell)
    {
        if (cell.X is < 0 or > 6 || cell.Y is < 0 or > 6)
            return 0;

        return cell.X is 0 or 6 || cell.Y is 0 or 6 ? 2 : 1;
    }

    [Test]
    public void FloodFillsAnEnclosedRoom()
    {
        var cells = MappingToolsShapes.Flood(V(3, 3), Room, new Box2i(-10, -10, 10, 10), 1000, out var truncated);
        Assert.Multiple(() =>
        {
            Assert.That(truncated, Is.False);
            Assert.That(cells, Has.Count.EqualTo(25));
            Assert.That(cells, Is.Unique);
            Assert.That(cells.All(c => Room(c) == 1), Is.True);
        });
    }

    [Test]
    public void FloodLeakStopsAtTheBounds()
    {
        // A gap in the wall lets the fill out into space; the bounds hold it.
        int? Leaky(Vector2i cell) => cell == V(6, 3) ? 1 : Room(cell) == 0 ? 1 : Room(cell);

        var bounds = new Box2i(-2, -2, 9, 9);
        var cells = MappingToolsShapes.Flood(V(3, 3), Leaky, bounds, 1000, out var truncated);
        Assert.Multiple(() =>
        {
            Assert.That(truncated, Is.False);
            Assert.That(cells, Has.Count.EqualTo(11 * 11 - 23), "Every non-wall cell in the bounds.");
            Assert.That(cells.All(c => c.X >= -2 && c.X < 9 && c.Y >= -2 && c.Y < 9), Is.True);
        });
    }

    [Test]
    public void FloodFillsEmptyCells()
    {
        var cells = MappingToolsShapes.Flood(V(-1, -1), Room, new Box2i(-1, -1, 8, 8), 1000, out var truncated);
        Assert.Multiple(() =>
        {
            Assert.That(truncated, Is.False);
            Assert.That(cells, Has.Count.EqualTo(9 * 9 - 49), "The space ring round the room, inside the bounds.");
            Assert.That(cells.All(c => Room(c) == 0), Is.True);
        });
    }

    [Test]
    public void FloodStopsAtTheCap()
    {
        var cells = MappingToolsShapes.Flood(V(3, 3), Room, new Box2i(-10, -10, 10, 10), 10, out var truncated);
        Assert.Multiple(() =>
        {
            Assert.That(truncated, Is.True);
            Assert.That(cells, Has.Count.EqualTo(10));
            Assert.That(cells[0], Is.EqualTo(V(3, 3)));
        });

        var exact = MappingToolsShapes.Flood(V(3, 3), Room, new Box2i(-10, -10, 10, 10), 25, out var exactTruncated);
        Assert.Multiple(() =>
        {
            Assert.That(exact, Has.Count.EqualTo(25));
            Assert.That(exactTruncated, Is.False, "A cap the fill just fits isn't truncation.");
        });
    }

    [Test]
    public void FloodStartingOutsideTheBoundsFillsNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsShapes.Flood(V(3, 3), Room, new Box2i(4, 4, 6, 6), 100, out var truncated), Is.Empty);
            Assert.That(truncated, Is.False);
            Assert.That(MappingToolsShapes.Flood(V(6, 6), Room, new Box2i(0, 0, 6, 6), 100, out _), Is.Empty,
                "Right and top are exclusive.");
            Assert.That(MappingToolsShapes.Flood(V(3, 3), _ => null, new Box2i(0, 0, 6, 6), 100, out _), Is.Empty,
                "A cell that can't be filled.");
            Assert.That(MappingToolsShapes.Flood(V(3, 3), Room, new Box2i(0, 0, 6, 6), 0, out _), Is.Empty,
                "No cap, no cells.");
        });
    }
}
