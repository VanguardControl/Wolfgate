using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCities;

/// <summary>
/// A hand-made multi-level city stamped onto a world every round at a random spot, from a game map's levels: the
/// main map on the ground, its maps above on the flight layers and its maps below underground. The game map's station
/// becomes joinable there.
/// </summary>
[Prototype("wfFixedCity")]
public sealed partial class WFFixedCityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The world it is built on.</summary>
    [DataField(required: true)]
    public ProtoId<WFPlanetSurfacePrototype> Surface;

    /// <summary>The game map whose levels and station config are used. A plain id: the client ignores game maps.</summary>
    [DataField(required: true)]
    public string GameMap = string.Empty;

    /// <summary>Which of the game map's stations to create.</summary>
    [DataField(required: true)]
    public string Station = string.Empty;

    /// <summary>
    /// Where the levels' tiles lie, in the ground level's own coordinates: held as soon as the world starts loading, so
    /// other claims keep out while the city loads. Anything outside it is left out.
    /// </summary>
    [DataField(required: true)]
    public Box2i Footprint;

    /// <summary>The site's name on signals and radars.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Entities swapped on load; a null replacement deletes the entity.</summary>
    [DataField]
    public Dictionary<EntProtoId, EntProtoId?> Replace = new();

    /// <summary>
    /// Bare rock on the levels below the street. Where the layer grows its own rock it is dropped with its tiles before
    /// loading, so the world's underground fills in around the city; elsewhere it is swapped as <see cref="Replace"/> says.
    /// </summary>
    [DataField]
    public HashSet<EntProtoId> Terrain = new();

    /// <summary>Whether godmode is taken off everything in the city, so all of it can be destroyed.</summary>
    [DataField]
    public bool StripGodmode = true;

    /// <summary>Roads cut from the city's own streets out to the edge of its footprint, where the world's roads join.</summary>
    [DataField]
    public List<WFFixedCityEntrance> Entrances = new();
}

/// <summary>A straight road cut from a street tile in the ground level out to the edge of the city's footprint.</summary>
[DataDefinition]
public sealed partial class WFFixedCityEntrance
{
    /// <summary>The street tile the cut starts from, in the ground level's own coordinates.</summary>
    [DataField(required: true)]
    public Vector2i Start;

    [DataField(required: true)]
    public WFPlanetPoiDirection Direction;

    /// <summary>Width of the cut, centred on the start tile; odd.</summary>
    [DataField]
    public int Width = 5;

    [DataField]
    public string Tile = "FloorAsphalt";
}
