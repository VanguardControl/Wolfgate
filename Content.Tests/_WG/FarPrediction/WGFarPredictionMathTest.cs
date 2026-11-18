using System.Numerics;
using Content.Shared._WG.FarPrediction;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WG.FarPrediction;

/// <summary>The far-grid step maths: when it applies, what it expects and what it accepts as rounding.</summary>
[TestFixture]
public sealed class WGFarPredictionMathTest
{
    /// <summary>Only positions past the threshold on either axis are corrected.</summary>
    [TestCase(0f, 0f, false)]
    [TestCase(4095f, -4095f, false)]
    [TestCase(4096f, 0f, true)]
    [TestCase(10f, -76000f, true)]
    public void FarIsPastTheThresholdOnEitherAxis(float x, float y, bool expected)
    {
        Assert.That(WGFarPredictionMath.IsFar(new Vector2(x, y)), Is.EqualTo(expected));
    }

    /// <summary>The spacing is the float gap at the sum of both axes: 8 mm in the Deep, 16 mm past 131 km.</summary>
    [TestCase(76000f, 0f, 0.0078125f)]
    [TestCase(30000f, 0f, 0.001953125f)]
    [TestCase(76000f, -76000f, 0.015625f)]
    public void SpacingIsTheFloatGapAtThatDistance(float x, float y, float expected)
    {
        Assert.That(WGFarPredictionMath.Spacing(new Vector2(x, y)), Is.EqualTo(expected));
    }

    /// <summary>A velocity along a turned grid's own axis is a step along that axis in the grid's coordinates.</summary>
    [Test]
    public void StepIsTheVelocityTurnedIntoTheParentsFrame()
    {
        var rotation = Angle.FromDegrees(30);
        var velocity = rotation.RotateVec(new Vector2(3f, 0f));

        var step = WGFarPredictionMath.Step(velocity, 1f / 60f, rotation, Vector2.Zero, Angle.Zero, Angle.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(step.X, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(step.Y, Is.EqualTo(0f).Within(1e-6f));
        });
    }

    /// <summary>A body that turns about an off-centre mass swings its origin the other way.</summary>
    [Test]
    public void StepIncludesTheSwingAboutTheCentreOfMass()
    {
        var step = WGFarPredictionMath.Step(
            Vector2.Zero,
            1f / 60f,
            Angle.Zero,
            new Vector2(1f, 0f),
            Angle.Zero,
            Angle.FromDegrees(90));

        Assert.Multiple(() =>
        {
            Assert.That(step.X, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(step.Y, Is.EqualTo(-1f).Within(1e-6f));
        });
    }

    /// <summary>An answer within a few float gaps of the expected one is rounding and is replaced.</summary>
    [Test]
    public void RoundingIsCorrected()
    {
        var world = new Vector2(76000f, 0f);
        var before = new Vector2(5f, 5f);
        var step = new Vector2(0.04f, 0f);
        var solved = before + step + new Vector2(0.012f, -0.009f);

        Assert.That(WGFarPredictionMath.TryCorrect(before, step, solved, world, out var corrected), Is.True);
        Assert.That(corrected, Is.EqualTo(before + step));
    }

    /// <summary>An answer further off than rounding can explain is something real and is kept.</summary>
    [Test]
    public void ARealMoveIsKept()
    {
        var world = new Vector2(76000f, 0f);
        var before = new Vector2(5f, 5f);
        var step = new Vector2(0.04f, 0f);
        var solved = before + step + new Vector2(0.05f, 0f);

        Assert.That(WGFarPredictionMath.TryCorrect(before, step, solved, world, out _), Is.False);
    }

    /// <summary>The same offset that is rounding in the Deep is a real move nearer in, where floats are finer.</summary>
    [Test]
    public void ToleranceShrinksWithDistance()
    {
        var before = new Vector2(5f, 5f);
        var step = new Vector2(0.04f, 0f);
        var solved = before + step + new Vector2(0.012f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(WGFarPredictionMath.TryCorrect(before, step, solved, new Vector2(76000f, 0f), out _), Is.True);
            Assert.That(WGFarPredictionMath.TryCorrect(before, step, solved, new Vector2(10000f, 0f), out _), Is.False);
        });
    }

    /// <summary>Nothing is reported when the engine already landed on the expected position.</summary>
    [Test]
    public void AnExactAnswerNeedsNoCorrection()
    {
        var before = new Vector2(5f, 5f);
        var step = new Vector2(0.04f, 0f);

        Assert.That(WGFarPredictionMath.TryCorrect(before, step, before + step, new Vector2(76000f, 0f), out _), Is.False);
    }
}
