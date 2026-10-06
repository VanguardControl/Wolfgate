using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.LegStyle;

/// <summary>
/// Legs a species can pick instead of its own. A species listed here stands the other way by default.
/// </summary>
[Prototype("wfLegStyle")]
public sealed partial class LegStylePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Species that may pick these legs.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<SpeciesPrototype>> Species = new();

    /// <summary>
    /// The stance these legs give.
    /// </summary>
    [DataField(required: true)]
    public LegStance Stance;

    /// <summary>
    /// Base sprites drawn in place of the species' own, by layer.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<HumanoidVisualLayers, ProtoId<HumanoidSpeciesSpriteLayer>> Sprites = new();

    /// <summary>
    /// Clothing slots these legs reshape. Such a slot uses the map below, or none, instead of the species' map.
    /// </summary>
    [DataField]
    public HashSet<string> DisplacementSlots = new();

    /// <summary>
    /// Displacement maps for the reshaped slots.
    /// </summary>
    [DataField]
    public Dictionary<string, DisplacementData> Displacements = new();

    /// <summary>
    /// Maps used instead on a female body.
    /// </summary>
    [DataField]
    public Dictionary<string, DisplacementData> FemaleDisplacements = new();
}
