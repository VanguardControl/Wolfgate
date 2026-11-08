using Robust.Shared.Configuration;

namespace Content.Shared._WF.Lattice;

/// <summary>The Lattice's end: the Unstringing when the last primary anchor falls, and Tacet after it.</summary>
[CVarDefs]
public sealed class WFLatticeEndgameCVars
{
    /// <summary>Whether Kyphrus implodes some time after the Unstringing.</summary>
    public static readonly CVarDef<bool> Tacet =
        CVarDef.Create("wf.lattice.tacet", true, CVar.SERVERONLY);

    /// <summary>Soonest and latest Tacet comes after the Unstringing, in seconds.</summary>
    public static readonly CVarDef<float> TacetMin =
        CVarDef.Create("wf.lattice.tacet_min", 900f, CVar.SERVERONLY);

    public static readonly CVarDef<float> TacetMax =
        CVarDef.Create("wf.lattice.tacet_max", 1800f, CVar.SERVERONLY);

    /// <summary>After Tacet, how often working bulbs are rolled to burst, in seconds, and the chance each one does.</summary>
    public static readonly CVarDef<float> DarkInterval =
        CVarDef.Create("wf.lattice.tacet_dark_interval", 30f, CVar.SERVERONLY);

    public static readonly CVarDef<float> DarkChance =
        CVarDef.Create("wf.lattice.tacet_dark_chance", 0.25f, CVar.SERVERONLY);
}
