using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Warmth;

/// <summary>
/// What a body feels of the temperature around it: the air on its own tile, how warm its body is, how wet it is and
/// whether the cold or heat is warning it yet. Server-written each second; its owner's client reads it for the HUD,
/// the screen effect and the slowdown.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFWarmthComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>The air temperature on the body's own tile in whole degrees Celsius; null in vacuum.</summary>
    [DataField, AutoNetworkedField]
    public int? HereCelsius;

    [DataField, AutoNetworkedField]
    public WFBodyFeel Feel = WFBodyFeel.Comfortable;

    /// <summary>Which warning signs are showing, if any.</summary>
    [DataField, AutoNetworkedField]
    public WFWarmthStrain Strain = WFWarmthStrain.None;

    /// <summary>How strong the warning signs are, 0 at the warning line and 1 at the line where harm starts.</summary>
    [DataField, AutoNetworkedField]
    public float Severity;

    /// <summary>How soaked the body and its clothes are, from 0 dry to 1 drenched.</summary>
    [DataField, AutoNetworkedField]
    public float Wetness;

    /// <summary>The extra heat the wind takes off the body on an open tile, as a multiplier; 1 out of the wind.</summary>
    [ViewVariables]
    public float WindChill = 1f;

    /// <summary>When the next shiver or sweat message may show.</summary>
    [ViewVariables]
    public TimeSpan NextSign;

    /// <summary>When the next visible shiver or sweat may show to others.</summary>
    [ViewVariables]
    public TimeSpan NextVisibleSign;
}

/// <summary>How warm a body feels, from its real body temperature.</summary>
[Serializable, NetSerializable]
public enum WFBodyFeel : byte
{
    Freezing,
    Cold,
    Chilly,
    Comfortable,
    Warm,
    Hot,
    Overheating,
}

/// <summary>Which warning signs a body shows before the cold or heat harms it.</summary>
[Serializable, NetSerializable]
public enum WFWarmthStrain : byte
{
    None,
    Cold,
    Hot,
}
