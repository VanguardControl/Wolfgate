using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for Wolfgate caverns.
/// </summary>
[CVarDefs]
public sealed class CavernCVars
{
    /// <summary>
    /// Whether planet networks built from now on get a cavern below their ground.
    /// </summary>
    public static readonly CVarDef<bool> Caverns =
        CVarDef.Create("wf.caverns", false, CVar.SERVERONLY);
}
