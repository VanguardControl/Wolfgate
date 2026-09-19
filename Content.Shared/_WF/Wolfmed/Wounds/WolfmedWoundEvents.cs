using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Raised on a body part for one damage type of one hit, before Onyx creates its default wound for that
/// type. The single extension point the Wolfmed wound rules answer from; handlers that create their own
/// wound set <see cref="SuppressDefault"/> to take the default one's place.
/// </summary>
[ByRefEvent]
public record struct WolfmedWoundSelectionEvent(
    EntityUid Body,
    EntityUid Part,
    ProtoId<DamageTypePrototype> DamageType,
    FixedPoint2 Amount,
    EntityUid? Origin,
    EntityUid? Tool,
    bool IsExplosion,
    float SeverityMultiplier,
    bool SuppressDefault = false);

/// <summary>
/// Broadcast by <see cref="WolfmedWoundRuleSystem"/> for every damage type of every hit that reaches a
/// body part, with the cause already derived. The wound-selection event it is raised from is directed and
/// single-owner, so systems that need to answer a hit rather than a wound listen here.
/// </summary>
/// <remarks>
/// Raised before the rules run, so a handler sees the part as the hit found it. Server-only, because
/// <c>WoundSystem.HandlePartDamageApplied</c> is.
/// </remarks>
[ByRefEvent]
public readonly record struct WolfmedPartDamageEvent(
    EntityUid Body,
    EntityUid Part,
    ProtoId<DamageTypePrototype> DamageType,
    FixedPoint2 Amount,
    WolfmedWoundCause Cause,
    EntityUid? Origin,
    EntityUid? Tool);

/// <summary>Which end of a wound's life the lifecycle event reports.</summary>
public enum WolfmedWoundLifecycle : byte
{
    Created,
    Changed,
    Removed,
}

/// <summary>
/// Broadcast by <see cref="WolfmedWoundTraitSystem"/> whenever a wound is created, changed or removed.
/// The directed <c>WoundComponent</c> and <c>WoundableComponent</c> subscriptions for those three events
/// are already held (by the trait system and by Onyx's WoundStatusEffectSystem), and a component/event
/// pair can only have one owner, so Wolfmed systems that need wound lifecycle answer here instead.
/// </summary>
[ByRefEvent]
public readonly record struct WolfmedWoundLifecycleEvent(
    WolfmedWoundLifecycle Kind,
    EntityUid Part,
    EntityUid Wound,
    ProtoId<WoundPrototype> Prototype,
    FixedPoint2 OldSeverity,
    FixedPoint2 Severity);

/// <summary>
/// A limb has just been torn off by damage. Broadcast from <c>AmputationSystem.TryAmputate</c>, which is
/// the only damage-driven detach path; a surgical removal goes through <c>TryDetachPart</c> and raises
/// nothing, which is what keeps the operating table quiet.
/// </summary>
/// <remarks>
/// Raised after the part is detached and after the stump's wounds are created, so a handler sees the
/// finished state. The part is no longer a child of the body.
/// </remarks>
[ByRefEvent]
public readonly record struct WolfmedPartAmputatedEvent(EntityUid Body, EntityUid Part, EntityUid Parent);

/// <summary>
/// Antiseptic has reached this body's skin. Raised directed on the body by the
/// <c>WolfmedCleanWounds</c> entity effect, which sits in Shared while
/// <see cref="Content.Server._WF.Wolfmed.Wounds.WolfmedInfectionSystem"/> does not; handlers report how
/// many wounds they cleaned so the effect knows whether to say anything.
/// </summary>
[ByRefEvent]
public record struct WolfmedCleanWoundsEvent(int Cleaned = 0);

/// <summary>
/// An antibiotic is being metabolised. <paramref name="Units"/> is the dose this tick, already scaled by
/// the reagent's own quantity. Raised directed on the body for the same reason as the wash event.
/// </summary>
[ByRefEvent]
public record struct WolfmedAntibioticEvent(float Units, bool Treated = false);

/// <summary>
/// A rejuvenate has just cleared this entity's wounds. <paramref name="Target"/> is a wound host, or a
/// detached part that was healed on its own.
/// </summary>
/// <remarks>
/// Broadcast because both <c>WoundHostComponent</c> and <c>WoundableComponent</c> already own their
/// <c>RejuvenateEvent</c> pair, and because the Wolfmed state a heal has to undo (sepsis on the body, dead
/// tissue and a tourniquet on the parts) is not a wound and so survives the clear.
/// </remarks>
[ByRefEvent]
public readonly record struct WolfmedRejuvenateEvent(EntityUid Target);

/// <summary>Prying one embedded object out of a wound.</summary>
[Serializable, NetSerializable]
public sealed partial class WolfmedEmbeddedRemovalDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Wound;

    /// <summary>A surgical tool; false means an improvised sharp item, which hurts and cuts.</summary>
    public readonly bool Clean;

    public WolfmedEmbeddedRemovalDoAfterEvent(NetEntity wound, bool clean)
    {
        Wound = wound;
        Clean = clean;
    }
}

/// <summary>Holding something hot against an open bleed until it stops.</summary>
[Serializable, NetSerializable]
public sealed partial class WolfmedCauteryDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Part;

    public WolfmedCauteryDoAfterEvent(NetEntity part)
    {
        Part = part;
    }
}

/// <summary>Forcing a dislocated joint back into place.</summary>
[Serializable, NetSerializable]
public sealed partial class WolfmedRelocateDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Wound;

    public WolfmedRelocateDoAfterEvent(NetEntity wound)
    {
        Wound = wound;
    }
}
