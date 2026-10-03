using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Settings for random encounters.</summary>
[CVarDefs]
public sealed class EncountersCVars
{
    /// <summary>Whether the scheduler starts encounters on its own. Admins can always start one.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.encounters.enabled", false, CVar.SERVERONLY);

    /// <summary>Shortest wait between scheduled encounters, in seconds.</summary>
    public static readonly CVarDef<float> IntervalMin =
        CVarDef.Create("wf.encounters.interval_min", 900f, CVar.SERVERONLY);

    /// <summary>Longest wait between scheduled encounters, in seconds.</summary>
    public static readonly CVarDef<float> IntervalMax =
        CVarDef.Create("wf.encounters.interval_max", 1800f, CVar.SERVERONLY);

    /// <summary>The scheduler starts nothing while this many encounters are unresolved.</summary>
    public static readonly CVarDef<int> MaxActive =
        CVarDef.Create("wf.encounters.max_active", 2, CVar.SERVERONLY);

    /// <summary>A resolved encounter's ships are removed once no player is within this distance.</summary>
    public static readonly CVarDef<float> CleanupRange =
        CVarDef.Create("wf.encounters.cleanup_range", 1500f, CVar.SERVERONLY);

    /// <summary>Seconds a resolved encounter's ship must be left alone before it is removed.</summary>
    public static readonly CVarDef<float> CleanupDelay =
        CVarDef.Create("wf.encounters.cleanup_delay", 120f, CVar.SERVERONLY);
}
