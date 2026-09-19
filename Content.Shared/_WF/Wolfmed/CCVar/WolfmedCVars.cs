using Robust.Shared.Configuration;

namespace Content.Shared._WF.Wolfmed.CCVar;

/// <summary>
/// Wolfmed client settings.
/// </summary>
[CVarDefs]
public sealed class WolfmedCVars
{
    /// <summary>
    /// Plays a looping heartbeat while the local player's own body is in critical condition.
    /// </summary>
    public static readonly CVarDef<bool> CritHeartbeat =
        CVarDef.Create("wolfmed.crit_heartbeat", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
