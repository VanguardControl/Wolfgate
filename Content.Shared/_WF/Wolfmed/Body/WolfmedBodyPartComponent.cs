using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>Wound-system data Onyx keeps on its own BodyPartComponent; Wolfgate stays on Shitmed's, so it lives here.</summary>
[RegisterComponent]
public sealed partial class WolfmedBodyPartComponent : Component
{
    /// <summary>Fracture profile for this part. Null = no fractures.</summary>
    [DataField] public ProtoId<FractureProfilePrototype>? FractureProfile;

    /// <summary>Structural damage cap; damage past it becomes tear-off pressure instead. Zero disables overflow entirely.</summary>
    [DataField] public FixedPoint2 MaxDamage;

    /// <summary>Per-damage-type totals at which the part becomes severable.</summary>
    [DataField] public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> AmputationThresholds = new();

    /// <summary>Minimum follow-up hit per damage type needed to detach a ruined part.</summary>
    [DataField] public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> DismembermentFinishingDamage = new();

    /// <summary>Severity of the consequence wound left on the parent when this part is torn off.</summary>
    [DataField] public FixedPoint2 AmputationConsequenceSeverity = 35;

    /// <summary>Overrides the host's per-part-type dismemberment severity.</summary>
    [DataField] public FixedPoint2? DismembermentSeverity;
}
