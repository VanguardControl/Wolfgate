// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.ScissorCut;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DestroyOnXenoPierceScissorComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId SpawnPrototype;

    [DataField, AutoNetworkedField]
    public SoundSpecifier Sound;
}
