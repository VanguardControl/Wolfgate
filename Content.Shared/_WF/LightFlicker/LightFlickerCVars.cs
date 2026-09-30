using Robust.Shared.Configuration;

namespace Content.Shared._WF.LightFlicker;

/// <summary>
/// Tuning for light ballast damage.
/// </summary>
[CVarDefs]
public sealed class LightFlickerCVars
{
    /// <summary>
    /// Chance, from 0 to 1, that an explosion's shockwave damages the ballast of each powered light it reaches.
    /// </summary>
    public static readonly CVarDef<float> BallastDamageChance =
        CVarDef.Create("wf.light_flicker.ballast_damage_chance", 0.5f, CVar.SERVERONLY);
}
