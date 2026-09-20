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
    /// Shock damage an EMP deals to each machine body part it reaches (cybernetic limbs, IPC parts). Zero turns
    /// EMP damage to bodies off.
    /// </summary>
    public static readonly CVarDef<float> EmpPartDamage =
        CVarDef.Create("wolfmed.emp_part_damage", 15f, CVar.SERVERONLY);

    /// <summary>
    /// Most one EMP deals to one body in total. The parts share it, so a full chassis takes this much and a lone
    /// cybernetic limb takes wolfmed.emp_part_damage. Zero removes the ceiling.
    /// </summary>
    public static readonly CVarDef<float> EmpBodyDamage =
        CVarDef.Create("wolfmed.emp_body_damage", 45f, CVar.SERVERONLY);

    /// <summary>
    /// Multiplier on every wound's bleed rate. Applied where the rate is computed, so the analyzer, the spurts
    /// and the bloodstream all see the same slowed figure.
    /// </summary>
    public static readonly CVarDef<float> BleedRate =
        CVarDef.Create("wolfmed.bleed_rate", 0.6f, CVar.SERVERONLY);

    /// <summary>
    /// Ceiling on a wound host's total damage. Part damage past it is discarded. High enough that any one limb
    /// can still reach its amputation threshold on a dead body. Zero disables the ceiling.
    /// </summary>
    public static readonly CVarDef<float> BodyDamageCap =
        CVarDef.Create("wolfmed.body_damage_cap", 600f, CVar.SERVER | CVar.REPLICATED);

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

    /// <summary>
    /// Whether wounds and dismemberment make a noise (V1/V2). False silences them without touching any
    /// other part of the model.
    /// </summary>
    public static readonly CVarDef<bool> WoundSfx =
        CVarDef.Create("wolfmed.wound_sfx", true, CVar.SERVERONLY);

    /// <summary>Whether hits that wound throw blood mist or sparks (V4). The one knob for that spawn cost.</summary>
    public static readonly CVarDef<bool> HitDebris =
        CVarDef.Create("wolfmed.hit_debris", true, CVar.SERVERONLY);

    /// <summary>
    /// Whether a major bleed or an open stump throws blood around every few seconds (G2). False leaves the
    /// bleeding model alone and only stops the spectacle.
    /// </summary>
    public static readonly CVarDef<bool> BleedSpurts =
        CVarDef.Create("wolfmed.bleed_spurts", true, CVar.SERVERONLY);
}
