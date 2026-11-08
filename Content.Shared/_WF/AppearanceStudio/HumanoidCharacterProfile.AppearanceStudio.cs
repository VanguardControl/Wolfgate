using System.Linq;
using Content.Shared._WF.AppearanceStudio;
using Robust.Shared.Configuration;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>The parts of this character placed in the Appearance Studio.</summary>
    [DataField]
    public List<StudioTweak> StudioTweaks { get; set; } = new();

    public HumanoidCharacterProfile WithStudioTweaks(IEnumerable<StudioTweak> tweaks)
    {
        return new(this) { StudioTweaks = StudioRules.Copy(tweaks) };
    }

    private bool StudioTweaksEqual(HumanoidCharacterProfile other)
    {
        return StudioRules.SameList(StudioTweaks, other.StudioTweaks);
    }

    private void EnsureStudioTweaksValid(IConfigurationManager cfg)
    {
        // Crafted network data can deliver null.
        StudioTweaks = StudioRules.Clean(StudioTweaks, StudioRules.GetLimits(cfg));
    }
}
