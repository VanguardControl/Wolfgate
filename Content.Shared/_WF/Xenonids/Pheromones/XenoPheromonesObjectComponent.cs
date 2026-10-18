// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Pheromones;

// Component for adding pheromones to objects, e.g. spore resin fruit
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoPheromonesObjectComponent : Component
{
    [DataField, AutoNetworkedField]
    public XenoPheromones Pheromones = XenoPheromones.Recovery;
}
