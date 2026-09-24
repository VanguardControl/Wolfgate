using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>Organ health data Onyx keeps on its own OrganComponent; Wolfgate stays on Shitmed's, so it lives here.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedOrganComponent : Component
{
    /// <summary>Current organ health; at or below zero the organ stops working and is destroyed.</summary>
    [DataField, AutoNetworkedField] public FixedPoint2 Health = FixedPoint2.New(15);

    /// <summary>Health the organ starts at and is clamped to.</summary>
    [DataField, AutoNetworkedField] public FixedPoint2 MaxHealth = FixedPoint2.New(15);

    /// <summary>Wound left on the containing part when this organ is destroyed. Null leaves none.</summary>
    [DataField] public ProtoId<WoundPrototype>? DestructionWound;

    /// <summary>Severity of the wound left behind by destruction.</summary>
    [DataField] public FixedPoint2 DestructionWoundSeverity;

    /// <summary>M3 (plan §8): health fraction under which the organ is impaired and its band effects apply.</summary>
    [DataField] public float ImpairedBelow = 0.5f;

    /// <summary>
    /// M3: blood regeneration multiplier while this organ is impaired (the heart: 0.5). 1 means the organ has
    /// no say in regeneration.
    /// </summary>
    [DataField] public float ImpairedRegenFactor = 1f;

    /// <summary>OK, impaired or failed, from health against <see cref="ImpairedBelow"/>.</summary>
    public WolfmedOrganBand Band =>
        Health <= FixedPoint2.Zero ? WolfmedOrganBand.Failed
        : MaxHealth > FixedPoint2.Zero && Health.Float() / MaxHealth.Float() < ImpairedBelow ? WolfmedOrganBand.Impaired
        : WolfmedOrganBand.Ok;

    /// <summary>Health as a fraction of the maximum, 0 to 1.</summary>
    public float Fraction => MaxHealth > FixedPoint2.Zero ? Math.Clamp(Health.Float() / MaxHealth.Float(), 0f, 1f) : 1f;
}

/// <summary>M3 (plan §8): how an organ is doing, in the analyzer's words.</summary>
[Serializable, NetSerializable]
public enum WolfmedOrganBand : byte
{
    Ok = 0,
    Impaired = 1,
    Failed = 2,
}
