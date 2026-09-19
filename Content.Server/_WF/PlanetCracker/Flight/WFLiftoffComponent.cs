namespace Content.Server._WF.PlanetCracker.Flight;

/// <summary>
/// Server-side ascent intent. Its presence feeds normal CE upward input until orbit or a cancellation gate.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFLiftoffComponent : Component
{
    /// <summary>The console that engaged liftoff.</summary>
    [DataField]
    public EntityUid Console;

    /// <summary>The pilot who engaged liftoff; losing this pilot cancels the latch.</summary>
    [DataField]
    public EntityUid Pilot;
}
