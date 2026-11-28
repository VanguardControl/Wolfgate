using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Modsuits;

/// <summary>A suit's look and build: its theme, part set, inbuilt modules and the plating that teaches it. Players call it a plate.</summary>
/// <remarks>Generated from the control units by <c>Tools/_WG/Modsuits/patterns.py</c>; edit the generator, not the output.</remarks>
[Prototype("modPattern")]
public sealed partial class ModPatternPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public ProtoId<ModThemePrototype> Theme;

    /// <summary>Part prototype per inventory slot, as on <see cref="Components.ModSuitComponent.Parts"/>.</summary>
    [DataField(required: true)]
    public Dictionary<string, EntProtoId> Parts = new();

    /// <summary>Modules built into this suit on top of the theme's own.</summary>
    [DataField]
    public List<EntProtoId> InbuiltModules = new();

    /// <summary>Construction tag of the plating item that teaches this pattern; null where no plating exists yet.</summary>
    [DataField]
    public ProtoId<TagPrototype>? Plating;

    /// <summary>The control unit this pattern was generated from.</summary>
    [DataField(required: true)]
    public EntProtoId Source;

    /// <summary>Civilian patterns are the ones a Protean keeps between rounds.</summary>
    [DataField]
    public bool Civilian;

    /// <summary>Nanomass a Protean spends to shift into this pattern.</summary>
    [DataField]
    public float ShiftCost = 10f;
}
