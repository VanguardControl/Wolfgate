using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for Wolfgate planets.
/// </summary>
[CVarDefs]
public sealed class PlanetCrackerCVars
{
    /// <summary>
    /// Master switch for Wolfgate planet networks: orbit layers, surfaces and the orbit FTL jumps.
    /// </summary>
    public static readonly CVarDef<bool> PlanetNetworks =
        CVarDef.Create("wf.planet_networks", false, CVar.SERVERONLY);
}
