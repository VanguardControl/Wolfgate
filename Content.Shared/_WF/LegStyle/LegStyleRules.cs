using Robust.Shared.Prototypes;

namespace Content.Shared._WF.LegStyle;

/// <summary>
/// Which leg styles a species has, read from the leg style prototypes.
/// </summary>
public static class LegStyleRules
{
    /// <summary>
    /// The legs a species draws for a stance, or null when those are its own.
    /// </summary>
    public static LegStylePrototype? Find(string species, LegStance stance, IPrototypeManager proto)
    {
        if (stance == LegStance.Default)
            return null;

        foreach (var style in proto.EnumeratePrototypes<LegStylePrototype>())
        {
            if (style.Stance == stance && style.Species.Contains(species))
                return style;
        }

        return null;
    }

    /// <summary>
    /// The stance a species can switch to, if it has one.
    /// </summary>
    public static bool TryGetAlternate(string species, IPrototypeManager proto, out LegStance alternate)
    {
        foreach (var style in proto.EnumeratePrototypes<LegStylePrototype>())
        {
            if (!style.Species.Contains(species))
                continue;

            alternate = style.Stance;
            return true;
        }

        alternate = LegStance.Default;
        return false;
    }

    /// <summary>
    /// The stance to save: Default unless the species has other legs for it.
    /// </summary>
    public static LegStance Validate(string species, LegStance stance, IPrototypeManager proto)
    {
        return Find(species, stance, proto) == null ? LegStance.Default : stance;
    }

    /// <summary>
    /// Whether a character with this saved stance stands digitigrade.
    /// </summary>
    public static bool IsDigitigrade(string species, LegStance stance, IPrototypeManager proto)
    {
        if (!TryGetAlternate(species, proto, out var alternate))
            return false;

        // The species' own legs are the opposite of the ones it can switch to.
        return Find(species, stance, proto) != null
            ? stance == LegStance.Digitigrade
            : alternate != LegStance.Digitigrade;
    }
}
