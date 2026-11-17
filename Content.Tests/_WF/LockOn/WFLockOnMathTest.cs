using System;
using System.Numerics;
using Content.Shared._WF.LockOn;
using NUnit.Framework;

namespace Content.Tests._WF.LockOn;

/// <summary>The lock indicator's intercept solver: closed-form cases a gunner can check by eye.</summary>
[TestFixture]
[TestOf(typeof(WFLockOnMath))]
public sealed class WFLockOnMathTest
{
    /// <summary>A still target is met where it sits, after the plain flight time.</summary>
    [Test]
    public void MeetsAStillTarget()
    {
        var lead = WFLockOnMath.Solve(Vector2.Zero, new Vector2(100f, 0f), Vector2.Zero, 100f);

        Assert.Multiple(() =>
        {
            Assert.That(lead.Valid, Is.True);
            Assert.That(lead.Time, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(lead.Point.X, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(lead.Point.Y, Is.EqualTo(0f).Within(1e-3f));
        });
    }

    /// <summary>A crossing target is led in the direction it is moving.</summary>
    [Test]
    public void LeadsACrossingTarget()
    {
        var lead = WFLockOnMath.Solve(Vector2.Zero, new Vector2(100f, 0f), new Vector2(0f, 10f), 200f);

        Assert.Multiple(() =>
        {
            Assert.That(lead.Valid, Is.True);
            Assert.That(lead.Time, Is.EqualTo(0.5006f).Within(1e-3f));
            Assert.That(lead.Point.X, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(lead.Point.Y, Is.EqualTo(5.006f).Within(1e-2f));
        });
    }

    /// <summary>A target outrunning the gun has no intercept, so the indicator must not pretend it does.</summary>
    [Test]
    public void RejectsATargetThatOutrunsTheGun()
    {
        var lead = WFLockOnMath.Solve(Vector2.Zero, new Vector2(100f, 0f), new Vector2(100f, 0f), 50f);
        Assert.That(lead.Valid, Is.False);
    }

    /// <summary>Hitscan guns have no flight time, so the target's own position is the lead.</summary>
    [Test]
    public void TreatsHitscanAsInstant()
    {
        var lead = WFLockOnMath.Solve(Vector2.Zero, new Vector2(100f, 0f), new Vector2(0f, 10f), 0f);

        Assert.Multiple(() =>
        {
            Assert.That(lead.Valid, Is.True);
            Assert.That(lead.Time, Is.EqualTo(0f));
            Assert.That(lead.Point, Is.EqualTo(new Vector2(100f, 0f)));
        });
    }

    /// <summary>
    /// A shot carries the firing ship's turn, so a gun slewing to starboard must be aimed short of the target's
    /// future position by the kick it will pick up on the way out.
    /// </summary>
    [Test]
    public void LeadsBehindTheShipsOwnTurn()
    {
        var muzzle = Vector2.Zero;
        var target = new Vector2(1000f, 0f);
        var targetVelocity = Vector2.Zero;
        var muzzleVelocity = new Vector2(0f, 30f);
        var lead = WFLockOnMath.Solve(muzzle, target, targetVelocity - muzzleVelocity, 320f);

        Assert.Multiple(() =>
        {
            Assert.That(lead.Valid, Is.True);
            Assert.That(lead.Time, Is.EqualTo(3.1389f).Within(1e-3f));
            Assert.That(lead.Point.Y, Is.EqualTo(-94.17f).Within(0.1f),
                "The cross must sit behind the target by the turn's kick over the flight.");
            Assert.That(ClosestApproach(muzzle, muzzleVelocity, target, targetVelocity, 320f, lead),
                Is.LessThan(0.01f));
        });
    }

    /// <summary>The solved point is the barrel's aim point for a moving target while the shooter turns.</summary>
    [Test]
    public void MeetsTheTargetThroughTheFlight()
    {
        var muzzle = Vector2.Zero;
        var target = new Vector2(1000f, 0f);
        var targetVelocity = new Vector2(0f, 100f);
        var muzzleVelocity = new Vector2(0f, 30f);
        var lead = WFLockOnMath.Solve(muzzle, target, targetVelocity - muzzleVelocity, 320f);

        Assert.That(ClosestApproach(muzzle, muzzleVelocity, target, targetVelocity, 320f, lead),
            Is.LessThan(0.01f));
    }

    /// <summary>A head-on target closing at exactly the gun's speed still has an intercept.</summary>
    [Test]
    public void CatchesAHeadOnTargetAtEqualSpeed()
    {
        var lead = WFLockOnMath.Solve(Vector2.Zero, new Vector2(1000f, 0f), new Vector2(-100f, 0f), 100f);

        Assert.Multiple(() =>
        {
            Assert.That(lead.Valid, Is.True);
            Assert.That(lead.Time, Is.EqualTo(5f).Within(1e-3f));
            Assert.That(lead.Point.X, Is.EqualTo(500f).Within(1e-2f));
        });
    }

    /// <summary>How close the bullet, flying with the muzzle's kick, gets to the target over the solved flight.</summary>
    private static float ClosestApproach(Vector2 muzzle, Vector2 muzzleVelocity, Vector2 target,
        Vector2 targetVelocity, float speed, WFLockOnMath.WFLockOnLead lead)
    {
        var direction = Vector2.Normalize(lead.Point - muzzle);
        var bullet = muzzleVelocity + direction * speed;
        var offset = target - muzzle;
        var relative = targetVelocity - bullet;
        var time = relative.LengthSquared() > 0f
            ? -Vector2.Dot(offset, relative) / relative.LengthSquared()
            : 0f;
        time = Math.Clamp(time, 0f, lead.Time);
        return (offset + relative * time).Length();
    }
}
