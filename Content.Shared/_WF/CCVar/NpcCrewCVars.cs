using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Settings for NPC crew.</summary>
[CVarDefs]
public sealed class NpcCrewCVars
{
    /// <summary>
    /// Whether a crew pilot that runs out of docking attempts jumps its ship straight onto the port instead of holding
    /// off it.
    /// </summary>
    public static readonly CVarDef<bool> DockFtlFallback =
        CVarDef.Create("wf.crew.dock_ftl_fallback", false, CVar.SERVERONLY);
}
