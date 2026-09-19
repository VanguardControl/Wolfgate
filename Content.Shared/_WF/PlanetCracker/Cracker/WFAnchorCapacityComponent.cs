using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>On a hull's gravity generator: how many anchors it is rated to carry, and how many are aboard (design D11).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFAnchorCapacityComponent : Component
{
    /// <summary>How many crated or loose anchors this hull is rated to carry.</summary>
    [DataField, AutoNetworkedField]
    public int Capacity = 1;

    /// <summary>How many are aboard right now, recounted on a one-second throttle.</summary>
    [ViewVariables, AutoNetworkedField]
    public int Aboard;
}
