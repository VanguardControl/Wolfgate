using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>
/// The anchor's half of a running beam, since surface viewers never have the orbiting projector in PVS.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFCrackBeamTargetComponent : Component
{
    /// <summary>World XY of the projector firing at this anchor; every z-layer of a planet shares world XY.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 ProjectorWorldPos;
}
