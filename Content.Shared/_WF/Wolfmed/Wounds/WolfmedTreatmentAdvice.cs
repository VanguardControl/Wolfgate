using Content.Shared._Onyx.Wounds;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// UI3: where the analyzer's "what is this and what do I do" text comes from. Advice is locale data keyed
/// off the wound prototype id or off a condition name, never a table in C#, so a new wound is a prototype
/// plus two FTL lines and the coverage test fails until both exist.
/// </summary>
/// <remarks>
/// Two keys per subject. The short key is the tooltip, one or two lines. The steps key is the numbered
/// procedure the treatment window prints, as markup, one step per line. A <c>-mechanical</c> variant of
/// either is used when the part is a chassis and the advice differs; where it does not, the base key is
/// used for both.
/// </remarks>
public static class WolfmedTreatmentAdvice
{
    public const string ShortPrefix = "wolfmed-treatment-short-";
    public const string StepsPrefix = "wolfmed-treatment-steps-";
    public const string MechanicalSuffix = "-mechanical";

    private const string ConditionInfix = "cond-";
    private const string CategoryInfix = "cat-";

    /// <summary>
    /// Every part-level or body-level finding the panel draws that is not a wound row. The names are the
    /// tail of the locale key, and <see cref="MechanicalConditions"/> says which of them read differently
    /// on a chassis.
    /// </summary>
    public static readonly string[] Conditions =
    [
        "bleeding",
        "internal-bleeding",
        "fracture",
        "clotting",
        "embedded",
        "infection-local",
        "infection-spreading",
        "infection-septic",
        "necrosis",
        "necrosis-risk",
        "overheating",
        "impaired",
        "disabled",
        "unavailable",
        "sepsis",
        "blood-low",
    ];

    /// <summary>The conditions a chassis reports differently. W6 already splits their wording; the advice follows.</summary>
    public static readonly string[] MechanicalConditions =
    [
        "bleeding",
        "fracture",
        "clotting",
    ];

    /// <summary>The wounds both flesh and a chassis can carry, so both need a procedure.</summary>
    public static readonly string[] MechanicalWounds =
    [
        "ElectricalWound",
        "SurgicalIncisionWound",
        "DismembermentWound",
        "AmputationConsequenceWound",
        "SystemicBleedingWound",
    ];

    public static string ShortKey(string woundId) => ShortPrefix + Slug(woundId);

    public static string StepsKey(string woundId) => StepsPrefix + Slug(woundId);

    public static string ConditionShortKey(string condition) => ShortPrefix + ConditionInfix + condition;

    public static string ConditionStepsKey(string condition) => StepsPrefix + ConditionInfix + condition;

    public static string CategoryShortKey(WolfmedWoundCategory category) =>
        ShortPrefix + CategoryInfix + category.ToString().ToLowerInvariant();

    /// <summary>The condition name for a functionality state the panel draws.</summary>
    public static string FunctionalityCondition(BodyPartFunctionalityState state) =>
        state.ToString().ToLowerInvariant();

    /// <summary>The condition name for an infection stage the panel draws.</summary>
    public static string InfectionCondition(WolfmedInfectionStage stage) =>
        "infection-" + stage.ToString().ToLowerInvariant();

    /// <summary>A PascalCase prototype id as the kebab-case tail of a locale key.</summary>
    public static string Slug(string id)
    {
        var result = string.Empty;
        for (var i = 0; i < id.Length; i++)
        {
            if (i > 0 && char.IsUpper(id[i]))
                result += "-";

            result += char.ToLowerInvariant(id[i]);
        }

        return result;
    }
}
