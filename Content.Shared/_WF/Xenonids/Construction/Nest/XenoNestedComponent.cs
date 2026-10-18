// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._WF.Xenonids.Construction.Nest;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoNestedComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Nest;

    [DataField, AutoNetworkedField]
    public bool Detached;

    [DataField]
    public float IncubationMultiplier = 1.5f;

    [DataField, AutoNetworkedField]
    public NetUserId? GhostedId;
}
