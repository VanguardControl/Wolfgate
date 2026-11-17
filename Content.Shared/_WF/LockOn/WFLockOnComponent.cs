using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.LockOn;

/// <summary>
/// One firing solution per ship, shared by every helm and gunnery console aboard. Any console can set or clear it;
/// missiles fired by any of them seek the locked target.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFLockOnComponent : Component
{
    /// <summary>The locked entity, or its grid when the click landed on a ship, asteroid or debris grid.</summary>
    [AutoNetworkedField, ViewVariables]
    public EntityUid? Target;

    /// <summary>
    /// The clicked point on the target's hull in map space as of <see cref="AimTime"/>. The server owns it so every
    /// console solves the same lead from the same point, including the target's own motion.
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public Vector2 AimPoint;

    /// <summary>Map velocity of <see cref="AimPoint"/>, including the target grid's spin about its centre.</summary>
    [AutoNetworkedField, ViewVariables]
    public Vector2 AimVelocity;

    /// <summary>Round time in seconds when <see cref="AimPoint"/> was measured, so clients can extrapolate.</summary>
    [AutoNetworkedField, ViewVariables]
    public float AimTime;

    [AutoNetworkedField]

    public Vector2 MissileBearing;          // map-space unit vector

    [AutoNetworkedField]
    public Vector2 MissileMuzzle;           // world position of best launcher

    [AutoNetworkedField]
    public float MissileDivertCost;         // m/s

    [AutoNetworkedField]
    public float MissileDivertBudget;       // m/s

    [AutoNetworkedField]
    public float MissileQuality;            // 0..1

    [AutoNetworkedField]
    public bool MissileInEnvelope;

    [AutoNetworkedField]
    public bool MissileHasSolution;

    [AutoNetworkedField]
    public float MissileRange;              // intercept distance, m

    /// <summary>Furthest distance from the ship's centre at which a lock is held, in metres.</summary>
    [DataField]
    public float MaxRange = 2560f;

    [ViewVariables, DataField]
    public float MissileSpeed; // m/s, set from the missile's prototype / gun component on the server

    /// <summary>The click in the target's own frame, so the aim point rides a moving, turning hull.</summary>
    [ViewVariables]
    public Vector2 TargetLocalPoint;

    /// <summary>Earliest refresh of <see cref="AimPoint"/> and <see cref="AimVelocity"/>.</summary>
    [ViewVariables]
    public TimeSpan NextAim;

    /// <summary>The console that acquired the current lock.</summary>
    [ViewVariables]
    public EntityUid? LockedBy;

    /// <summary>The player who acquired the current lock.</summary>
    [ViewVariables]
    public EntityUid? Operator;

    /// <summary>When the current target was acquired.</summary>
    [ViewVariables]
    public TimeSpan LockedAt;

    /// <summary>
    /// Earliest periodic validity check. A lock is held between checks without replicating anything.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextCheck;
}
