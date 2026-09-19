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
