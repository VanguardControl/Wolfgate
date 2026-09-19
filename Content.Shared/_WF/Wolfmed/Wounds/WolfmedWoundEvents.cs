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
