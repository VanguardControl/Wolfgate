namespace Content.Shared._WF.PlanetCracker.Flight;

/// <summary>
/// Raised directionally on every member of a liftoff transit set before built-in movement gates are checked.
/// Subscribers may veto the whole upward-thrust attempt and provide the localised reason shown to the pilot.
/// </summary>
[ByRefEvent]
public record struct WFLiftoffAttemptEvent
{
    public bool Cancelled;
    public string? Reason;
}
