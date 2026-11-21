using Robust.Shared.Configuration;

namespace Content.Server._WG.ColdStorage;

/// <summary>Server cvars for <see cref="WGColdStoreSystem"/>.</summary>
[CVarDefs]
public sealed class WGColdStoreCVars
{
    /// <summary>Master switch for storing wrecks; turning it off restores every stored grid, one a tick.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.coldstore.enabled", false, CVar.SERVERONLY);

    /// <summary>Seconds a grid must have been in stasis before it is stored; down to a quarter at full entity pressure.</summary>
    public static readonly CVarDef<float> Idle =
        CVarDef.Create("wg.coldstore.idle", 900f, CVar.SERVERONLY);

    /// <summary>Fewest entities a grid needs to be worth a stand-in.</summary>
    public static readonly CVarDef<int> MinEntities =
        CVarDef.Create("wg.coldstore.min_entities", 150, CVar.SERVERONLY);

    /// <summary>Metres from a stored grid's bounds within which a viewer restores it; half of it for another grid.</summary>
    public static readonly CVarDef<float> RestoreRange =
        CVarDef.Create("wg.coldstore.restore_range", 600f, CVar.SERVERONLY);

    /// <summary>
    /// Milliseconds a tick a restore's load may take, so a big hull comes back over many ticks; 0 loads it in one call.
    /// Ignored once what woke the grid is within <see cref="WGColdStoreSystem.UrgentRange"/>.
    /// </summary>
    public static readonly CVarDef<float> RestoreBudgetMs =
        CVarDef.Create("wg.coldstore.restore_budget_ms", 4f, CVar.SERVERONLY);

    /// <summary>Milliseconds a restore's slowest tick should take at most; only compared and logged.</summary>
    public static readonly CVarDef<float> MaxRestoreMs =
        CVarDef.Create("wg.coldstore.max_restore_ms", 50f, CVar.SERVERONLY);
}
