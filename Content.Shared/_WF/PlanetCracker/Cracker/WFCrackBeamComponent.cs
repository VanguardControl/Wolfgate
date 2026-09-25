using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>
/// Added to a gravity projector while it is cutting; gates the beam overlay and names the beam's far end.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFCrackBeamComponent : Component
{
    /// <summary>The anchor this projector is firing at, or null while nothing is targeted.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Target;
}
