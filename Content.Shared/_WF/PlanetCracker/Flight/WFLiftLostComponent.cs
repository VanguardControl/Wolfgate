using System.Numerics;
using Content.Shared._WF.ShipPa;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Flight;

/// <summary>
/// A hull below orbit whose landing thrusters no longer hold it up: it is coming down whatever the pilot does. Drives
/// the PA callouts and the glide, and is dropped again the moment the lift comes back or the hull is on the ground.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFLiftLostComponent : Component
{
    /// <summary>Lift ratio as the last sweep measured it; 1 is level flight, 0 is a brick.</summary>
    [DataField, AutoNetworkedField]
    public float Ratio;

    /// <summary>Planar velocity the hull carried into the fall, so a glide keeps its heading even through a transit hop.</summary>
    [DataField]
    public Vector2 Glide;

    /// <summary>Depth of the layer under the hull when the alarms last looked; a drop in it is one layer of glide.</summary>
    [DataField]
    public int LastDepth = int.MaxValue;

    /// <summary>The situation code the ship was on before the alarms took it over, restored when the state ends.</summary>
    [DataField]
    public ProtoId<ShipAlertCodePrototype>? PriorCode;

    /// <summary>How far the callouts have got. They only ever advance, so a bounce between gaps cannot re-announce.</summary>
    [DataField]
    public WFFlightAlarmStage Stage = WFFlightAlarmStage.None;

    /// <summary>When the pull-up callout may sound again; it is the one that repeats.</summary>
    [DataField]
    public TimeSpan NextPullUp;

    /// <summary>The caution alarm, looped to everyone aboard for the whole emergency. Server-side.</summary>
    [DataField]
    public EntityUid? Alarm;

    /// <summary>When the alarm loop is re-cut, so somebody who boarded mid-fall is inside its filter too.</summary>
    [DataField]
    public TimeSpan NextAlarmLoop;
}

/// <summary>The PA callouts of a descent, in the order a falling hull passes through them.</summary>
public enum WFFlightAlarmStage : byte
{
    None = 0,

    /// <summary>Lift is gone; the caution chime, before any callout.</summary>
    LiftLost = 1,

    /// <summary>Out of orbit and into the top air layer.</summary>
    DontSink = 2,

    /// <summary>Top air layer into the middle of the stack.</summary>
    SinkRate = 3,

    /// <summary>The middle of the stack into the bottom air layer.</summary>
    Terrain = 4,

    /// <summary>The bottom gap, first half.</summary>
    TooLowTerrain = 5,

    /// <summary>The bottom gap, last seconds. Repeats until the hull is down.</summary>
    PullUp = 6,
}
