using Content.Shared._WF.NpcCrew;

namespace Content.Server._WF.NpcCrew;

/// <summary>What a crew skill level means in numbers. Veteran is the crew as tuned; the others scale from it.</summary>
public static class WFCrewSkills
{
    /// <param name="AimError">Degrees a hand weapon shot may stray to either side.</param>
    /// <param name="ShootDelay">Seconds with a target in sight before the first shot.</param>
    /// <param name="GunneryError">Metres ship guns may be laid off the target, re-rolled every few seconds.</param>
    /// <param name="Leading">How well ship guns lead a moving target, 0 to 1.</param>
    /// <param name="Handling">Multiplier on thrust and turn rate at the helm.</param>
    /// <param name="Evasion">Multiplier on how far ahead the pilot looks for collisions and incoming fire.</param>
    /// <param name="DodgesFire">Whether the pilot steers out of the way of ship weapon fire.</param>
    public readonly record struct Profile(float AimError, float ShootDelay, float GunneryError, float Leading, float Handling,
        float Evasion, bool DodgesFire);

    public static Profile Of(WFCrewSkill skill)
    {
        return skill switch
        {
            WFCrewSkill.Green => new Profile(14f, 0.9f, 30f, 0.3f, 0.7f, 0.4f, false),
            WFCrewSkill.Regular => new Profile(6f, 0.45f, 14f, 0.7f, 0.9f, 0.75f, true),
            WFCrewSkill.Elite => new Profile(0f, 0.1f, 0f, 1f, 1.1f, 1.25f, true),
            _ => new Profile(2.5f, 0.2f, 5f, 1f, 1f, 1f, true),
        };
    }
}
