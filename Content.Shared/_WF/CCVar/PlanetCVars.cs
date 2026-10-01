using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for Wolfgate planets.
/// </summary>
[CVarDefs]
public sealed class PlanetCVars
{
    /// <summary>
    /// Master switch for Wolfgate planet networks: orbit layers, surfaces and the orbit FTL jumps.
    /// </summary>
    public static readonly CVarDef<bool> PlanetNetworks =
        CVarDef.Create("wf.planet_networks", false, CVar.SERVERONLY);

    /// <summary>
    /// Whether planet ground gets an atmosphere, so rooms built on it hold their own air. Bare ground keeps the
    /// planet's air either way. Applies to networks built after it is set.
    /// </summary>
    public static readonly CVarDef<bool> TerrainAtmosphere =
        CVarDef.Create("wf.planet_terrain_atmos", true, CVar.SERVERONLY);
}
