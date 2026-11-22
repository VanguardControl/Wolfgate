using Robust.Shared.Configuration;

namespace Content.Shared._WG.MentorObserve;

/// <summary>Settings for mentors watching askers and joining them as critters.</summary>
[CVarDefs]
public sealed class MentorObserveCVars
{
    /// <summary>Minutes a living mentor may stay stepped out before going back to their body.</summary>
    public static readonly CVarDef<float> StepOutMinutes =
        CVarDef.Create("wg.mentor.step_out_minutes", 10f, CVar.SERVERONLY);

    /// <summary>Whether mentors may join askers as critters.</summary>
    public static readonly CVarDef<bool> Critters =
        CVarDef.Create("wg.mentor.critters", true, CVar.SERVER | CVar.REPLICATED);
}
