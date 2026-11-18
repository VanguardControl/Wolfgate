using Robust.Shared.Configuration;

namespace Content.Shared._WG.FarPrediction;

/// <summary>Switches for the FarPrediction module.</summary>
[CVarDefs]
public sealed class WGFarPredictionCVars
{
    /// <summary>
    /// Whether clients steady their own predicted movement on grids far from the map origin. Set on the server and
    /// sent to every client, so it can be switched off for everyone without a client update.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.prediction.far_correction", true, CVar.SERVER | CVar.REPLICATED);
}
