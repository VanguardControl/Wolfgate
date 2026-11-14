using System.Numerics;

namespace Content.Shared._WF.LockOn;

/// <summary>Where a gun has to aim to meet a moving target, as the lock indicator draws it.</summary>
public static class WFLockOnMath
{
    /// <summary>Longest flight time considered, in seconds; beyond it the target is outrunning the gun.</summary>
    public const float MaxFlightTime = 60f;

    /// <summary>Relative speed within this fraction of the projectile's counts as equal, and the solve is linear.</summary>
    private const float EqualSpeedFraction = 1e-3f;

    /// <summary>
    /// Solves the lead point for a projectile of <paramref name="speed"/> fired from <paramref name="muzzle"/>.
    /// <paramref name="relativeVelocity"/> is the target's velocity less the muzzle's own, because a shot carries
    /// the firing ship's turn with it. A non-positive or non-finite speed is hitscan: the target's current position
    /// is the lead.
    /// </summary>
    public static WFLockOnLead Solve(Vector2 muzzle, Vector2 target, Vector2 relativeVelocity, float speed)
    {
        if (!float.IsFinite(speed) || speed <= 0f)
            return new WFLockOnLead(target, 0f, true);

        var offset = target - muzzle;
        var closing = Vector2.Dot(relativeVelocity, relativeVelocity);
        var a = closing - speed * speed;
        var b = 2f * Vector2.Dot(offset, relativeVelocity);
        var c = Vector2.Dot(offset, offset);

        float time;
        if (MathF.Abs(a) <= EqualSpeedFraction * speed * speed)
        {
            // The target closes at exactly the projectile's speed, so the intercept is linear in time.
            if (MathF.Abs(b) <= float.Epsilon)
                return new WFLockOnLead(target, 0f, false);

            time = -c / b;
        }
        else
        {
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f || !float.IsFinite(discriminant))
                return new WFLockOnLead(target, 0f, false);

            var root = MathF.Sqrt(discriminant);
            var denominator = 2f * a;
            time = SmallestPositive((-b - root) / denominator, (-b + root) / denominator);
        }

        if (!float.IsFinite(time) || time <= 0f || time > MaxFlightTime)
            return new WFLockOnLead(target, 0f, false);

        return new WFLockOnLead(target + relativeVelocity * time, time, true);
    }

    /// <summary>The smaller positive root, or zero when neither root is positive.</summary>
    private static float SmallestPositive(float first, float second)
    {
        var small = MathF.Min(first, second);
        if (small > 0f)
            return small;

        var large = MathF.Max(first, second);
        return large > 0f ? large : 0f;
    }

    /// <summary>The lead point and flight time, or the target's own position when no lead can be solved.</summary>
    public readonly record struct WFLockOnLead(Vector2 Point, float Time, bool Valid);
}
