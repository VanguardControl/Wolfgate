using System.Numerics;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>
/// Records what a z-map network is, on the network entity itself. Server-only: nothing client-side needs it.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetNetworkComponent : Component
{
    /// <summary>The sector body this network belongs to, or null for a standalone network built by hand.</summary>
    [DataField]
    public EntityUid? Planet;

    /// <summary>The depth 0 biome map.</summary>
    [DataField]
    public EntityUid GroundMap;

    /// <summary>The top map, the only FTL door in or out.</summary>
    [DataField]
    public EntityUid OrbitMap;

    /// <summary>Every member map, ordered by depth ascending from 0.</summary>
    [DataField]
    public List<EntityUid> Layers = new();

    /// <summary>The surface definition this network was built from.</summary>
    [DataField]
    public ProtoId<WFPlanetSurfacePrototype> Surface;

    /// <summary>The planet centre in the world frame, matching what the shared FTL range gate measures.</summary>
    [DataField]
    public Vector2 Centre;
}
