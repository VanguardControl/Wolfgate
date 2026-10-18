// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Acid;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CorrodibleComponent : Component
{
    // TODO RMC14 intel and nuke shouldn't be corrodible
    [DataField, AutoNetworkedField]
    public bool IsCorrodible = true;

    [DataField, AutoNetworkedField]
    public TimeSpan TimeToApply = TimeSpan.FromSeconds(4);

    [DataField, AutoNetworkedField]
    public bool Structure = false;

    [DataField, AutoNetworkedField]
    public float MeltTimeMult = 1;
}
