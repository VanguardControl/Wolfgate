// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Sweep;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoSweepingComponent : Component
{
    [DataField, AutoNetworkedField]
    public Direction? LastDirection;

    [DataField, AutoNetworkedField]
    public TimeSpan Delay = TimeSpan.FromSeconds(0.07);

    [DataField, AutoNetworkedField]
    public TimeSpan NextRotation;

    [DataField, AutoNetworkedField]
    public int TotalRotations;

    [DataField, AutoNetworkedField]
    public int MaxRotations = 4;
}
