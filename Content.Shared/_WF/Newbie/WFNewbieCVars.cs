using Robust.Shared.Configuration;

namespace Content.Shared._WF.Newbie;

/// <summary>
/// Tuning for the newbie tag.
/// </summary>
[CVarDefs]
public sealed class WFNewbieCVars
{
    /// <summary>
    /// Total playtime in hours under which a player is tagged as new. 0 turns the tag off for everyone.
    /// </summary>
    public static readonly CVarDef<float> Hours =
        CVarDef.Create("wf.newbie.hours", 15f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Whether this player shows their own newbie tag. Turning it off also drops the newbie rules' protection.
    /// </summary>
    public static readonly CVarDef<bool> ShowTag =
        CVarDef.Create("wf.newbie.show_tag", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);
}
