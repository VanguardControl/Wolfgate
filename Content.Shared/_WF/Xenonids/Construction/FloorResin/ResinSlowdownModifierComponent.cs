// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared._WF.Xenonids.Weeds;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction.FloorResin;

[RegisterComponent, NetworkedComponent]
public sealed partial class ResinSlowdownModifierComponent : Component
{
    [DataField]
    public float OutsiderSpeedModifier;

    [DataField]
    public float OutsiderSpeedModifierArmor;
}
