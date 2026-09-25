using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Planets;

/// <summary>
/// The round-scoped planet registry entry, added to each spawned sector body that has a Wolfgate surface.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFSectorPlanetComponent : Component
{
    /// <summary>The surface definition for this body, or null when it has no Wolfgate surface.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<WFPlanetSurfacePrototype>? Surface;

    /// <summary>The z-network entity, once the stack has been built.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Network;

    /// <summary>The orbit layer map of this body's network, once built.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? OrbitMap;

    /// <summary>True once a disc has been cut out of this body; kept here because the z-network can be rebuilt.</summary>
    [DataField, AutoNetworkedField]
    public bool Cracked;

    /// <summary>Whether cracking this world is legal, mirrored off the surface prototype.</summary>
    [DataField, AutoNetworkedField]
    public bool Sanctioned = true;
}
