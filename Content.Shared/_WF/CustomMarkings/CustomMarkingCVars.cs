using Robust.Shared.Configuration;

namespace Content.Shared._WF.CustomMarkings;

/// <summary>Server settings for custom markings.</summary>
[CVarDefs]
public sealed class CustomMarkingCVars
{
    /// <summary>
    /// Whether characters get their custom markings and players can save new ones. Replicated so the creator can
    /// hide its button.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.custom_markings.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Most custom markings one character can wear. Replicated so the creator can show it.</summary>
    public static readonly CVarDef<int> MaxWorn =
        CVarDef.Create("wf.custom_markings.max_worn", 4, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Most markings a player's library holds. Replicated so the creator can show it.</summary>
    public static readonly CVarDef<int> LibraryLimit =
        CVarDef.Create("wf.custom_markings.library_limit", 24, CVar.SERVER | CVar.REPLICATED);
}
