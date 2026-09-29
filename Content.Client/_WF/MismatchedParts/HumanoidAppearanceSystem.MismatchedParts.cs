using System.Linq;
using Content.Shared._WF.MismatchedParts;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;

namespace Content.Client.Humanoid;

public sealed partial class HumanoidAppearanceSystem
{
    /// <summary>
    /// Creator doll: copies the profile's Mismatched parts option and adds the hair and facial hair it unlocks, forced
    /// past the species' point budget.
    /// </summary>
    private void AddMismatchedHair(
        HumanoidAppearanceComponent humanoid,
        MarkingSet markings,
        HumanoidCharacterProfile profile,
        Marking hair,
        Marking facialHair)
    {
        humanoid.MismatchedParts = profile.MismatchedParts;
        AddMismatched(markings, profile, MarkingCategories.Hair, hair);
        AddMismatched(markings, profile, MarkingCategories.FacialHair, facialHair);
    }

    private void AddMismatched(MarkingSet markings, HumanoidCharacterProfile profile, MarkingCategories category, Marking marking)
    {
        if (!MismatchedPartsRules.Unlocks(category, marking.MarkingId, profile.Species, profile.Sex, profile.MismatchedParts, _markingManager, _prototypeManager))
            return;

        if (markings.TryGetCategory(category, out var worn) && worn.Any(m => m.MarkingId == marking.MarkingId))
            return;

        markings.AddBack(category, new Marking(marking) { Forced = true });
    }

    /// <summary>Mismatched hair and facial hair draw even where the species has no layer for them.</summary>
    private static bool DrawsMismatched(HumanoidAppearanceComponent humanoid, MarkingPrototype marking)
    {
        return humanoid.MismatchedParts && MismatchedPartsRules.Opens(marking.MarkingCategory);
    }
}
