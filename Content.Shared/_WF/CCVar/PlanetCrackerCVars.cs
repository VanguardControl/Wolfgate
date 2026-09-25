using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for Wolfgate planet cracking.
/// </summary>
[CVarDefs]
public sealed class PlanetCrackerCVars
{
    /// <summary>
    /// Whether an unsanctioned crack raises sector notices; false mutes both the begin and the extraction line.
    /// </summary>
    public static readonly CVarDef<bool> Announce =
        CVarDef.Create("wf.planet_cracker.announce", true, CVar.SERVERONLY);
}
