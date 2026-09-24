using Content.Shared._WF.Wolfmed.Body;
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

    /// <summary>M5: the core is still under the cold arrest line; rewarm first.</summary>
    TooCold = 9,
}

/// <summary>M5 (plan §3.8): the toxin load against its lines.</summary>
[Serializable, NetSerializable]
public enum WolfmedToxinBand : byte
{
    Low = 0,

    /// <summary>At or past the Downed line.</summary>
    High = 1,

    /// <summary>At or past the coma line: the brain is draining.</summary>
    Coma = 2,
}

/// <summary>M5 (plan §3.8, OD13): what the liver is doing for toxin clearance.</summary>
[Serializable, NetSerializable]
public enum WolfmedLiverState : byte
{
    None = 0,
    Working = 1,
    Impaired = 2,
    Failed = 3,
}

/// <summary>M5 (plan §3.9): the marrow route's stage.</summary>
[Serializable, NetSerializable]
public enum WolfmedRadiationBand : byte
{
    Low = 0,

    /// <summary>Past wolfmed.rad_marrow_stop: no blood regenerates.</summary>
    Suppressed = 1,

    /// <summary>Past wolfmed.rad_marrow_bleed: blood is lost as well.</summary>
    Failing = 2,
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

    /// <summary>Playtest 2: whole seconds left in a pain faint, -1 when none runs.</summary>
    public int FaintSeconds = -1;

    /// <summary>M3 (plan §8): every organ with Wolfmed data that is impaired or failed, in medical reading order.</summary>
    public List<WolfmedOrganReading> Organs = new();

    /// <summary>M2 (plan §5.5): every process making the patient worse right now, each named with its first aid.</summary>
    public WolfmedRoutes Routes;

    /// <summary>M2: why the heart last stopped, as an Arrest* source; None outside wolfmed.arrest_cause_memory_seconds.</summary>
    public WolfmedCauseSource RestartCause;

    /// <summary>M2: that cause is still there.</summary>
    public bool RestartPresent;

    /// <summary>M2: for blood still present, the units to the post-shock target plus the bleed over the grace; -1 none.</summary>
    public float RestartUnits = -1f;

    /// <summary>M2: units to the brain-safe line, the grace left in seconds, and that line as a percentage.</summary>
    public float RestartSafeUnits;

    public float RestartGraceSeconds;
    public float RestartSafeLine;

    /// <summary>M2 (plan §7.2): a dead chassis's restart button, in words. Hidden for anything else.</summary>
    public WolfmedRestartVerdict Restart;

    /// <summary>M5 (plan §3.8): the toxin load, -1 when there is none.</summary>
    public float Toxin = -1f;

    public WolfmedToxinBand ToxinBand;
    public WolfmedLiverState Liver;

    /// <summary>M5 (plan §3.9): systemic radiation, -1 when there is none.</summary>
    public float Radiation = -1f;

    public WolfmedRadiationBand RadiationBand;

    /// <summary>M5: blood the failing marrow costs, units a second.</summary>
    public float MarrowLoss;

    /// <summary>M5 (plan §3.10): the core temperature in kelvin while it is cold or hot enough to count, else -1.</summary>
    public float Core = -1f;

    /// <summary>M5: <see cref="Core"/> is on the cold side.</summary>
    public bool CoreCold;

    /// <summary>M5: for a cold refusal, the core and the line it has to be rewarmed past, in kelvin.</summary>
    public float VerdictCore;

    public float VerdictRewarm;
}

/// <summary>M2 (plan §7.2): what a dead chassis's restart button would do, in the analyzer's words.</summary>
[Serializable, NetSerializable]
public enum WolfmedRestartVerdict : byte
{
    Hidden = 0,
    Ready = 1,
    NoHead = 2,
    NoCore = 3,
    CoreDestroyed = 4,
    NoPump = 5,
    NoPower = 6,

    /// <summary>Rot or other content's unrevivable reason.</summary>
    Refused = 7,
}

/// <summary>M3: one organ the analyzer names, by its body slot, and how it is doing.</summary>
[Serializable, NetSerializable]
public readonly record struct WolfmedOrganReading(string Slot, WolfmedOrganBand Band);

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

        // M5 (plan §3.8-3.10): toxins and the liver, radiation and the marrow, the core temperature.
        if (ToxinLine(report) is { } toxins)
            lines.Add(toxins);

        if (RadiationLine(report) is { } radiation)
            lines.Add(radiation);

        if (CoreTemperatureLine(report) is { } core)
            lines.Add(core);

        // M2 (plan §5.5): what is getting worse, and what the last arrest was.
        if (RoutesLine(report) is { } routes)
            lines.Add(routes);

        if (RestartLine(report) is { } restart)
            lines.Add(restart);

        if (OrganLine(report) is { } organs)
            lines.Add(organs);

        if (VerdictLine(report) is { } verdict)
            lines.Add(verdict);

        if (RestartVerdictLine(report) is { } button)
            lines.Add(button);

        return lines;
    }

    /// <summary>
    /// M2: "Getting worse: bleeding (pressure, gauze, tourniquet); sepsis (antibiotics)", or "Getting worse: nothing
    /// now" for a patient who is down but stable (§1.5's fourth priority). Not shown on the dead, nor on somebody up
    /// with nothing running.
    /// </summary>
    public static string? RoutesLine(WolfmedVitalsReport report)
    {
        if (report.State == WolfmedVitalsState.Dead)
            return null;

        if (report.Routes == WolfmedRoutes.None)
            return report.State == WolfmedVitalsState.Up ? null : Loc.GetString("wolfmed-vitals-routes-none");

        var names = new List<string>();
        for (var bit = 0; bit < 16; bit++)
        {
            var route = (WolfmedRoutes) (1 << bit);
            if ((report.Routes & route) == 0)
                continue;

            var key = $"wolfmed-vitals-route-{route.ToString().ToLowerInvariant()}";
            names.Add(report.Mechanical && Loc.TryGetString(key + "-mechanical", out var machine) ? machine : Loc.GetString(key));
        }

        return Loc.GetString("wolfmed-vitals-routes", ("routes", string.Join("; ", names)));
    }

    /// <summary>
    /// M2: "After a restart: arrest cause blood. Still present: yes. Transfuse ≈ 30 u within 45 s ...". Kept for
    /// wolfmed.arrest_cause_memory_seconds after the heart started again.
    /// </summary>
    public static string? RestartLine(WolfmedVitalsReport report)
    {
        if (report.RestartCause == WolfmedCauseSource.None)
            return null;

        var cause = SourceName(report.RestartCause);
        if (!report.RestartPresent || report.RestartUnits < 0f)
        {
            return Loc.GetString("wolfmed-vitals-restart", ("cause", cause),
                ("present", Loc.GetString(report.RestartPresent ? "wolfmed-vitals-yes" : "wolfmed-vitals-no")));
        }

        return report.RestartGraceSeconds > 0f
            ? Loc.GetString("wolfmed-vitals-restart-transfuse", ("cause", cause),
                ("units", MathF.Ceiling(report.RestartUnits)), ("seconds", MathF.Ceiling(report.RestartGraceSeconds)),
                ("safe", MathF.Ceiling(report.RestartSafeUnits)), ("line", MathF.Round(report.RestartSafeLine)))
            : Loc.GetString("wolfmed-vitals-restart-transfuse-late", ("cause", cause),
                ("safe", MathF.Ceiling(report.RestartSafeUnits)), ("line", MathF.Round(report.RestartSafeLine)));
    }

    /// <summary>M2 (plan §7.2): "Restart: ready" or "Restart: refused: core destroyed, core repair first". Dead chassis only.</summary>
    public static string? RestartVerdictLine(WolfmedVitalsReport report) =>
        report.Restart == WolfmedRestartVerdict.Hidden
            ? null
            : Loc.GetString($"wolfmed-vitals-restart-verdict-{report.Restart.ToString().ToLowerInvariant()}");

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
        {
            // Playtest 2: a faint that nothing else holds says when it ends; a blocked one names the blocker instead.
            return report.State == WolfmedVitalsState.Faint && report.FaintSeconds >= 0
                ? Loc.GetString("wolfmed-vitals-state-faint-timed",
                    ("cause", CauseName(report.Cause, report.Source, report.Mechanical)), ("seconds", report.FaintSeconds))
                : state;
        }

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
            WolfmedCauseSource.ArrestCold => WolfmedCauseFlags.Cold, // M5
            WolfmedCauseSource.ArrestToxin => WolfmedCauseFlags.Toxin,
            WolfmedCauseSource.ArrestHeat => WolfmedCauseFlags.Heat,
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
            WolfmedBreathing.Laboured => Loc.GetString("wolfmed-vitals-breathing-laboured"), // M3
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

    /// <summary>M5 (plan §3.8): "Toxins: 72, high; liver clearing". Null while the body carries no Poison.</summary>
    public static string? ToxinLine(WolfmedVitalsReport report)
    {
        if (report.Mechanical || report.Toxin <= 0f)
            return null;

        return Loc.GetString("wolfmed-vitals-toxins",
            ("load", (int) MathF.Round(report.Toxin)),
            ("band", Loc.GetString($"wolfmed-vitals-toxin-band-{report.ToxinBand.ToString().ToLowerInvariant()}")),
            ("liver", Loc.GetString($"wolfmed-vitals-liver-{report.Liver.ToString().ToLowerInvariant()}")));
    }

    /// <summary>M5 (plan §3.9): "Radiation: 120, marrow failing: blood not regenerating, losing 0.1 u/s".</summary>
    public static string? RadiationLine(WolfmedVitalsReport report)
    {
        if (report.Mechanical || report.Radiation <= 0f)
            return null;

        return Loc.GetString($"wolfmed-vitals-radiation-{report.RadiationBand.ToString().ToLowerInvariant()}",
            ("dose", (int) MathF.Round(report.Radiation)), ("rate", MathF.Round(report.MarrowLoss, 2)));
    }

    /// <summary>M5 (plan §3.10): "Core temperature: 271 K, hypothermic". Null while the core is near normal.</summary>
    public static string? CoreTemperatureLine(WolfmedVitalsReport report)
    {
        if (report.Mechanical || report.Core < 0f)
            return null;

        return Loc.GetString(report.CoreCold ? "wolfmed-vitals-core-cold" : "wolfmed-vitals-core-hot",
            ("kelvin", (int) MathF.Round(report.Core)));
    }

    /// <summary>
    /// M3 (plan §8): "Organs: lungs impaired (short of breath); heart impaired (irregular pulse)". Null while every
    /// organ is OK.
    /// </summary>
    public static string? OrganLine(WolfmedVitalsReport report)
    {
        if (report.Organs.Count == 0)
            return null;

        var parts = new List<string>();
        foreach (var (slot, band) in report.Organs)
        {
            var key = slot.ToLowerInvariant();
            var name = Loc.TryGetString($"wolfmed-vitals-organ-{key}", out var named) ? named : key;
            var word = Loc.GetString($"wolfmed-vitals-organ-band-{band.ToString().ToLowerInvariant()}",
                ("organ", name));
            parts.Add(Loc.TryGetString($"wolfmed-vitals-organ-{key}-{band.ToString().ToLowerInvariant()}",
                out var effect)
                ? Loc.GetString("wolfmed-vitals-organ-with-effect", ("reading", word), ("effect", effect))
                : word);
        }

        return Loc.GetString("wolfmed-vitals-organs", ("organs", string.Join("; ", parts)));
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
            WolfmedDefibVerdict.TooCold => Loc.GetString("wolfmed-vitals-verdict-toocold",
                ("kelvin", (int) MathF.Round(report.VerdictCore)),
                ("line", (int) MathF.Ceiling(report.VerdictRewarm))),
            WolfmedDefibVerdict.Unrevivable => Loc.GetString("wolfmed-vitals-verdict-unrevivable",
                ("reason", report.VerdictReason is { } reason && Loc.TryGetString(reason, out var text)
                    ? text
                    : report.VerdictReason ?? string.Empty)),
            _ => Loc.GetString($"wolfmed-vitals-verdict-{report.Verdict.ToString().ToLowerInvariant()}"),
        };
    }
}
