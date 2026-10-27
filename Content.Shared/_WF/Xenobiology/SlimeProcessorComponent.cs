using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenobiology;

/// <summary>
/// A machine that pulls in dead slimes next to it and turns them into extracts.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlimeProcessorComponent : Component
{
    public const string SlimeContainerName = "slimes";

    [DataField(required: true), AutoNetworkedField]
    public TimeSpan ProcessingTime;

    /// <summary>
    /// Extracts made from each slime.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public int YieldMultiplier;

    /// <summary>
    /// Time between pulling in slimes.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public TimeSpan SlimeAcquireCooldown;

    /// <summary>
    /// Total damage a slime needs before it is pulled in.
    /// </summary>
    [DataField]
    public float CollectDamageThreshold = 200f;

    [DataField]
    public float CollectRange = 1f;

    [DataField]
    public SoundSpecifier ProcessSound = new SoundPathSpecifier("/Audio/Machines/blender.ogg");
}

/// <summary>
/// A slime processor that is processing; removed when done.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ActiveSlimeProcessorComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan ProcessingFinishedMoment;
}

/// <summary>
/// A powered, idle slime processor that pulls in slimes.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class CollectingSlimeProcessorComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan SlimeAcquireMoment;
}
