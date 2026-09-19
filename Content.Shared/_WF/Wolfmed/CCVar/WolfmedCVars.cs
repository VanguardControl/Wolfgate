using Robust.Shared.Configuration;

namespace Content.Shared._WF.Wolfmed.CCVar;

/// <summary>
/// Wolfmed settings.
/// </summary>
[CVarDefs]
public sealed class WolfmedCVars
{
    /// <summary>
    /// Plays a looping heartbeat while the local player's own body is in critical condition.
    /// </summary>
    public static readonly CVarDef<bool> CritHeartbeat =
        CVarDef.Create("wolfmed.crit_heartbeat", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether open wounds become infected at all (W5). False leaves existing infections in place and
    /// stops them progressing, so turning it off mid-round is safe.
    /// </summary>
    public static readonly CVarDef<bool> InfectionEnabled =
        CVarDef.Create("wolfmed.infection_enabled", true, CVar.SERVERONLY);

    /// <summary>Multiplier on every infection timer. The knob for tuning, and for accelerating a test.</summary>
    public static readonly CVarDef<float> InfectionRate =
        CVarDef.Create("wolfmed.infection_rate", 1f, CVar.SERVERONLY);

    /// <summary>Whether a spreading infection can go systemic. False caps the model at the fever stage.</summary>
    public static readonly CVarDef<bool> SepsisEnabled =
        CVarDef.Create("wolfmed.sepsis_enabled", true, CVar.SERVERONLY);

    /// <summary>Whether tourniquets, deep burns and late reattachments can kill a limb.</summary>
    public static readonly CVarDef<bool> NecrosisEnabled =
        CVarDef.Create("wolfmed.necrosis_enabled", true, CVar.SERVERONLY);

    /// <summary>Multiplier on every necrosis timer, including the tourniquet clock.</summary>
    public static readonly CVarDef<float> NecrosisRate =
        CVarDef.Create("wolfmed.necrosis_rate", 1f, CVar.SERVERONLY);
}
