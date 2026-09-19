using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.FixedPoint;

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

/// <summary>
/// A head injury that muddles the patient rather than damaging the part: blurred sight, slurred speech and
/// a moment on the floor when it lands. Read by <see cref="WolfmedConcussionSystem"/>.
/// </summary>
/// <remarks>
/// Declared per stage, so a harder knock blurs more. Nothing treats it: the wound carries no damage types,
/// so no topical reaches it, and it fades on its own time (faster while the patient rests).
/// </remarks>
[DataDefinition]
public sealed partial class WolfmedConcussionBehavior : WoundBehavior
{
    /// <summary>Blur added to the patient's vision, on the same scale as eye damage (capped at 6).</summary>
    [DataField]
    public float Blur = 2f;

    /// <summary>Whether speech comes out slurred.</summary>
    [DataField]
    public bool Stutter = true;

    /// <summary>How long the patient is off their feet when the wound lands or worsens.</summary>
    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(2);

    /// <summary>Severity lost per minute while the patient is up and about.</summary>
    [DataField]
    public FixedPoint2 RecoveryPerMinute = FixedPoint2.New(2);

    /// <summary>Recovery multiplier while asleep or buckled to a medical bed.</summary>
    [DataField]
    public float RestMultiplier = 4f;
}

/// <summary>
/// A joint knocked out of place. The penalty itself is <see cref="WolfmedLimbPenaltyBehavior"/>; this says
/// what it takes to put the joint back, which is the only thing that clears the wound.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedDislocationBehavior : WoundBehavior
{
    /// <summary>Do-after for someone else setting the joint.</summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    /// <summary>Doing it to your own arm takes longer, because you flinch.</summary>
    [DataField]
    public float SelfMultiplier = 2.5f;

    /// <summary>Pain the wrench itself causes, on top of what the joint already hurts.</summary>
    [DataField]
    public FixedPoint2 Pain = FixedPoint2.New(15);

    /// <summary>Pain multiplier when the patient is their own medic.</summary>
    [DataField]
    public float SelfPainMultiplier = 2f;
}

/// <summary>
/// A bruised organ under the wound: the blow reached something inside the part without rupturing it.
/// Applied by <see cref="Content.Server._WF.Wolfmed.Wounds.WolfmedOrganContusionSystem"/>.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedOrganContusionBehavior : WoundBehavior
{
    /// <summary>Organ health taken by the blow.</summary>
    [DataField]
    public FixedPoint2 Damage = FixedPoint2.New(4);

    /// <summary>Health the organ is never taken below, so a contusion bruises and never destroys.</summary>
    [DataField]
    public FixedPoint2 MinRemainingHealth = FixedPoint2.New(1);
}
