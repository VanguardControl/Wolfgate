using Robust.Shared.Configuration;

namespace Content.Shared._WF.LockOn;

/// <summary>Developer switches for the lock indicator.</summary>
[CVarDefs]
public sealed class WFLockOnCVars
{
    /// <summary>Draws the solved flight time, speeds and lead distance beside each lead cross.</summary>
    public static readonly CVarDef<bool> DebugLead =
        CVarDef.Create("wf.lockon.debug_lead", false, CVar.CLIENTONLY | CVar.ARCHIVE);
}
