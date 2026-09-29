using System.Collections.Frozen;
using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.MismatchedParts;

/// <summary>
/// What the Mismatched parts option unlocks. For now it gives hair and facial hair to species that can't wear them;
/// a species that can keeps its own styles either way.
/// </summary>
public static class MismatchedPartsRules
{
    /// <summary>Whether the option opens this marking category.</summary>
    public static bool Opens(MarkingCategories category)
    {
        return category is MarkingCategories.Hair or MarkingCategories.FacialHair;
    }

    /// <summary>
    /// Whether the species wears this category on its own: it has points for it, a sprite layer that takes markings
    /// and at least one style.
    /// </summary>
    public static bool IsNative(MarkingCategories category, string species, MarkingManager markings, IPrototypeManager proto)
    {
        return NativeStyles(category, species, markings, proto) != null;
    }

    /// <summary>The styles of a category a character of this species may pick.</summary>
    public static IReadOnlyDictionary<string, MarkingPrototype> Styles(
        MarkingCategories category,
        string species,
        bool mismatchedParts,
        MarkingManager markings,
        IPrototypeManager proto)
    {
        if (!Opens(category))
            return markings.MarkingsByCategoryAndSpecies(category, species);

        if (NativeStyles(category, species, markings, proto) is { } native)
            return native;

        if (!mismatchedParts)
            return FrozenDictionary<string, MarkingPrototype>.Empty;

        // The species' own styles, if it has any, plus every style no species restricts.
        return markings.MarkingsByCategory(category)
            .Where(p => p.Value.SpeciesRestrictions == null || p.Value.SpeciesRestrictions.Contains(species))
            .ToDictionary(p => p.Key, p => p.Value);
    }

    /// <summary>
    /// Whether the option puts this style on the character: it is on, the species can't wear the category on its own,
    /// and the style is one the option opens for this species and sex.
    /// </summary>
    public static bool Unlocks(
        MarkingCategories category,
        string style,
        string species,
        Sex sex,
        bool mismatchedParts,
        MarkingManager markings,
        IPrototypeManager proto)
    {
        if (!mismatchedParts
            || !Opens(category)
            || IsNative(category, species, markings, proto)
            || !Styles(category, species, true, markings, proto).TryGetValue(style, out var prototype))
            return false;

        return prototype.SexRestriction == null || prototype.SexRestriction == sex;
    }

    /// <summary>The species' own styles if it wears the category on its own, otherwise null.</summary>
    private static IReadOnlyDictionary<string, MarkingPrototype>? NativeStyles(
        MarkingCategories category,
        string species,
        MarkingManager markings,
        IPrototypeManager proto)
    {
        if (!proto.TryIndex<SpeciesPrototype>(species, out var speciesProto))
            return null;

        if (proto.TryIndex<MarkingPointsPrototype>(speciesProto.MarkingPoints, out var points)
            && points.Points.TryGetValue(category, out var limit)
            && limit.Points <= 0)
            return null;

        if (Layer(category) is not { } layer
            || !proto.TryIndex<HumanoidSpeciesBaseSpritesPrototype>(speciesProto.SpriteSet, out var sprites)
            || !sprites.Sprites.TryGetValue(layer, out var layerId)
            || !proto.TryIndex<HumanoidSpeciesSpriteLayer>(layerId, out var layerProto)
            || !layerProto.AllowsMarkings)
            return null;

        var styles = markings.MarkingsByCategoryAndSpecies(category, species);
        return styles.Count > 0 ? styles : null;
    }

    private static HumanoidVisualLayers? Layer(MarkingCategories category)
    {
        return category switch
        {
            MarkingCategories.Hair => HumanoidVisualLayers.Hair,
            MarkingCategories.FacialHair => HumanoidVisualLayers.FacialHair,
            _ => null,
        };
    }
}
