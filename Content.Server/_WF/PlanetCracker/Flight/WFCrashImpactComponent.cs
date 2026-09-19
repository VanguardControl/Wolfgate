using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
namespace Content.Server._WF.PlanetCracker.Flight;

[RegisterComponent, UnsavedComponent, AutoGenerateComponentPause]
public sealed partial class WFCrashImpactComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextImpact;

    // Temporary seam indices, consumed once the engine has separated the floor grids.
    public HashSet<Vector2i>? LatticeSeam;
}
