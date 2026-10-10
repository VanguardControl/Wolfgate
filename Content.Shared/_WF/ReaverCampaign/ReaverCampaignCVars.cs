using Robust.Shared.Configuration;

namespace Content.Shared._WF.ReaverCampaign;

/// <summary>Settings for the Ashfall Reaver campaign.</summary>
[CVarDefs]
public sealed class ReaverCampaignCVars
{
    /// <summary>Whether the campaign runs. It also needs the storyteller and sector control on.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.reavers.enabled", true, CVar.SERVERONLY);
}
