using Robust.Shared.Configuration;

namespace Content.Shared._WF.AppearanceStudio;

/// <summary>Server settings for the Appearance Studio. Replicated so the studio shows the limits it is held to.</summary>
[CVarDefs]
public sealed class StudioCVars
{
    /// <summary>Whether characters get their studio tweaks and the creator shows the studio.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.appearance_studio.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Furthest a part may be moved on either axis, in pixels. Capped at <see cref="StudioRules.OffsetCap"/>.</summary>
    public static readonly CVarDef<int> MaxOffset =
        CVarDef.Create("wf.appearance_studio.max_offset", 16, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Smallest scale a part may be drawn at.</summary>
    public static readonly CVarDef<float> MinScale =
        CVarDef.Create("wf.appearance_studio.min_scale", 0.5f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Largest scale a part may be drawn at.</summary>
    public static readonly CVarDef<float> MaxScale =
        CVarDef.Create("wf.appearance_studio.max_scale", 2f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Whether players may paint and wear displacement maps (warps) for worn and held items.</summary>
    public static readonly CVarDef<bool> Warps =
        CVarDef.Create("wf.appearance_studio.warps", true, CVar.SERVER | CVar.REPLICATED);
}
