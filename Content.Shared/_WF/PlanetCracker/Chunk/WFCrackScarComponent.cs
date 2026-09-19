using System.Numerics;

namespace Content.Shared._WF.PlanetCracker.Chunk;

/// <summary>
/// Every hole cut out of this ground layer, kept on the ground grid rather than on a chunk so it outlives the chunk.
/// BiomeSystem.ReserveTiles regenerates any empty tile in its bounds with no ModifiedTiles guard, and every grid
/// landing over the site calls it through ShuttleSystem.Smimsh, so the holes must be re-stamped from here.
/// </summary>
[RegisterComponent]
public sealed partial class WFCrackScarComponent : Component
{
    /// <summary>Every cut circle recorded on this ground grid, in the order they were cut.</summary>
    [DataField]
    public List<WFCrackScar> Scars = new();
}

/// <summary>One cut circle on the ground layer; membership is recomputed from these rather than stored per tile.</summary>
[DataDefinition]
public partial struct WFCrackScar
{
    /// <summary>Centre of the cut circle as a raw world XY on the ground layer.</summary>
    [DataField]
    public Vector2 Centre;

    /// <summary>Radius of the cut circle in tiles.</summary>
    [DataField]
    public float Radius;
}
