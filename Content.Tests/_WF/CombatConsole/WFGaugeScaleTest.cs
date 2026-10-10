using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._WF.CombatConsole;
using NUnit.Framework;

namespace Content.Tests._WF.CombatConsole;

/// <summary>Checks signed scales, overrange readings and unavailable telemetry.</summary>
[TestFixture]
public sealed class WFGaugeScaleTest
{
    [Test]
    public void SignedMotionCrossesTheCentreAndClampsAtTheStops()
    {
        Assert.That(WFGaugeScale.Fraction(-100, -100, 100), Is.EqualTo(0));
        Assert.That(WFGaugeScale.Fraction(0, -100, 100), Is.EqualTo(0.5f));
        Assert.That(WFGaugeScale.Fraction(50, -100, 100), Is.EqualTo(0.75f));
        Assert.That(WFGaugeScale.Fraction(200, -100, 100), Is.EqualTo(1));
        Assert.That(WFGaugeScale.Fraction(-200, -100, 100), Is.EqualTo(0));
    }

    [Test]
    public void MissingAndInvalidDataNeverLooksLikeARealZero()
    {
        foreach (var value in new double?[] { null, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.That(WFGaugeScale.Fraction(value, 0, 100), Is.Null);
        Assert.That(WFGaugeScale.Fraction(0, 0, 100), Is.Zero);
        Assert.That(WFGaugeScale.Fraction(3, 5, 5), Is.Null);
        Assert.That(WFGaugeScale.Fraction(3, 5, -5), Is.Null);
        Assert.That(WFGaugeScale.Fraction(3, 0, double.NaN), Is.Null);
        Assert.That(WFGaugeScale.Fraction(0, -double.MaxValue, double.MaxValue), Is.Null);
    }

    [Test]
    public void FittedFontScalesStayBoundedAndNeverGrowPastTheirAllowance()
    {
        var scales = new HashSet<float>();
        for (var index = 1; index <= 20000; index++)
        {
            var allowance = index / 1000f;
            var scale = WFGaugeScale.FontScale(allowance);
            Assert.That(scale, Is.InRange(0f, MathF.Min(4, allowance)));
            scales.Add(scale);
        }
        Assert.That(scales.Count, Is.LessThanOrEqualTo(33), "Resizing must not grow an unbounded set of font atlases.");
        foreach (var invalid in new[] { -1f, 0f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            Assert.That(WFGaugeScale.FontScale(invalid), Is.Zero);
    }

    [TestCase(28)]
    [TestCase(40)]
    [TestCase(60)]
    [TestCase(100)]
    [TestCase(160)]
    public void EndpointLabelsRemainInsideTheLensAndSeparate(float radius)
    {
        var minimum = WFGaugeScale.RoundLabelBounds(radius, false);
        var maximum = WFGaugeScale.RoundLabelBounds(radius, true);
        Assert.That(minimum.Right, Is.LessThan(maximum.Left));
        Assert.That(minimum.Top, Is.EqualTo(maximum.Top));
        foreach (var bounds in new[] { minimum, maximum })
        {
            Assert.That(bounds.Width, Is.GreaterThan(0));
            Assert.That(bounds.Height, Is.GreaterThan(0));
            foreach (var corner in new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight })
                Assert.That(Vector2.Distance(Vector2.Zero, corner), Is.LessThan(radius * 0.98f),
                    "The whole endpoint number must leave a clear gap before the curved rim.");
        }
    }

    [TestCase(0, 30, 30)]
    [TestCase(31, 30, 50)]
    [TestCase(101, 30, 200)]
    [TestCase(250, 30, 500)]
    [TestCase(500, 30, 500)]
    [TestCase(501, 30, 1000)]
    public void CountScalesRemainLabelledAndContainTheWholeQuantity(double value, double floor, double expected) =>
        Assert.That(WFGaugeScale.Ceiling(value, floor), Is.EqualTo(expected));
}
