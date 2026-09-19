using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Marks a wound as an arterial bleed: a bleed that a dressing only slows, a tourniquet stops while it is
/// on, and only surgery or a suture over a stopped bleed actually closes. Read by
/// <see cref="WolfmedWoundTraitSystem"/> and by the Wolfmed halves of the bleeding and healing systems.
/// </summary>
/// <remarks>
/// A behavior rather than a component so it can be declared per stage: a wound may become arterial only
/// once it is bad enough. <see cref="WoundPrototype.TryGetBehavior{T}"/> resolves it at the wound's
/// current severity.
/// </remarks>
[DataDefinition]
public sealed partial class WolfmedArterialBleedBehavior : WoundBehavior
{
    /// <summary>
    /// Replaces the shared bleeding-rate multiplier table for the treatments listed here. The default says
    /// a dressing buys a little time and nothing more; anything absent keeps the ordinary multiplier, so a
    /// tourniquet still stops the flow outright.
    /// </summary>
    [DataField]
    public Dictionary<BleedingTreatment, float> TreatmentMultipliers = new()
    {
        [BleedingTreatment.Bandaged] = 0.6f,
    };

    /// <summary>
    /// Whether a topical's bloodloss modifier may eat the wound's bleeding severity. False keeps gauze from
    /// whittling an artery shut one roll at a time.
    /// </summary>
    [DataField]
    public bool TopicalsReduceBleeding;

    /// <summary>
    /// Whether the wound refuses every treatment while it is still losing blood. The bleed has to be
    /// stopped first, by a tourniquet or by the clamping step of the surgery.
    /// </summary>
    [DataField]
    public bool RequiresStoppedBleedToTreat = true;

    /// <summary>Part types a tourniquet can tie off. An artery in the torso or the neck has nowhere to tie.</summary>
    [DataField]
    public HashSet<BodyPartType> TourniquetableParts =
    [
        BodyPartType.Arm,
        BodyPartType.Hand,
        BodyPartType.Leg,
        BodyPartType.Foot,
    ];
}

/// <summary>
/// A mechanical penalty to the limb the wound is on, applied through the same two multipliers a fracture
/// uses: movement speed on a mobility part, hand-work duration on a manipulation part.
/// </summary>
/// <remarks>
/// Onyx's own route for this is <see cref="WoundFunctionalityBehavior"/>, which is dead here:
/// <c>wounds.body_part_functionality_enabled</c> ships false and stays false (P2-3), so
/// <see cref="BodyPartFunctionalitySystem"/> reports every part functional. This behavior hooks the
/// fracture branch of <see cref="FractureEffectSystem"/> instead, which is not gated on that CVar.
/// A fracture on the same part shadows it, as it already shadows the functionality fallback.
/// </remarks>
[DataDefinition]
public sealed partial class WolfmedLimbPenaltyBehavior : WoundBehavior
{
    /// <summary>Speed multiplier while the wound sits on a leg or a foot. 1 is no limp.</summary>
    [DataField]
    public float MovementModifier = 1f;

    /// <summary>Duration multiplier for what the matching hand does. 1 is no penalty.</summary>
    [DataField]
    public float ManipulationModifier = 1f;
}

/// <summary>
/// How much more likely than an ordinary wound this one is to go septic. Carries no effect by itself;
/// W5's infection timer reads it through <see cref="WolfmedWoundTraitSystem.GetInfectionRisk"/>.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedInfectionRiskBehavior : WoundBehavior
{
    [DataField]
    public float RiskMultiplier = 1f;
}
