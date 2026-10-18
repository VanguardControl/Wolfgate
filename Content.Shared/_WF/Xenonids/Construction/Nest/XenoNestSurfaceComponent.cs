// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared._WF.Xenonids.Weeds;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Construction.Nest;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoNestSurfaceComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId? Nest; // XenoNest lands with the structures lane

    [DataField, AutoNetworkedField]
    public TimeSpan DoAfter = TimeSpan.FromSeconds(8);

    [DataField, AutoNetworkedField]
    public Dictionary<Direction, EntityUid> Nests = new();

    [DataField, AutoNetworkedField]
    public EntityUid? Weedable;
}
