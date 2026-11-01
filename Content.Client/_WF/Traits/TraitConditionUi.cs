using Content.Shared._WF.Traits;
using Content.Shared._WF.Traits.Conditions;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Traits;

/// <summary>
/// Condition checks and tooltip lines for the character editor.
/// </summary>
public static class TraitConditionUi
{
    /// <summary>
    /// Checks the conditions the editor can know from the profile. Job conditions stay open here because the
    /// character has not been assigned a job yet; the server checks them when the character spawns.
    /// </summary>
    public static bool AllMet(TraitPrototype trait, string speciesId, IPrototypeManager proto)
    {
        if (trait.Conditions.Count == 0)
            return true;

        var ctx = new TraitConditionContext
        {
            SpeciesId = speciesId,
            Proto = proto,
        };

        return TraitConditionEvaluator.AllMet(trait.Conditions, ctx);
    }

    /// <summary>
    /// Appends a tooltip line for each condition that describes a requirement.
    /// </summary>
    public static void AddTooltipLines(TraitPrototype trait, IPrototypeManager proto, List<string> lines)
    {
        var loc = IoCManager.Resolve<ILocalizationManager>();
        foreach (var condition in trait.Conditions)
        {
            var line = condition.GetTooltip(proto, loc);
            if (!string.IsNullOrEmpty(line))
                lines.Add(line);
        }
    }
}
