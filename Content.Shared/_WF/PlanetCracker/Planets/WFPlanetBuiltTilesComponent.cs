using Robust.Shared.Map;

namespace Content.Shared._WF.PlanetCracker.Planets;

/// <summary>
/// The terrain a crew has built over on a planet layer: lattice goes straight onto natural ground there, and this is
/// what comes back when it is cut away again, instead of the hole a lattice's own base turf would leave.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetBuiltTilesComponent : Component
{
    [ViewVariables]
    public Dictionary<Vector2i, Tile> Underlay = new();
}
