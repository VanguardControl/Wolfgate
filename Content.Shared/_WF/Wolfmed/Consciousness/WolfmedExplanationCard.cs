using System.Linq;
using Content.Shared._WF.Wolfmed.Life;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// M2 (plan §5.2): the explanation card on the unconscious screen, as lines of text, drawn from the cause prototype
/// with no numbers. The cause; what else holds you down; what is happening, as a symptom; what will wake you, in its
/// conditional form while something else holds you (a faint says "shortly" only while nothing does); while Dying, the
/// brain's time as a coarse bar; and the rescue line when somebody is doing CPR or reading you with an analyzer.
/// Shared, so the client draws it and the tests read the same text.
/// </summary>
public static class WolfmedExplanationCard
{
    /// <summary>Every text line of the card, top to bottom. The bar is not text: see <see cref="Bar"/>.</summary>
    public static List<string> Lines(IPrototypeManager prototypes, WolfmedConsciousnessComponent consciousness,
        WolfmedCardComponent? card)
    {
        var lines = new List<string>();
        var proto = Cause(prototypes, consciousness.Cause, consciousness.Heartless); // M4: circulatory collapse

        lines.Add(Title(proto, consciousness));

        if (consciousness.Blockers != WolfmedCauseFlags.None)
        {
            var names = WolfmedCauses.Each(consciousness.Blockers)
                .Select(cause => Cause(prototypes, cause) is { } blocker
                    ? Loc.GetString(blocker.BlockerName)
                    : Loc.GetString("wolfmed-condition-cause-unknown"));
            lines.Add(Loc.GetString("wolfmed-card-blockers", ("blockers", string.Join(", ", names))));
        }

        if (proto?.Symptom is { } symptom)
            lines.Add(Loc.GetString(symptom));

        var blocked = consciousness.Blockers != WolfmedCauseFlags.None;
        var help = blocked && proto?.HelpOutBlocked is { } outBlocked ? outBlocked : proto?.HelpOut;
        if (help is { } helpKey)
            lines.Add(Loc.GetString(helpKey));

        if (card is { Cpr: true })
            lines.Add(Loc.GetString("wolfmed-card-cpr"));

        if (card is { Examined: true })
            lines.Add(Loc.GetString("wolfmed-card-examined"));

        return lines;
    }

    /// <summary>
    /// While Dying: tenths of the rescue window the brain still has (0 to 10), and the label for it. Null otherwise.
    /// </summary>
    public static (int Tenths, string Label)? Bar(IPrototypeManager prototypes, WolfmedConsciousnessComponent consciousness,
        WolfmedCardComponent? card)
    {
        if (card is not { Reserve: >= 0 } ||
            Cause(prototypes, consciousness.Cause, consciousness.Heartless) is not { Dying: true })
            return null;

        return (Math.Clamp((int) card.Reserve, 0, 10), Loc.GetString("wolfmed-card-bar"));
    }

    private static WolfmedConsciousnessCausePrototype? Cause(IPrototypeManager prototypes, WolfmedCause cause,
        bool heartless = false) =>
        cause != WolfmedCause.None &&
        prototypes.TryIndex<WolfmedConsciousnessCausePrototype>(WolfmedCauses.PrototypeId(cause, heartless), out var proto)
            ? proto
            : null;

    /// <summary>"Unconscious: blood loss", "Passed out: pain", "Cardiac arrest: blood loss": the Critical title.</summary>
    private static string Title(WolfmedConsciousnessCausePrototype? proto,
        WolfmedConsciousnessComponent consciousness)
    {
        if (proto == null)
            return Loc.GetString("wolfmed-condition-title-out", ("cause", Loc.GetString("wolfmed-condition-cause-unknown")));

        var name = Loc.GetString(proto.BlockerName);
        var source = proto.Sources.TryGetValue(consciousness.CauseSource, out var sourceName)
            ? Loc.GetString(sourceName)
            : Loc.GetString("wolfmed-condition-source-unknown");

        if (proto.TitleOut is { } title)
            return Loc.GetString(title, ("cause", name), ("source", source));

        var named = proto.Sources.TryGetValue(consciousness.CauseSource, out var sub)
            ? Loc.GetString("wolfmed-condition-cause-with-source", ("cause", name), ("source", Loc.GetString(sub)))
            : name;
        return Loc.GetString("wolfmed-condition-title-out", ("cause", named));
    }
}
