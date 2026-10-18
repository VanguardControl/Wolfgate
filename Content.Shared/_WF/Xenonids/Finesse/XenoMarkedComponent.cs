// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Finesse;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoMarkedComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan TimeAdded;

    [DataField, AutoNetworkedField]
    public TimeSpan WearOffAt;

    [DataField, AutoNetworkedField]
    public bool IsCriticalTag = false;
}
