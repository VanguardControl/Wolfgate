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

    /// <summary>
    /// True once a disc has been cut out of this body. It lives here rather than on the z-network because the network
    /// can be torn down and rebuilt, and this is what the sector survey console enumerates.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Cracked;

    /// <summary>
    /// Whether cracking this world is legal, mirrored off the surface prototype by WFPlanetRegistrySystem.ApplySurface
    /// so a console reads it without indexing a prototype. It lives here rather than on WFPlanetNetworkComponent
    /// because that one is server-only, [UnsavedComponent] and destroyed by DeleteNetwork while the body lives on
    /// (WFPlanetNetworkSystem.cs:252-263).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Sanctioned = true;
}
