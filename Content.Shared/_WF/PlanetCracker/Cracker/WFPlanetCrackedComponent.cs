using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>On a sector body: a disc has been cut out of it; kept on the body because the z-network can be rebuilt.</summary>
[RegisterComponent, NetworkedComponent, UnsavedComponent]
public sealed partial class WFPlanetCrackedComponent : Component;
