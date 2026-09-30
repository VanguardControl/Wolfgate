using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.ShipShields;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.ShipShields;

/// <summary>Checks hull contour clearance, concavity, and collision-safe edges.</summary>
[TestFixture]
[TestOf(typeof(WFShipShieldGeometry))]
public sealed class WFShipShieldGeometryTest
{
    [Test]
    public void EmptyHullHasNoContours()
    {
        Assert.That(WFShipShieldGeometry.CreateContours(Array.Empty<Vector2i>()), Is.Empty);
    }

    [Test]
    public void TileSizeScalesHullAndClearance()
    {
        var contour = WFShipShieldGeometry.CreateContours(new[] { new Vector2i(-2, 5) }, 2f)[0];
        Assert.Multiple(() =>
        {
            Assert.That(contour.Min(v => v.X), Is.EqualTo(-10f));
            Assert.That(contour.Max(v => v.X), Is.EqualTo(4f));
            Assert.That(contour.Min(v => v.Y), Is.EqualTo(4f));
            Assert.That(contour.Max(v => v.Y), Is.EqualTo(18f));
        });
        AssertValidContour(contour);
    }

    [Test]
    public void ConcaveHullKeepsItsLargeNotch()
    {
        var tiles = new List<Vector2i>();
        for (var x = 0; x < 20; x++)
        for (var y = 0; y < 20; y++)
        {
            if (x < 4 || y < 4)
                tiles.Add(new Vector2i(x, y));
        }

        var contours = WFShipShieldGeometry.CreateContours(tiles);
        Assert.That(contours.Length, Is.EqualTo(1));
        var contour = contours[0];
        Assert.Multiple(() =>
        {
            Assert.That(Contains(contour, new Vector2(2f, 18f)), Is.True);
            Assert.That(Contains(contour, new Vector2(18f, 2f)), Is.True);
            Assert.That(Contains(contour, new Vector2(14f, 14f)), Is.False);
        });
        AssertValidContour(contour);
    }

    [Test]
    public void InputOrderAndDuplicatesDoNotChangeContours()
    {
        var tiles = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(1, 1), new Vector2i(30, -5) };
        var expected = WFShipShieldGeometry.CreateContours(tiles);
        var actual = WFShipShieldGeometry.CreateContours(tiles.Reverse().Concat(tiles));
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual[i], Is.EqualTo(expected[i]));
            AssertValidContour(actual[i]);
        }
    }

    [Test]
    public void DisconnectedHullSectionsKeepSeparateContours()
    {
        var contours = WFShipShieldGeometry.CreateContours(new[] { new Vector2i(0, 0), new Vector2i(30, 0) });
        Assert.That(contours.Length, Is.EqualTo(2));
        Assert.That(contours.Any(c => Contains(c, new Vector2(0.5f, 0.5f))), Is.True);
        Assert.That(contours.Any(c => Contains(c, new Vector2(30.5f, 0.5f))), Is.True);
        Assert.That(contours.Any(c => Contains(c, new Vector2(15f, 0.5f))), Is.False);
        foreach (var contour in contours)
            AssertValidContour(contour);
    }

    [Test]
    public void EnclosedHolesDoNotBecomeShieldPerimeters()
    {
        var tiles = new List<Vector2i>();
        for (var x = 0; x < 20; x++)
        for (var y = 0; y < 20; y++)
        {
            if (x == 0 || y == 0 || x == 19 || y == 19)
                tiles.Add(new Vector2i(x, y));
        }

        var contours = WFShipShieldGeometry.CreateContours(tiles);
        Assert.That(contours.Length, Is.EqualTo(1));
        Assert.That(Contains(contours[0], new Vector2(10f, 10f)), Is.True);
        AssertValidContour(contours[0]);
    }

    [Test]
    public void ImpactProjectionIncludesClosingEdgeAndEndpoints()
    {
        var contours = new[] { new[] { new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 4f), new Vector2(0f, 4f) } };
        Assert.Multiple(() =>
        {
            Assert.That(WFShipShieldGeometry.ClosestPoint(contours, new Vector2(-2f, 2f)), Is.EqualTo(new Vector2(0f, 2f)));
            Assert.That(WFShipShieldGeometry.ClosestPoint(contours, new Vector2(5f, 5f)), Is.EqualTo(new Vector2(4f, 4f)));
            Assert.That(WFShipShieldGeometry.ClosestPoint(Array.Empty<Vector2[]>(), new Vector2(5f, 5f)), Is.EqualTo(new Vector2(5f, 5f)));
        });
    }
    private static void AssertValidContour(Vector2[] contour)
    {
        Assert.That(contour.Length, Is.GreaterThanOrEqualTo(3));
        Assert.That(contour[0], Is.Not.EqualTo(contour[^1]), "Closing vertex must not be duplicated.");
        var area = 0f;
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Length];
            Assert.That(Vector2.DistanceSquared(a, b), Is.GreaterThan(0.0001f), "Every edge must be usable for collision.");
            area += a.X * b.Y - b.X * a.Y;
        }
        Assert.That(area, Is.GreaterThan(0f), "Outer contours must face incoming projectiles.");
    }

    private static bool Contains(Vector2[] contour, Vector2 point)
    {
        var inside = false;
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Length];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
