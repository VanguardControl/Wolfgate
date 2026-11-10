using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Blueprints;

/// <summary>A lost pattern the research console's locator can hunt for; recovering it unlocks its recipes.</summary>
[Prototype("wfBlueprintPattern")]
public sealed partial class WFBlueprintPatternPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>What it does; a technology's pattern may leave it out and is described by its discipline.</summary>
    [DataField]
    public LocId? Description;

    /// <summary>1 Recovered, 2 Lost, 3 Forgotten: where its shard lies and how long it takes to decode.</summary>
    [DataField]
    public int Tier = 1;

    /// <summary>What the locator shows for it.</summary>
    [DataField(required: true)]
    public EntProtoId Icon;

    /// <summary>The lathe recipes it unlocks on the linked research server.</summary>
    [DataField]
    public List<ProtoId<LatheRecipePrototype>> Recipes = new();

    /// <summary>A technology it unlocks; that technology can't be researched with points.</summary>
    [DataField]
    public ProtoId<TechnologyPrototype>? Technology;
}
