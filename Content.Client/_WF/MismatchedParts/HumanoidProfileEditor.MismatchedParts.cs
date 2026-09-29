using Content.Shared._WF.MismatchedParts;
using Content.Shared.Humanoid.Markings;

namespace Content.Client.Lobby.UI;

/// <summary>The Mismatched parts toggle in the Body card, and the hair styles it opens.</summary>
public sealed partial class HumanoidProfileEditor
{
    private void InitializeMismatchedParts()
    {
        MismatchedPartsCheckBox.OnToggled += args => SetMismatchedParts(args.Pressed);
    }

    private void UpdateMismatchedParts()
    {
        MismatchedPartsCheckBox.Pressed = Profile?.MismatchedParts ?? false;
    }

    private void SetMismatchedParts(bool enabled)
    {
        if (Profile == null || Profile.MismatchedParts == enabled)
            return;

        Profile = Profile.WithMismatchedParts(enabled);
        EnforceSpeciesHair();
        UpdateHairPickers();
        UpdateCMarkingsHair();
        UpdateCMarkingsFacialHair();
        ReloadPreview();
    }

    /// <summary>The styles of a hair category the edited character may pick.</summary>
    private IReadOnlyDictionary<string, MarkingPrototype> HairStyleChoices(MarkingCategories category)
    {
        if (Profile == null)
            return new Dictionary<string, MarkingPrototype>();

        return MismatchedPartsRules.Styles(category, Profile.Species, Profile.MismatchedParts, _markingManager, _prototypeManager);
    }
}
