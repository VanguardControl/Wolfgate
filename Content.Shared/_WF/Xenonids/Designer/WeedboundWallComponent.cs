// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WF.Xenonids.Designer;

// Marks a wall or door as bound to weeds.
// When the bound weeds are destroyed, this wall collapses.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WeedboundWallComponent : Component
{
    [DataField, AutoNetworkedField]
    public NetEntity? BoundNode;

    [DataField, AutoNetworkedField]
    public NetEntity? BoundWeed;

    [DataField, AutoNetworkedField]
    public EntProtoId ResiduePrototype = "XenoStickyResinWeakWeedbound";

    [DataField, AutoNetworkedField]
    public EntProtoId ThickResiduePrototype = "XenoStickyResin";

    [DataField]
    public EntityUid? BoundWeedUid;

    [DataField, AutoNetworkedField]
    public bool IsThickVariant = false;
}
