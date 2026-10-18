// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Bombard;

[RegisterComponent, NetworkedComponent]
public sealed partial class XenoBombardComponent : Component
{
    [DataField]
    public FixedPoint2 PlasmaCost = 200;

    [DataField]
    public EntProtoId Projectile = "XenoBombardAcidProjectile";
}
