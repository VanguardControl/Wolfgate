// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Rank;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoRankComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Rank;
}
