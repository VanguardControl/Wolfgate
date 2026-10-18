// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Construction;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoStructureUpgradeableComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId? To;

    [DataField(required: true), AutoNetworkedField]
    public FixedPoint2 Cost;
}
