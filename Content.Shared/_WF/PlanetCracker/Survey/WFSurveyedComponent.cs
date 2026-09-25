using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// Veins one player has revealed for the round; kept on the player so reveals do not leak to other clients.
/// </summary>
// The client reveals veins from AfterAutoHandleStateEvent, so AutoGenerateComponentState(true) is required.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause, UnsavedComponent]
public sealed partial class WFSurveyedComponent : Component
{
    /// <summary>Every deep vein this player has pulsed, by net entity.</summary>
    [DataField, AutoNetworkedField]
    public HashSet<NetEntity> Revealed = new();

    /// <summary>Where the last pulse went off, for the overlay's expanding fade.</summary>
    [DataField, AutoNetworkedField]
    public NetCoordinates? LastPulse;

    /// <summary>Radius of the last pulse, in tiles.</summary>
    [DataField, AutoNetworkedField]
    public float LastPulseRadius;

    /// <summary>When the last pulse's overlay fade ends.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan PulseFadeEnd;

    /// <summary>How long a pulse's overlay fade lasts.</summary>
    [DataField]
    public TimeSpan PulseFade = TimeSpan.FromSeconds(3);
}
