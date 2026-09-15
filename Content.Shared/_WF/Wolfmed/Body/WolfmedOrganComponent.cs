using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

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
}
