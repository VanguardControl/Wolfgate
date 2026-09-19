using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>
/// The anchor's half of a running cut's beam (design D4): the firing projector is on the orbit map and is never in a
/// surface viewer's PVS, so the anchor — which always is — carries the far end itself. Present only while the beam is.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFCrackBeamTargetComponent : Component
{
    /// <summary>World XY of the projector firing at this anchor; every z-layer of a planet shares world XY.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 ProjectorWorldPos;
}
