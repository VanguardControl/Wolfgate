using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.TileMovement;

/// <summary>
/// Makes a mob walk from tile centre to tile centre on grids instead of moving freely.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFTileMovementComponent : Component
{
    /// <summary>
    /// Whether a step is in progress.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Moving;

    /// <summary>
    /// Whether the current step is a retreat to <see cref="Origin"/> after the way was blocked.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Returning;

    /// <summary>
    /// Grid the step is on.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Grid;

    /// <summary>
    /// Grid-local tile centre the step started from.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 Origin;

    /// <summary>
    /// Grid-local tile centre the step ends on.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 Target;

    /// <summary>
    /// Grid-local direction of the step, one of the eight neighbours.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2i Direction;

    /// <summary>
    /// Speed of the step, kept so a step finishes after the key is let go.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Speed;

    /// <summary>
    /// When the step counts as blocked and the mob walks back.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan Deadline;

    /// <summary>
    /// Speed used to settle onto a tile centre when idle off-centre.
    /// </summary>
    [DataField]
    public float SettleSpeed = 2.5f;
}
