using Robust.Shared.Configuration;

namespace Content.Server._WG.ColdStorage;

/// <summary>Server cvars for <see cref="WGPlanetStoreSystem"/>.</summary>
[CVarDefs]
public sealed class WGPlanetStoreCVars
{
    /// <summary>Master switch for unloading planets nobody is on; turning it off brings every unloaded world back.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.planetstore.enabled", false, CVar.SERVERONLY);

    /// <summary>Seconds a world must have had nobody on it, in orbit or on the way before it is unloaded.</summary>
    public static readonly CVarDef<float> Idle =
        CVarDef.Create("wg.planetstore.idle", 600f, CVar.SERVERONLY);

    /// <summary>Milliseconds a tick spent saving, deleting or loading a world.</summary>
    public static readonly CVarDef<float> BudgetMs =
        CVarDef.Create("wg.planetstore.budget_ms", 4f, CVar.SERVERONLY);

    /// <summary>Milliseconds a tick spent loading a world once a player is on a layer of it below orbit.</summary>
    public static readonly CVarDef<float> UrgentMs =
        CVarDef.Create("wg.planetstore.urgent_ms", 15f, CVar.SERVERONLY);
}
