// WOLFGATE: P3-D5 (Option B). Onyx's ArmorComponent.Locational.cs datafields, added through a partial so
// Content.Shared/Armor/ArmorComponent.cs keeps zero edits. Read only by WolfmedPartArmorSystem; prototype-static
// and never mutated at runtime, so no [AutoNetworkedField] (ArmorPartModifier is a [DataDefinition], not
// NetSerializable). Onyx's `traumaDeductions` key is deliberately not ported (P3-D20: no C# datafield exists in
// Onyx either) and neither is `ShowArmorOnExamine`.

using Content.Shared.Body.Part;
using Content.Shared.Damage;

namespace Content.Shared.Armor;

public sealed partial class ArmorComponent
{
    /// <summary>Body part types this armour protects. Null or empty protects every part; the worn slot is irrelevant.</summary>
    [DataField] public HashSet<BodyPartType>? Coverage;

    /// <summary>Sides this armour protects. Null or empty protects every symmetry.</summary>
    [DataField] public HashSet<BodyPartSymmetry>? CoverageSymmetry;

    /// <summary>Ordered per-location modifier overrides; the first matching entry wins and skips the coverage gate.</summary>
    [DataField] public List<ArmorPartModifier> PartModifiers = [];
}

/// <summary>A location-specific armour modifier set, matched by part type and symmetry.</summary>
[DataDefinition]
public sealed partial class ArmorPartModifier
{
    /// <summary>Matching body part types. Empty matches every type.</summary>
    [DataField] public HashSet<BodyPartType> Parts = [];

    /// <summary>Matching sides. Empty matches every symmetry.</summary>
    [DataField] public HashSet<BodyPartSymmetry> Symmetry = [];

    /// <summary>Modifiers applied to the struck part's share of the damage.</summary>
    [DataField(required: true)] public DamageModifierSet Modifiers = default!;
}
