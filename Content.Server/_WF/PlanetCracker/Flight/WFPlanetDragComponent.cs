namespace Content.Server._WF.PlanetCracker.Flight;

/// <summary>A grid currently flying a planet layer under that layer's drag, holding its own damping until it leaves.</summary>
/// <remarks>
/// Server-only and not networked: physics damping is networked on the body itself, so a client already sees the
/// change. UnsavedComponent because it is a live loan of a field, not a property of the hull - a map saved mid-flight
/// must not come back with the layer's damping recorded as the grid's own.
/// </remarks>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetDragComponent : Component
{
    /// <summary>The grid's own linear damping, taken the moment it arrived on its first planet layer.</summary>
    [DataField]
    public float OriginalDamping;
}
