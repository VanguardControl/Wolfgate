// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.HiveLeader;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HiveLeaderGranterComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<EntityUid> Leaders = new();

    [DataField, AutoNetworkedField]
    public int MaxLeaders = 4;

    [DataField, AutoNetworkedField]
    public EntProtoId PheromoneRelayId = "XenoLeaderPheromoneRelay";
}
