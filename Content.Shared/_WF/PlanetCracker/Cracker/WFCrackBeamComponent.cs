using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>
/// Added to a gravity projector at runtime while it is cutting (design D4); the beam overlay's on/off gate and the
/// only place the far end of the beam is named.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFCrackBeamComponent : Component
{
    /// <summary>The anchor this projector is firing at, or null while nothing is targeted.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Target;
}
