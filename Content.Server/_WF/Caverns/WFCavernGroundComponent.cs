using Content.Shared._WF.Caverns;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Caverns;

/// <summary>Links a planet's ground map to the cavern below it.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFCavernGroundComponent : Component
{
    /// <summary>The cavern map below this ground.</summary>
    [ViewVariables]
    public EntityUid Cavern;

    /// <summary>The cavern prototype it was built from.</summary>
    [ViewVariables]
    public ProtoId<WFCavernPrototype> Prototype;
}
