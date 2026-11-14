namespace Content.Server._WF.LockOn;

/// <summary>
/// Carries the ship's lock from a console's fire command to the projectiles that command spawns. It
/// lives on the weapon for a couple of seconds so a burst of missiles all receive the same target.
/// </summary>
[RegisterComponent]
public sealed partial class WFLockOnShotComponent : Component
{
    /// <summary>The ship's locked target when the console fired.</summary>
    public EntityUid? Target;

    /// <summary>When the marker stops applying to further shots from the same weapon.</summary>
    public TimeSpan Expires;
}
