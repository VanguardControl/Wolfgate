using Content.Server._WF.NpcCrew.Systems;
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

    /// <summary>Dock: the grid docked with.</summary>
    [DataField]
    public EntityUid? DockTarget;

    /// <summary>
    /// Dock: how far out from the target dock the approach ends and the creep starts. Undock: how far to back off.
    /// </summary>
    [DataField]
    public float DockStandoff = 40f;

    /// <summary>Dock: speed the ship may still have when it reaches the standoff, in m/s.</summary>
    [DataField]
    public float DockApproachSpeed = 3f;

    /// <summary>Dock: speed the ship may still have when it reaches the dock, in m/s.</summary>
    [DataField]
    public float DockCreepSpeed = 1.5f;

    /// <summary>Dock: seconds of creeping without the docks lining up before the attempt is abandoned.</summary>
    [DataField]
    public float DockCreepTimeout = 60f;

    /// <summary>Dock: hand-flown attempts before giving up. Zero goes straight to the FTL fallback, or holds.</summary>
    [DataField]
    public int DockMaxAttempts = 3;

    /// <summary>Dock: the phase being flown.</summary>
    [ViewVariables]
    public WFDockPhase DockPhase;

    /// <summary>Dock: attempts failed so far.</summary>
    [ViewVariables]
    public int DockAttempts;

    /// <summary>Dock: seconds spent in the current phase.</summary>
    [ViewVariables]
    public float DockPhaseTime;

    /// <summary>Dock: the chosen dock pair and the poses flown to.</summary>
    [ViewVariables]
    public WFDockPlan? DockPlan;

    /// <summary>Dock: the grid the ship hit during the creep since the last update.</summary>
    [ViewVariables]
    public EntityUid? DockCollision;

    /// <summary>Whether the crewman holds the helm and is steering.</summary>
    [ViewVariables]
    public bool AtHelm;

    /// <summary>Whether the last GoTo, Dock or Undock was flown to its end.</summary>
    [ViewVariables]
    public bool OrdersCompleted;
}
