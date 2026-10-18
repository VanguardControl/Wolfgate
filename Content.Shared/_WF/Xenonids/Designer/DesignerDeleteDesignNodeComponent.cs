// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared._WF.Xenonids.Construction;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WF.Xenonids.Designer;

[RegisterComponent, AutoGenerateComponentState, NetworkedComponent]
public sealed partial class DesignerDeleteDesignNodeComponent : Component
{
    [DataField, AutoNetworkedField]
    public int PlasmaCost = 25;

    [DataField, AutoNetworkedField]
    public bool OnlyOwnNodes = true;
}
