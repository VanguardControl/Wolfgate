using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for Wolfgate planet cracking.
/// </summary>
[CVarDefs]
public sealed class PlanetCrackerCVars
{
    /// <summary>
    /// Master switch for Wolfgate planet networks: orbit layers, surfaces and the orbit FTL jumps.
    /// </summary>
    public static readonly CVarDef<bool> PlanetNetworks =
        CVarDef.Create("wf.planet_networks", false, CVar.SERVERONLY);

    /// <summary>
    /// Whether an unsanctioned crack raises sector notices (F9 D7); false mutes both the begin and the extraction line.
    /// </summary>
    public static readonly CVarDef<bool> Announce =
        CVarDef.Create("wf.planet_cracker.announce", true, CVar.SERVERONLY);
}
