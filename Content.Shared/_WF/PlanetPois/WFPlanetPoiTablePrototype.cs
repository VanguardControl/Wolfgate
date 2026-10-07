using Content.Shared.Procedural;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetPois;

/// <summary>What a world rolls its points of interest from each round, and how they are spread over it.</summary>
[Prototype("wfPlanetPoiTable")]
public sealed partial class WFPlanetPoiTablePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The kinds of site this world can hold, drawn by weight.</summary>
    [DataField(required: true)]
    public List<WFPlanetPoiEntry> Entries = new();

    /// <summary>How many sites a world gets, inclusive.</summary>
    [DataField]
    public int MinCount = 4;

    [DataField]
    public int MaxCount = 8;

    /// <summary>Tiles between one site's origin and the next.</summary>
    [DataField]
    public float Spacing = 128f;

    /// <summary>Tiles a site's origin keeps from the world's edge, so the whole site fits inside it.</summary>
    [DataField]
    public float EdgeMargin = 96f;

    /// <summary>Tiles a site's origin keeps from the world's centre, where arrivals land and the gate mouth lies.</summary>
    [DataField]
    public float CentreClear = 96f;

    /// <summary>Half the side, in tiles, of the square round a candidate origin that must hold nothing pinned, such as a cavern mouth.</summary>
    [DataField]
    public int Footprint = 40;

    /// <summary>Metres from a site's origin at which someone on the ground identifies its signal.</summary>
    [DataField]
    public float RevealRange = 48f;
}

/// <summary>One kind of site: the dungeon generated for it and what its signal is called once identified.</summary>
[DataDefinition]
public sealed partial class WFPlanetPoiEntry
{
    [DataField(required: true)]
    public ProtoId<DungeonConfigPrototype> Dungeon;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public float Weight = 1f;
}
