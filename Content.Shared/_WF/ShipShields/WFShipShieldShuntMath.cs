using System.Numerics;

namespace Content.Shared._WF.ShipShields;

/// <summary>Shares angular shield allocation and perimeter clipping between simulation and rendering.</summary>
public static class WFShipShieldShuntMath
{
    /// <summary>The narrowest supported allocation sector.</summary>
    public const float MinimumArc = MathF.PI / 6f;
    /// <summary>A full circle of shield coverage.</summary>
    public const float FullArc = MathF.PI * 2f;

    /// <summary>Normalizes an angle to the range minus pi to pi.</summary>
    public static float NormalizeAngle(float angle)
    {
        return MathF.IEEERemainder(angle, FullArc);
    }

    /// <summary>Returns local strength while conserving the angular capacity allocation.</summary>
    public static float StrengthMultiplier(Vector2 position, Vector2 center, float directionRadians, float concentration, float arcRadians)
    {
        if (arcRadians >= FullArc - 0.00001f || concentration <= 0f)
            return 1f;
        var offset = position - center;
        var angle = MathF.Atan2(offset.Y, offset.X);
        var inside = MathF.Abs(NormalizeAngle(angle - directionRadians)) <= arcRadians * 0.5f + 0.00001f;
        return inside ? 1f + concentration * (FullArc / arcRadians - 1f) : 1f - concentration;
    }

    /// <summary>Splits an edge at sector rays and returns only its protected intervals.</summary>
    public static List<(Vector2 Start, Vector2 End)> ProtectedSegments(Vector2 start, Vector2 end, Vector2 center,
        float directionRadians, float concentration, float arcRadians)
    {
        var result = new List<(Vector2 Start, Vector2 End)>();
        if (concentration < 1f || arcRadians >= FullArc - 0.00001f)
        {
            result.Add((start, end));
            return result;
        }
        var cuts = new List<float> { 0f, 1f };
        var edge = end - start;
        for (var side = -1; side <= 1; side += 2)
        {
            var angle = directionRadians + side * arcRadians * 0.5f;
            var ray = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var denominator = Cross(edge, ray);
            if (MathF.Abs(denominator) < 0.000001f)
                continue;
            var offset = center - start;
            var t = Cross(offset, ray) / denominator;
            var alongRay = Cross(offset, edge) / denominator;
            if (t > 0f && t < 1f && alongRay >= 0f)
                cuts.Add(t);
        }
        cuts.Sort();
        for (var i = 0; i < cuts.Count - 1; i++)
        {
            var a = start + edge * cuts[i];
            var b = start + edge * cuts[i + 1];
            if (Vector2.DistanceSquared(a, b) < 0.0001f ||
                StrengthMultiplier((a + b) * 0.5f, center, directionRadians, concentration, arcRadians) <= 0f)
                continue;
            result.Add((a, b));
        }
        return result;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.X * b.Y - a.Y * b.X;
    }
}
