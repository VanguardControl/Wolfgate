// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared.FixedPoint;
using Content.Shared._WF.Xenonids.Construction;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Designer;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DesignerRemoteThickenResinComponent : Component
{
    [DataField, AutoNetworkedField]
    public int PlasmaCost = 60;

    [DataField, AutoNetworkedField]
    public float DoAfter = 1f;

    [DataField, AutoNetworkedField]
    public float Range = 15f;
}
