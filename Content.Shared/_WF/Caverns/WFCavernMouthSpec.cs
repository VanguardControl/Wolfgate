using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Caverns;

/// <summary>How a cavern's mouths are placed and fitted out: the hole, its lip, the pad below and the climb point.</summary>
[DataDefinition]
public sealed partial class WFCavernMouthSpec
{
    /// <summary>Tiles per mouth cell; each cell holds at most one mouth.</summary>
    [DataField]
    public int CellSize = 96;

    /// <summary>Edge of the square hole in tiles, 1 or 2.</summary>
    [DataField]
    public int HoleSize = 2;

    /// <summary>The gate's first candidate, relative to the planet centre.</summary>
    [DataField]
    public Vector2i GateOffset = new(0, 24);

    /// <summary>Natural ground tiles a mouth may cut; every footprint tile must be one of them.</summary>
    [DataField(required: true)]
    public List<ProtoId<ContentTileDefinition>> GroundTiles = new();

    /// <summary>Natural ground entities a footprint must not touch, such as liquids and boulders.</summary>
    [DataField]
    public List<EntProtoId> Avoid = new();

    /// <summary>The cavern tile under the hole.</summary>
    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> LandingTile;

    /// <summary>How far the pinned, rock-free pad reaches around the hole in the cavern.</summary>
    [DataField]
    public int PadRadius = 3;

    /// <summary>The side of the hole whose lip holds the climb point.</summary>
    [DataField]
    public Direction ClimbSide = Direction.South;

    /// <summary>The unanchored pit entity over each hole tile.</summary>
    [DataField(required: true)]
    public EntProtoId Shade;

    /// <summary>The anchored climb entity in the cavern under the lip.</summary>
    [DataField(required: true)]
    public EntProtoId ClimbPoint;

    /// <summary>Decor anchored on up to two of the lip's corner tiles.</summary>
    [DataField]
    public List<EntProtoId> Rim = new();

    /// <summary>Base climb-up time in seconds, before surface gravity scales it.</summary>
    [DataField]
    public float ClimbSeconds = 4f;
}
