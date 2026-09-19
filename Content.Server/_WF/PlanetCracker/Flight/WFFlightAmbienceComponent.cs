namespace Content.Server._WF.PlanetCracker.Flight;

/// <summary>
/// The air a hull below orbit is flying through, as two looping streams played to everyone aboard it. Server-only and
/// added by <see cref="WFFlightAmbienceSystem"/> alone; it exists exactly as long as the sound does.
/// </summary>
[RegisterComponent]
public sealed partial class WFFlightAmbienceComponent : Component
{
    /// <summary>The wind, playing for as long as the hull is anywhere below orbit and off the ground.</summary>
    [DataField]
    public EntityUid? Wind;

    /// <summary>The airframe rumble, which only exists while the hull is falling.</summary>
    [DataField]
    public EntityUid? Rumble;

    /// <summary>Pitch the wind was last issued at; a live stream's pitch cannot be changed, only re-cut.</summary>
    [DataField]
    public float WindPitch;

    /// <summary>When both loops are re-cut regardless, so somebody who boarded mid-flight is inside their filter.</summary>
    [DataField]
    public TimeSpan NextReissue;
}
