using Robust.Shared.Configuration;

namespace Content.Shared._WG.LogInvestigator;

/// <summary>Cvars for the log investigator and the data it reads.</summary>
[CVarDefs]
public sealed class LogInvestigatorCVars
{
    /// <summary>Admin logs open in the investigator; false brings back the upstream log window.</summary>
    public static readonly CVarDef<bool> Investigator =
        CVarDef.Create("wg.admin_logs.investigator", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Log types whose entities get their position written into the log's JSON, comma separated. Empty turns it off.
    /// </summary>
    public static readonly CVarDef<string> PositionTypes =
        CVarDef.Create("wg.admin_logs.position_types",
            "Damaged,MeleeHit,BulletHit,HitScanHit,ThrowHit,ExplosionHit,Explosion,ShipgunFired,Chat,Mind,Gib,InteractUsing,Stamina",
            CVar.SERVERONLY);

    /// <summary>Seconds between samples of where every player is; 0 turns sampling off.</summary>
    public static readonly CVarDef<float> PositionInterval =
        CVarDef.Create("wg.admin_logs.position_interval", 10f, CVar.SERVERONLY);

    /// <summary>Logs per page the investigator sends.</summary>
    public static readonly CVarDef<int> PageSize =
        CVarDef.Create("wg.admin_logs.page_size", 500, CVar.SERVERONLY);
}
