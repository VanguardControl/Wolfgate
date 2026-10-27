using Content.Shared.Actions;
using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenobiology;

/// <summary>
/// A console that sends out a remote eye to move, feed and dose slimes near it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenobiologyConsoleComponent : Component
{
    public const string SlimeContainerName = "slimes";

    [DataField(required: true)]
    public ProtoId<TagPrototype> MonkeyTag;

    [DataField(required: true)]
    public ProtoId<TagPrototype> MonkeyCubeTag;

    [DataField(required: true)]
    public EntProtoId MonkeyProtoId;

    /// <summary>
    /// The eye spawned at the console when someone uses it.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId EyeProto;

    /// <summary>
    /// Furthest the eye may go from the console, in tiles.
    /// </summary>
    [DataField]
    public float EyeRange = 10f;

    /// <summary>
    /// Furthest the user may stand from the console before the eye closes.
    /// </summary>
    [DataField]
    public float UserRange = 1.5f;

    /// <summary>
    /// Actions given to the user while the eye is open.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> Actions = new();

    /// <summary>
    /// Damage a monkey needs before it can be recycled.
    /// </summary>
    [DataField]
    public FixedPoint2 RecycleDamageThreshold = 100;

    [DataField, AutoNetworkedField]
    public FixedPoint2 MonkeyCubes = FixedPoint2.Zero;

    [DataField, AutoNetworkedField]
    public int MutationPotions;

    [DataField, AutoNetworkedField]
    public int StabilizerPotions;

    /// <summary>
    /// The user looking through the console and their eye.
    /// </summary>
    [ViewVariables]
    public EntityUid? User;

    [ViewVariables]
    public EntityUid? Eye;
}

/// <summary>
/// Someone looking through a xenobiology console.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenobiologyConsoleUserComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Console;

    [DataField, AutoNetworkedField]
    public EntityUid? Eye;

    [DataField]
    public List<EntityUid> GrantedActions = new();
}

public sealed partial class XenobioConsoleExitEvent : InstantActionEvent;

public sealed partial class XenobioConsoleScanSlimeEvent : InstantActionEvent;

public sealed partial class XenobioConsoleGrabSlimeEvent : InstantActionEvent;

public sealed partial class XenobioConsolePlaceSlimeEvent : InstantActionEvent;

public sealed partial class XenobioConsolePlaceMonkeyEvent : InstantActionEvent;

public sealed partial class XenobioConsoleRecycleMonkeyEvent : InstantActionEvent;

public sealed partial class XenobioConsoleMutationPotionEvent : InstantActionEvent;

public sealed partial class XenobioConsoleStabilizerPotionEvent : InstantActionEvent;

/// <summary>
/// Plays the bite lunge on a slime's sprite.
/// </summary>
[Serializable, NetSerializable]
public sealed class SlimeBiteAnimationMessage(NetEntity entity, Angle angle) : EntityEventArgs
{
    public readonly NetEntity Entity = entity;
    public readonly Angle Angle = angle;
}

/// <summary>
/// A handheld scanner that reports a slime's hunger and mutation chance.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeScannerComponent : Component;
