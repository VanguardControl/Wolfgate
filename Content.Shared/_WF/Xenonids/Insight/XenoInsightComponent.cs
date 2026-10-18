// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared.Chat.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Insight;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoInsightSystem))]
public sealed partial class XenoInsightComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Insight;

    [DataField, AutoNetworkedField]
    public int MaxInsight = 10;

    [DataField, AutoNetworkedField]
    public ProtoId<EmotePrototype>? Emote = "XenoRoar";
}
