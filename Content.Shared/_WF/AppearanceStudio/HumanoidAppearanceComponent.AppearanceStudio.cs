using Content.Shared._WF.AppearanceStudio;

namespace Content.Shared.Humanoid;

public sealed partial class HumanoidAppearanceComponent
{
    /// <summary>The parts of this body placed in the Appearance Studio; the client draws them (StudioSpriteSystem).</summary>
    [DataField, AutoNetworkedField]
    public List<StudioTweak> StudioTweaks = new();
}
