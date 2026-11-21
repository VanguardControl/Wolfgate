using Robust.Shared.Configuration;

namespace Content.Server._WG.ColdStorage;

/// <summary>Server cvars for <see cref="WGEntityPressureSystem"/>.</summary>
[CVarDefs]
public sealed class WGPressureCVars
{
    /// <summary>Master switch for the pressure governor; off, pressure is always 0 and nothing tightens.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.pressure.enabled", false, CVar.SERVERONLY);

    /// <summary>Entity count at or below which there is no pressure.</summary>
    public static readonly CVarDef<int> Low =
        CVarDef.Create("wg.pressure.low", 500000, CVar.SERVERONLY);

    /// <summary>Entity count at or above which pressure is full.</summary>
    public static readonly CVarDef<int> High =
        CVarDef.Create("wg.pressure.high", 800000, CVar.SERVERONLY);
}
