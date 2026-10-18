// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Hive;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HiveMemberComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Hive;
}
