using Content.Shared._WF.NpcCrew;
using Robust.Shared.Map;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>
/// A crewman that can fly the ship: the helm it takes and the orders it flies once there. Worked by
/// <c>WFPilotDutySystem</c> when the crewman's duty is Pilot.
/// </summary>
[RegisterComponent]
public sealed partial class WFPilotDutyComponent : Component
{
    /// <summary>Assigned helm. Null takes the nearest powered shuttle console on the crewman's grid.</summary>
    [DataField]
    public EntityUid? Console;

    [DataField]
    public WFPilotOrder Orders = WFPilotOrder.Hold;

    /// <summary>GoTo: the points flown in turn.</summary>
    [DataField]
    public List<EntityCoordinates> Waypoints = new();

    /// <summary>GoTo: the waypoint being flown to.</summary>
    [DataField]
    public int WaypointIndex;

    /// <summary>Loiter: the point circled.</summary>
    [DataField]
    public EntityCoordinates? LoiterCenter;

    [DataField]
    public float LoiterRadius = 60f;

    /// <summary>Follow: the grid kept in range.</summary>
    [DataField]
    public EntityUid? FollowTarget;

    [DataField]
    public float FollowRange = 150f;

    /// <summary>Speed the ship may still have when it counts as arrived, in m/s.</summary>
    [DataField]
    public float CruiseSpeed = 12f;

    /// <summary>GoTo: how close to a waypoint counts as reaching it.</summary>
    [DataField]
    public float ArrivalRange = 60f;

    /// <summary>Whether the crewman holds the helm and is steering.</summary>
    [ViewVariables]
    public bool AtHelm;

    /// <summary>Whether the last GoTo was flown to its end.</summary>
    [ViewVariables]
    public bool OrdersCompleted;
}
