using System.Numerics;

namespace Content.Shared._WG.FarPrediction;

/// <summary>
/// The step a body on a far grid should take, worked out in the grid's own coordinates instead of the world's.
/// </summary>
public static class WGFarPredictionMath
{
    /// <summary>
    /// How far from the map origin, on either axis, a body has to be before its predicted steps are corrected.
    /// Floats are under half a millimetre apart inside this, which the stock solver's error can't be seen through.
    /// </summary>
    public const float FarDistance = 4096f;

    /// <summary>
    /// How many float spacings the engine's answer may sit from the expected one and still be taken for rounding.
    /// The stock solver stayed within 2.4 over 6,400 sampled steps; anything further is a real move and is kept.
    /// </summary>
    public const float ToleranceSpacings = 4f;

    /// <summary>Whether a world position is far enough out for its steps to need correcting.</summary>
    public static bool IsFar(Vector2 world)
    {
        return MathF.Abs(world.X) >= FarDistance || MathF.Abs(world.Y) >= FarDistance;
    }

    /// <summary>
    /// The gap between neighbouring floats at a world position, in metres. Taken at the sum of both axes, since a
    /// rotated grid mixes them.
    /// </summary>
    public static float Spacing(Vector2 world)
    {
        var magnitude = MathF.Abs(world.X) + MathF.Abs(world.Y);
        return MathF.BitIncrement(magnitude) - magnitude;
    }

    /// <summary>
    /// How far a body's local position moves in one physics step: its velocity over the step, turned into the
    /// parent's frame, plus the swing of its origin about its centre of mass if it turned.
    /// </summary>
    public static Vector2 Step(
        Vector2 velocity,
        float frameTime,
        Angle parentWorldRotation,
        Vector2 localCenter,
        Angle localRotationBefore,
        Angle localRotationAfter)
    {
        var step = (-parentWorldRotation).RotateVec(velocity * frameTime);

        if (localCenter != Vector2.Zero && localRotationBefore != localRotationAfter)
            step += localRotationBefore.RotateVec(localCenter) - localRotationAfter.RotateVec(localCenter);

        return step;
    }

    /// <summary>
    /// Replaces the solver's answer with the expected one when the two differ by no more than float rounding at
    /// that distance. False when they agree exactly or differ by more.
    /// </summary>
    public static bool TryCorrect(Vector2 before, Vector2 step, Vector2 solved, Vector2 world, out Vector2 corrected)
    {
        corrected = before + step;

        if (corrected == solved)
            return false;

        var tolerance = ToleranceSpacings * Spacing(world);
        return Vector2.DistanceSquared(corrected, solved) <= tolerance * tolerance;
    }
}
