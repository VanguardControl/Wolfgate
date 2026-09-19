using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Surgery;

/// <summary>Gates a wound surgery on a matching wound being present on the selected part.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryWoundConditionComponent : Component
{
    [DataField] public ProtoId<WoundPrototype>? WoundPrototype;
    [DataField] public WoundVisibility? Visibility;
    [DataField] public WoundState? State;
    [DataField] public bool Bleeding;
    [DataField] public bool InternalBleeding;
    /// <summary>Inverts the whole test, so the surgery lists only while the wound is absent.</summary>
    [DataField] public bool Inverse;
}

/// <summary>Suture step: reduces the bleeding rate of the worst bleeding wound on the part.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryClampBleedingEffectComponent : Component
{
    [DataField(required: true)] public FixedPoint2 Amount;
    [DataField] public ProtoId<WoundPrototype>? WoundPrototype;
}

/// <summary>Treats a matching wound outright; the default Amount removes it in one step.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryTreatWoundEffectComponent : Component
{
    [DataField] public ProtoId<WoundPrototype>? WoundPrototype;
    [DataField] public bool InternalBleeding;
    [DataField] public FixedPoint2 Amount = FixedPoint2.MaxValue;
}

/// <summary>Gates a surgery on the selected part carrying a fracture in a grade/treatment window.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryFractureConditionComponent : Component
{
    [DataField] public FractureGrade MinGrade = FractureGrade.Hairline;
    [DataField] public FractureGrade? Grade;
    [DataField] public FractureTreatment? Treatment;
}

/// <summary>Advances the part's fracture to a treatment stage (Reduced by a bone setter, Mended by bone gel).</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryMendFractureEffectComponent : Component
{
    [DataField] public FractureTreatment Treatment = FractureTreatment.Mended;
}

/// <summary>Gates an organ surgery on a named organ slot on the selected part being damaged but alive.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryOrganDamagedConditionComponent : Component
{
    [DataField(required: true)] public string Slot = string.Empty;
    [DataField] public bool Inverse;
}

/// <summary>Restores health to a named organ slot on the selected part.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryOrganHealEffectComponent : Component
{
    [DataField(required: true)] public string Slot = string.Empty;
    [DataField] public FixedPoint2 Amount = 1;
}

/// <summary>Charges pain to the operated part when the step completes.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryPainEffectComponent : Component
{
    [DataField] public FixedPoint2 Amount = 5;
    /// <summary>Onyx's anaesthesia scale. Ships inert - Wolfgate has no consumer (P4-D22).</summary>
    [DataField] public FixedPoint2 SleepModifier = 1;
}

/// <summary>Opens a surgical incision as a real wound on the part, so it can bleed, be clamped and scar.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryIncisionWoundEffectComponent : Component
{
    [DataField] public FixedPoint2 Severity = 10;
    [DataField] public ProtoId<WoundPrototype> Wound = "SurgicalIncisionWound";
}

/// <summary>Clamps or closes the incision wounds this operation opened.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryIncisionTreatmentEffectComponent : Component
{
    [DataField] public ProtoId<WoundPrototype> Wound = "SurgicalIncisionWound";
    /// <summary>Clamp (the bleeders step) or Close (the close-incision step, which also rolls for a scar).</summary>
    [DataField] public WolfmedIncisionTreatment Treatment = WolfmedIncisionTreatment.Clamp;
}

/// <summary>Which half of the incision chain an incision treatment step runs.</summary>
public enum WolfmedIncisionTreatment : byte
{
    Clamp,
    Close,
}
