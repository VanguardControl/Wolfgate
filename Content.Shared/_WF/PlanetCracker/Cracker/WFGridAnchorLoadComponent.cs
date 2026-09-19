using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>On a grid: the virtual mass its cargo anchors add to the pooled gravgen lift check (design D11).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFGridAnchorLoadComponent : Component
{
    /// <summary>Summed VirtualMass of the unanchored anchors and crates riding on this grid.</summary>
    [ViewVariables, AutoNetworkedField]
    public float VirtualMass;
}
