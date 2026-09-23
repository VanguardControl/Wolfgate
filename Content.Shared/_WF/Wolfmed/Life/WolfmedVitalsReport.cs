using Content.Shared._WF.Wolfmed.Consciousness;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>The rung the analyzer names at the top of its vitals block (M1a, plan §5.5).</summary>
[Serializable, NetSerializable]
public enum WolfmedVitalsState : byte
{
    Up = 0,
    Downed = 1,

    /// <summary>Critical for a fixed time: a pain faint.</summary>
    Faint = 2,
    Unconscious = 3,
    Arrest = 4,

    /// <summary>A machine that has stopped: no power, no pump, or no hydraulic pressure.</summary>
    Shutdown = 5,
    Dead = 6,
}

/// <summary>Which way the blood (or oil) is going, from the bleed against regeneration.</summary>
[Serializable, NetSerializable]
public enum WolfmedBloodTrend : byte
{
    Steady = 0,
    Rising = 1,
    Falling = 2,

    /// <summary>Losing at least <c>wolfmed.analyzer_blood_fast</c> units a second.</summary>
    FallingFast = 3,
}

/// <summary>What the paddles would do right now, in the analyzer's words. Never "will work": a shock is a roll.</summary>
[Serializable, NetSerializable]
public enum WolfmedDefibVerdict : byte
{
    /// <summary>Not shown: the patient is conscious, or the body is not the defibrillator's to judge.</summary>
    Hidden = 0,
    Indicated = 1,
    PulsePresent = 2,
    NoHeart = 3,
    NoBlood = 4,
    BrainDead = 5,
    NoBrain = 6,
    Rotten = 7,

    /// <summary>Other content's <c>UnrevivableComponent</c>; <see cref="WolfmedVitalsReport.VerdictReason"/> names it.</summary>
    Unrevivable = 8,
}

/// <summary>
/// The analyzer's vitals block (M1a, plan §5.5): state and cause, breathing, circulation and the defib verdict.
/// Built on the server from the networked vitals and the life system; <see cref="WolfmedVitalsText"/> words it.
/// </summary>
[Serializable, NetSerializable]
public sealed class WolfmedVitalsReport
{
    public WolfmedVitalsState State;
    public WolfmedCause Cause;
    public WolfmedCauseSource Source;
    public WolfmedCauseFlags Blockers;

    /// <summary>A chassis: machine words, no pulse, no breathing, oil instead of blood.</summary>
    public bool Mechanical;

    /// <summary>Dead with the brain organ at 0 HP: catastrophic brain injury, not permanent loss.</summary>
    public bool BrainDestroyed;

    public WolfmedBreathing Breathing;
    public WolfmedBreathingSource BreathingSource;

    /// <summary>Sedation 0 to 1, for the depressed breathing line.</summary>
    public float Sedation;

    /// <summary>Mechanical: the coolant pump is present and working.</summary>
    public bool PumpRunning;

    public WolfmedBloodBand BloodBand;

    /// <summary>Blood (or oil) volume fraction, or -1 for a body with no bloodstream.</summary>
    public float Blood = -1f;

    public WolfmedBloodTrend Trend;

    /// <summary>Units to <see cref="Line"/>, 0 when already above it.</summary>
    public float UnitsToLine;

    /// <summary>The brain-safe blood line as a percentage (wolfmed.brain_blood_start): under it the brain drains.</summary>
    public float Line;

    public WolfmedDefibVerdict Verdict;

    /// <summary>The unrevivable reason's locale key, for <see cref="WolfmedDefibVerdict.Unrevivable"/>.</summary>
    public string? VerdictReason;

    /// <summary>For a blood refusal: units to the post-shock target plus the bleed over the grace.</summary>
    public float VerdictUnits;

    /// <summary>For a blood refusal: units to the brain-safe line, and that line as a percentage.</summary>
    public float VerdictSafeUnits;

    public float VerdictSafeLine;

    /// <summary>M1b: burn fluid loss in units a second, 0 when nothing weeps.</summary>
    public float BurnFluid;

    /// <summary>M1b: at or over <c>wolfmed.analyzer_burn_fast</c>.</summary>
    public bool BurnFluidFast;
}

/// <summary>
/// The words for <see cref="WolfmedVitalsReport"/>. One place, so the analyzer panel, the pod and the tests
/// read the same lines.
/// </summary>
public static class WolfmedVitalsText
{
    /// <summary>Every line the block shows, top to bottom.</summary>
    public static List<string> Lines(WolfmedVitalsReport report)
    {
        var lines = new List<string> { StateLine(report), BreathingLine(report) };
        if (CirculationLine(report) is { } circulation)
            lines.Add(circulation);

        if (BurnFluidLine(report) is { } burns)
            lines.Add(burns);

        if (VerdictLine(report) is { } verdict)
            lines.Add(verdict);

        return lines;
    }

    /// <summary>"DOWNED: blood loss", "FAINTED: pain", "CARDIAC ARREST: blood", "SHUTDOWN: no power".</summary>
    public static string StateLine(WolfmedVitalsReport report)
    {
        var state = report.State switch
        {
            WolfmedVitalsState.Up => Loc.GetString(report.Mechanical
                ? "wolfmed-vitals-state-up-mechanical"
                : "wolfmed-vitals-state-up"),
            WolfmedVitalsState.Dead => Loc.GetString(report.Mechanical ? "wolfmed-vitals-state-dead-mechanical"
                : report.BrainDestroyed ? "wolfmed-vitals-state-dead-brain"
                : "wolfmed-vitals-state-dead"),
            _ => Loc.GetString($"wolfmed-vitals-state-{report.State.ToString().ToLowerInvariant()}",
                ("cause", CauseName(report.Cause, report.Source, report.Mechanical))),
        };

        if (report.Blockers == WolfmedCauseFlags.None || report.State is WolfmedVitalsState.Up or WolfmedVitalsState.Dead)
            return state;

        // An arrest already names what stopped the heart; the same cause again as a blocker says nothing new.
        var shown = report.Blockers & ~ArrestSourceFlag(report);
        if (shown == WolfmedCauseFlags.None)
            return state;

        var blockers = new List<string>();
        foreach (var cause in WolfmedCauses.Each(shown))
            blockers.Add(CauseName(cause, WolfmedCauseSource.None, report.Mechanical));

        return Loc.GetString("wolfmed-vitals-blockers", ("state", state), ("blockers", string.Join(", ", blockers)));
    }

    private static WolfmedCauseFlags ArrestSourceFlag(WolfmedVitalsReport report) =>
        report.Cause != WolfmedCause.Arrest ? WolfmedCauseFlags.None : report.Source switch
        {
            WolfmedCauseSource.ArrestBlood => WolfmedCauseFlags.Blood,
            WolfmedCauseSource.ArrestOxygen => WolfmedCauseFlags.Hypoxia,
            _ => WolfmedCauseFlags.None,
        };

    /// <summary>
    /// The medic's short name for a cause. Arrest and shutdown name only what is behind them ("blood",
    /// "no power"); hypoxia adds its source.
    /// </summary>
    public static string CauseName(WolfmedCause cause, WolfmedCauseSource source, bool mechanical)
    {
        if (cause is WolfmedCause.Arrest or WolfmedCause.Shutdown && source != WolfmedCauseSource.None)
            return SourceName(source);

        var key = $"wolfmed-vitals-cause-{cause.ToString().ToLowerInvariant()}";
        var name = mechanical && Loc.TryGetString(key + "-mechanical", out var machine)
            ? machine
            : Loc.GetString(key);
        return cause == WolfmedCause.Hypoxia && source != WolfmedCauseSource.None
            ? Loc.GetString("wolfmed-vitals-cause-with-source", ("cause", name), ("source", SourceName(source)))
            : name;
    }

    private static string SourceName(WolfmedCauseSource source) =>
        Loc.GetString($"wolfmed-vitals-source-{source.ToString().ToLowerInvariant()}");

    /// <summary>"Breathing: normal", "Breathing: depressed: sedation 72%", "Breathing: none: no air, gasping".</summary>
    public static string BreathingLine(WolfmedVitalsReport report)
    {
        if (report.Mechanical)
            return Loc.GetString(report.PumpRunning ? "wolfmed-vitals-cooling-running" : "wolfmed-vitals-cooling-offline");

        return report.Breathing switch
        {
            WolfmedBreathing.Depressed => Loc.GetString("wolfmed-vitals-breathing-depressed",
                ("percent", (int) MathF.Round(report.Sedation * 100f))),
            WolfmedBreathing.Gasping => Loc.GetString("wolfmed-vitals-breathing-gasping"),
            WolfmedBreathing.None => Loc.GetString(
                $"wolfmed-vitals-breathing-none-{report.BreathingSource.ToString().ToLowerInvariant()}"),
            _ => Loc.GetString("wolfmed-vitals-breathing-normal"),
        };
    }

    /// <summary>
    /// "Circulation: pulse weak and rapid; blood 41%, falling fast; transfuse ≈ 27 u to 50%". A chassis gets
    /// its oil instead. Null for a machine with no oil to read.
    /// </summary>
    public static string? CirculationLine(WolfmedVitalsReport report)
    {
        var trend = Loc.GetString($"wolfmed-vitals-trend-{report.Trend.ToString().ToLowerInvariant()}");
        var percent = (int) MathF.Round(MathF.Max(0f, report.Blood) * 100f);
        var units = MathF.Ceiling(report.UnitsToLine);
        var line = MathF.Round(report.Line);

        if (report.Mechanical)
        {
            if (report.Blood < 0f)
                return null;

            return units > 0f
                ? Loc.GetString("wolfmed-vitals-hydraulics-refill",
                    ("percent", percent), ("trend", trend), ("units", units), ("line", line))
                : Loc.GetString("wolfmed-vitals-hydraulics", ("percent", percent), ("trend", trend));
        }

        var pulse = Loc.GetString($"wolfmed-vitals-pulse-{report.BloodBand.ToString().ToLowerInvariant()}");
        if (report.Blood < 0f)
            return Loc.GetString("wolfmed-vitals-circulation-no-blood", ("pulse", pulse));

        return units > 0f
            ? Loc.GetString("wolfmed-vitals-circulation-transfuse",
                ("pulse", pulse), ("percent", percent), ("trend", trend), ("units", units), ("line", line))
            : Loc.GetString("wolfmed-vitals-circulation", ("pulse", pulse), ("percent", percent), ("trend", trend));
    }

    /// <summary>M1b: "Fluid loss from burns: fast (1.4 u/s)", beside the circulation line. Null while nothing weeps.</summary>
    public static string? BurnFluidLine(WolfmedVitalsReport report)
    {
        if (report.Mechanical || report.BurnFluid <= 0f)
            return null;

        return Loc.GetString(report.BurnFluidFast ? "wolfmed-vitals-burn-fluid-fast" : "wolfmed-vitals-burn-fluid-slow",
            ("rate", MathF.Round(report.BurnFluid, 1)));
    }

    /// <summary>"Defib: shock indicated", or the refusal with what to do first. Null while hidden.</summary>
    public static string? VerdictLine(WolfmedVitalsReport report)
    {
        return report.Verdict switch
        {
            WolfmedDefibVerdict.Hidden => null,
            WolfmedDefibVerdict.NoBlood => Loc.GetString("wolfmed-vitals-verdict-noblood",
                ("percent", (int) MathF.Round(MathF.Max(0f, report.Blood) * 100f)),
                ("units", MathF.Ceiling(report.VerdictUnits)),
                ("safe", MathF.Ceiling(report.VerdictSafeUnits)),
                ("line", MathF.Round(report.VerdictSafeLine))),
            WolfmedDefibVerdict.Unrevivable => Loc.GetString("wolfmed-vitals-verdict-unrevivable",
                ("reason", report.VerdictReason is { } reason && Loc.TryGetString(reason, out var text)
                    ? text
                    : report.VerdictReason ?? string.Empty)),
            _ => Loc.GetString($"wolfmed-vitals-verdict-{report.Verdict.ToString().ToLowerInvariant()}"),
        };
    }
}
