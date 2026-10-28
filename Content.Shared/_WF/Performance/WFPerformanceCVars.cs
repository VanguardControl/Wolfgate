using Robust.Shared.Configuration;

namespace Content.Shared._WF.Performance;

/// <summary>Tuning for the Performance module.</summary>
[CVarDefs]
public sealed class WFPerformanceCVars
{
    /// <summary>
    /// Seconds a space worldgen chunk stays loaded after nothing needs it. Stops ships moving about a station from
    /// unloading and respawning the debris around it every time they cross a chunk edge.
    /// </summary>
    public static readonly CVarDef<float> ChunkUnloadGrace =
        CVarDef.Create("wf.worldgen.unload_grace", 60f, CVar.SERVERONLY);
}
