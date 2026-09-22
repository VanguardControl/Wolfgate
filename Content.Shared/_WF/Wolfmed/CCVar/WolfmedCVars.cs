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
        CVarDef.Create("wolfmed.emp_part_damage", 40f, CVar.SERVERONLY);

    /// <summary>
    /// Most one EMP deals to one body in total. The parts share it, so a full chassis takes this much and a lone
    /// cybernetic limb takes wolfmed.emp_part_damage. Zero removes the ceiling.
    /// </summary>
    public static readonly CVarDef<float> EmpBodyDamage =
        CVarDef.Create("wolfmed.emp_body_damage", 120f, CVar.SERVERONLY);

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

    /// <summary>Whether bullets can stray from the aimed body part, by gun spread and range.</summary>
    public static readonly CVarDef<bool> AimScatter =
        CVarDef.Create("wolfmed.aim_scatter", true, CVar.SERVERONLY);

    /// <summary>Chance to hit the aimed part with a tight gun or at point blank.</summary>
    public static readonly CVarDef<float> AimBestChance =
        CVarDef.Create("wolfmed.aim_best_chance", 0.75f, CVar.SERVERONLY);

    /// <summary>Floor for the chance to hit the aimed part, however wide the spread or long the range.</summary>
    public static readonly CVarDef<float> AimWorstChance =
        CVarDef.Create("wolfmed.aim_worst_chance", 0.1f, CVar.SERVERONLY);

    /// <summary>Whether a blast can tear limbs off outright.</summary>
    public static readonly CVarDef<bool> BlastDismember =
        CVarDef.Create("wolfmed.blast_dismember", true, CVar.SERVERONLY);

    /// <summary>Blast damage to one body below which no limb is ever torn off.</summary>
    public static readonly CVarDef<float> BlastDismemberMin =
        CVarDef.Create("wolfmed.blast_dismember_min", 30f, CVar.SERVERONLY);

    /// <summary>Blast damage at which the per-limb chance peaks. Every multiple of it also rolls one more limb.</summary>
    public static readonly CVarDef<float> BlastDismemberFull =
        CVarDef.Create("wolfmed.blast_dismember_full", 150f, CVar.SERVERONLY);

    /// <summary>Peak chance for a rolled limb to come off.</summary>
    public static readonly CVarDef<float> BlastDismemberChance =
        CVarDef.Create("wolfmed.blast_dismember_chance", 0.8f, CVar.SERVERONLY);

    /// <summary>Whether a blast may take the head as well.</summary>
    public static readonly CVarDef<bool> BlastDismemberHead =
        CVarDef.Create("wolfmed.blast_dismember_head", false, CVar.SERVERONLY);

    /// <summary>Client: the fading-out view and camera sway when close to death.</summary>
    public static readonly CVarDef<bool> DyingEffects =
        CVarDef.Create("wolfmed.dying_effects", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether consciousness owns Alive/Critical on wound hosts (CONSC). False hands mob state back to the
    /// damage thresholds, which is the pre-CONSC behaviour.
    /// </summary>
    public static readonly CVarDef<bool> Consciousness =
        CVarDef.Create("wolfmed.consciousness", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Effective pain, as a share of the soft pain cap, at which a body goes Downed.</summary>
    public static readonly CVarDef<float> ConsciousnessPainDown =
        CVarDef.Create("wolfmed.consc_pain_down", 0.95f, CVar.SERVERONLY);

    /// <summary>
    /// Pain before the soft clamp, as a share of the soft pain cap, at which a body goes unconscious. Onyx's
    /// PainComponent has no hard cap, so this is measured on the sum of the parts.
    /// </summary>
    public static readonly CVarDef<float> ConsciousnessPainOut =
        CVarDef.Create("wolfmed.consc_pain_out", 1.4f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction at or below which a body goes Downed.</summary>
    public static readonly CVarDef<float> ConsciousnessBloodDown =
        CVarDef.Create("wolfmed.consc_blood_down", 0.5f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction at or below which a body goes unconscious.</summary>
    public static readonly CVarDef<float> ConsciousnessBloodOut =
        CVarDef.Create("wolfmed.consc_blood_out", 0.35f, CVar.SERVERONLY);

    /// <summary>
    /// How far an input has to fall back below the value that caused a state before that state is left, as a
    /// share of the entry value. Keeps a bleeding body from flickering in and out of Downed.
    /// </summary>
    public static readonly CVarDef<float> ConsciousnessHysteresis =
        CVarDef.Create("wolfmed.consc_hysteresis", 0.1f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction at or under which the heart stops (BRAIN).</summary>
    public static readonly CVarDef<float> ArrestBlood =
        CVarDef.Create("wolfmed.arrest_blood", 0.30f, CVar.SERVERONLY);

    /// <summary>Brain oxygenation at or under which the heart stops.</summary>
    public static readonly CVarDef<float> ArrestOxygenation =
        CVarDef.Create("wolfmed.arrest_oxygenation", 0.15f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction at or under which a pain shock stops the heart instead of stunning.</summary>
    public static readonly CVarDef<float> ArrestShockBlood =
        CVarDef.Create("wolfmed.arrest_shock_blood", 0.5f, CVar.SERVERONLY);

    /// <summary>Sepsis progress at or past which the heart can stop on its own.</summary>
    public static readonly CVarDef<float> ArrestSepsis =
        CVarDef.Create("wolfmed.arrest_sepsis", 80f, CVar.SERVERONLY);

    /// <summary>Chance per second that late sepsis stops the heart.</summary>
    public static readonly CVarDef<float> ArrestSepsisChance =
        CVarDef.Create("wolfmed.arrest_sepsis_chance", 0.01f, CVar.SERVERONLY);

    /// <summary>Shock damage in one electrocution at or past which the heart stops. Zero turns that off.</summary>
    public static readonly CVarDef<float> ArrestShockDamage =
        CVarDef.Create("wolfmed.arrest_shock_damage", 60f, CVar.SERVERONLY);

    /// <summary>Seconds of a stopped heart that drain brain oxygenation from full to nothing.</summary>
    public static readonly CVarDef<float> BrainArrestSeconds =
        CVarDef.Create("wolfmed.brain_arrest_seconds", 120f, CVar.SERVERONLY);

    /// <summary>Seconds of not breathing at all that drain brain oxygenation from full to nothing.</summary>
    public static readonly CVarDef<float> BrainAirlossSeconds =
        CVarDef.Create("wolfmed.brain_airloss_seconds", 180f, CVar.SERVERONLY);

    /// <summary>Seconds at <see cref="BrainBloodFull"/> blood that drain oxygenation from full to nothing.</summary>
    public static readonly CVarDef<float> BrainBloodSeconds =
        CVarDef.Create("wolfmed.brain_blood_seconds", 300f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction under which perfusion starts costing the brain oxygen.</summary>
    public static readonly CVarDef<float> BrainBloodStart =
        CVarDef.Create("wolfmed.brain_blood_start", 0.5f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction at which the blood drain is at its full rate.</summary>
    public static readonly CVarDef<float> BrainBloodFull =
        CVarDef.Create("wolfmed.brain_blood_full", 0.3f, CVar.SERVERONLY);

    /// <summary>Seconds of late sepsis that drain brain oxygenation from full to nothing.</summary>
    public static readonly CVarDef<float> BrainSepsisSeconds =
        CVarDef.Create("wolfmed.brain_sepsis_seconds", 600f, CVar.SERVERONLY);

    /// <summary>Refill rate while perfused and breathing, as a share of the arrest drain rate.</summary>
    public static readonly CVarDef<float> BrainRefillFactor =
        CVarDef.Create("wolfmed.brain_refill_factor", 0.5f, CVar.SERVERONLY);

    /// <summary>
    /// Multiplier on the cold protection a brain gets from a low body temperature. The curve itself is data
    /// (WolfmedBrainComponent.ColdSteps); 1 uses it as authored and higher values weaken it.
    /// </summary>
    public static readonly CVarDef<float> BrainColdFactor =
        CVarDef.Create("wolfmed.brain_cold_factor", 1f, CVar.SERVERONLY);

    /// <summary>Drain multiplier while somebody is doing CPR.</summary>
    public static readonly CVarDef<float> BrainCprFactor =
        CVarDef.Create("wolfmed.brain_cpr_factor", 0.25f, CVar.SERVERONLY);

    /// <summary>Drain multiplier while an epinephrine-class stimulant is metabolising.</summary>
    public static readonly CVarDef<float> BrainStimulantFactor =
        CVarDef.Create("wolfmed.brain_stimulant_factor", 0.6f, CVar.SERVERONLY);

    /// <summary>Oxygenation under which the brain organ starts taking irreversible damage.</summary>
    public static readonly CVarDef<float> BrainDamageOxygenation =
        CVarDef.Create("wolfmed.brain_damage_oxygenation", 0.4f, CVar.SERVERONLY);

    /// <summary>Brain organ health lost per second at zero oxygenation, falling linearly to the threshold.</summary>
    public static readonly CVarDef<float> BrainDamageRate =
        CVarDef.Create("wolfmed.brain_damage_rate", 0.1f, CVar.SERVERONLY);

    /// <summary>Oxygenation under which hypoxia starts pushing consciousness down.</summary>
    public static readonly CVarDef<float> BrainPressureStart =
        CVarDef.Create("wolfmed.brain_pressure_start", 0.75f, CVar.SERVERONLY);

    /// <summary>Oxygenation at which hypoxia alone is enough to put a body out.</summary>
    public static readonly CVarDef<float> BrainPressureOut =
        CVarDef.Create("wolfmed.brain_pressure_out", 0.45f, CVar.SERVERONLY);

    /// <summary>Minutes a repaired brain carries its trauma, with the concussion effects.</summary>
    public static readonly CVarDef<float> BrainTraumaMinutes =
        CVarDef.Create("wolfmed.brain_trauma_minutes", 30f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction under which a defibrillator can never restart the heart.</summary>
    public static readonly CVarDef<float> DefibBlood =
        CVarDef.Create("wolfmed.defib_blood", 0.40f, CVar.SERVERONLY);

    /// <summary>Best chance a defibrillator has, at full brain oxygenation.</summary>
    public static readonly CVarDef<float> DefibChance =
        CVarDef.Create("wolfmed.defib_chance", 0.85f, CVar.SERVERONLY);

    /// <summary>What the defibrillator's chance is multiplied by at zero brain oxygenation.</summary>
    public static readonly CVarDef<float> DefibOxygenationFloor =
        CVarDef.Create("wolfmed.defib_oxygenation_floor", 0.15f, CVar.SERVERONLY);

    /// <summary>The autodoc's vital alarm: the monitor beep over a failing occupant. False silences it.</summary>
    public static readonly CVarDef<bool> AutodocAlarm =
        CVarDef.Create("wolfmed.autodoc_alarm", true, CVar.SERVERONLY);
}
