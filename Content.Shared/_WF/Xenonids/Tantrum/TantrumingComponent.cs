// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Tantrum;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TantrumingComponent : Component
{
    [DataField, AutoNetworkedField]
    public int ArmorGain = 15;

    [DataField, AutoNetworkedField]
    public TimeSpan ExpireAt;
}
