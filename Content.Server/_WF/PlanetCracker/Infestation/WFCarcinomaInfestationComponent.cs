using Robust.Shared.Physics;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WF.PlanetCracker.Infestation;

[RegisterComponent, UnsavedComponent, AutoGenerateComponentPause]
public sealed partial class WFCarcinomaInfestationComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextGrowth;
    public readonly HashSet<EntityUid> Tendrils = new();
    public bool Held;
    public BodyType PreviousBodyType;
}

[RegisterComponent]
public sealed partial class WFCarcinomaTendrilComponent : Component
{
    public EntityUid? Hull;
    [DataField] public SoundSpecifier? DeploySound;
}
