// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared._WF.Xenonids.Construction;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Designer;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DesignerDesignNodeVariantComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId Wall;

    [DataField(required: true), AutoNetworkedField]
    public EntProtoId Door;
}
