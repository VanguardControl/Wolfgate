using Content.Shared.FixedPoint;
using Content.Shared.Radio;
using Robust.Shared.Enums;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenobiology.Potions;

/// <summary>
/// Adds one extract to what a slime yields when processed.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeSteroidPotionComponent : Component;

/// <summary>
/// Changes a slime's mutation chance by <see cref="Amount"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeMutationPotionComponent : Component
{
    [DataField]
    public FixedPoint2 Amount = 0.12;
}

/// <summary>
/// Gives a slime extract one more use.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeExtractEnhancerPotionComponent : Component;

/// <summary>
/// Moves the user's mind into a mindless body that can hold one.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeMindTransferencePotionComponent : Component;

/// <summary>
/// Makes a mindless creature a ghost role.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeSentiencePotionComponent : Component;

/// <summary>
/// Makes worn clothing fully fireproof and heatproof.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlimeFireproofPotionComponent : Component
{
    [DataField, AutoNetworkedField]
    public int RemainingUses = 3;
}

/// <summary>
/// Gives a speaking creature an intrinsic radio on <see cref="Channels"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeBluespaceRadioPotionComponent : Component
{
    [DataField(required: true)]
    public HashSet<ProtoId<RadioChannelPrototype>> Channels = new();
}

/// <summary>
/// Halves the slowdown of a piece of clothing.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeSpeedPotionComponent : Component;

/// <summary>
/// Sets a humanoid's gender to the one picked by verb.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlimeGenderChangePotionComponent : Component
{
    [DataField, AutoNetworkedField]
    public Gender? Gender;
}

/// <summary>
/// Renames a creature with a mind to the name typed into its window.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlimeNameChangePotionComponent : Component
{
    [DataField, AutoNetworkedField]
    public string AssignedName = string.Empty;
}

[Serializable, NetSerializable]
public enum SlimeNameChangePotionUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SlimeNameChangePotionNewNameChangedMessage(string newName) : BoundUserInterfaceMessage
{
    public readonly string NewName = newName;
}
