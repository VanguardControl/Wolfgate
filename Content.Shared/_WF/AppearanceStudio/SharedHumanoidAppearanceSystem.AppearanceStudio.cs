using Content.Shared._WF.AppearanceStudio;
using Content.Shared.Preferences;

namespace Content.Shared.Humanoid;

public abstract partial class SharedHumanoidAppearanceSystem
{
    /// <summary>The studio tweaks a profile puts on a body: none while the studio is off.</summary>
    protected List<StudioTweak> ProfileStudioTweaks(HumanoidCharacterProfile profile)
    {
        return _cfgManager.GetCVar(StudioCVars.Enabled)
            ? StudioRules.Clean(profile.StudioTweaks, StudioRules.GetLimits(_cfgManager))
            : new List<StudioTweak>();
    }

    /// <summary>A copy of another body's tweaks, so the two never share a list.</summary>
    protected static List<StudioTweak> CopyStudioTweaks(List<StudioTweak> tweaks)
    {
        return StudioRules.Copy(tweaks);
    }
}
