// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Hide;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoHideComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Hiding;
}
