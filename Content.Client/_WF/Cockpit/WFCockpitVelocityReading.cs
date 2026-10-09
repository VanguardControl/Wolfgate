using System.Numerics;

namespace Content.Client._WF.Cockpit;

/// <summary>Expresses actual ship motion with its bow up and starboard to the right.</summary>
public readonly record struct WFCockpitVelocityReading(Vector2 BowVelocity)
{
    /// <summary>Speed below the readout precision has no stable direction pointer.</summary>
    public const float DirectionThreshold = 0.05f;

    /// <summary>Retains the velocity magnitude independently of ship heading.</summary>
    public float Speed => BowVelocity.Length();

    /// <summary>Maps positive forward motion upward in screen coordinates.</summary>
    public Vector2? ScreenDirection => Speed < DirectionThreshold ? null :
        new Vector2(BowVelocity.X, -BowVelocity.Y) / Speed;

    /// <summary>Measures drift clockwise from the bow, with starboard at ninety degrees.</summary>
    public double? BearingDegrees => ScreenDirection == null ? null :
        (Math.Atan2(BowVelocity.X, BowVelocity.Y) * 180 / Math.PI + 360) % 360;

    /// <summary>Uses the same inverse ship rotation as the helm's longitudinal and lateral readouts.</summary>
    public static WFCockpitVelocityReading? FromWorld(Vector2? worldVelocity, Angle? shipRotation)
    {
        if (worldVelocity is not { } velocity || shipRotation is not { } rotation ||
            !float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !double.IsFinite(rotation.Theta))
            return null;
        var relative = (-rotation).RotateVec(velocity);
        if (!float.IsFinite(relative.LengthSquared()))
            return null;
        return new WFCockpitVelocityReading(relative);
    }
}
