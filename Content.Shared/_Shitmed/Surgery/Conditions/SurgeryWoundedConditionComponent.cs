using Content.Shared.Body.Part;
using Content.Shared.Damage.Prototypes; // WOLFGATE: EXT 1
using Content.Shared.FixedPoint; // WOLFGATE: EXT 1
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes; // WOLFGATE: EXT 1

namespace Content.Shared._Shitmed.Medical.Surgery.Conditions;

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryWoundedConditionComponent : Component
{
    /// <summary>Damage group the wound-severity window is measured over.</summary>
    [DataField] public ProtoId<DamageGroupPrototype> WoundGroup = "Brute"; // WOLFGATE: EXT 1 - P4-D19

    /// <summary>Lowest wound severity in the group this surgery lists at. Null keeps the pre-Wolfmed behaviour.</summary>
    [DataField] public FixedPoint2? MinWoundSeverity; // WOLFGATE: EXT 1 - P4-D19

    /// <summary>Highest wound severity in the group this surgery lists at. Null keeps the pre-Wolfmed behaviour.</summary>
    [DataField] public FixedPoint2? MaxWoundSeverity; // WOLFGATE: EXT 1 - P4-D19
}
