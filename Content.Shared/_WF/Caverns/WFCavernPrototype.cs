using Content.Shared._DV.Planet;
using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Caverns;

/// <summary>The cavern under one planet surface: the map it is built from, its seed and its light.</summary>
// Kind named explicitly: Robust would derive "wFCavern".
[Prototype("wfCavern")]
public sealed partial class WFCavernPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The surface this cavern lies under; one cavern per surface.</summary>
    [DataField(required: true)]
    public ProtoId<WFPlanetSurfacePrototype> Surface;

    /// <summary>The cavern's name, shown in examines.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The planet prototype the cavern map is spawned from: biome, map name, light and air.</summary>
    [DataField(required: true)]
    public ProtoId<PlanetPrototype> Level;

    /// <summary>Added to the surface seed, unchecked, to seed the cavern biome.</summary>
    [DataField]
    public int SeedOffset;

    /// <summary>The ambient colour under solid ground.</summary>
    [DataField]
    public Color RoofColor = Color.Black;

    /// <summary>The share of the ground's map light that reaches the cavern through its shafts.</summary>
    [DataField]
    public float ShaftLight = 0.5f;
}
