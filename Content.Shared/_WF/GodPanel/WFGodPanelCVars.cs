using Robust.Shared.Configuration;

namespace Content.Shared._WF.GodPanel;

/// <summary>
/// Settings for the gamemaster's God Panel.
/// </summary>
[CVarDefs]
public sealed class WFGodPanelCVars
{
    /// <summary>
    /// Seconds between the state updates sent to each open panel.
    /// </summary>
    public static readonly CVarDef<float> UpdateInterval =
        CVarDef.Create("wf.godpanel.update_interval", 1.0f, CVar.SERVERONLY);

    /// <summary>
    /// The most camera feeds one gamemaster may hold open.
    /// </summary>
    public static readonly CVarDef<int> MaxFeeds =
        CVarDef.Create("wf.godpanel.max_feeds", 6, CVar.SERVERONLY);
}
