using Content.Shared._WF.NpcCrew;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew;

/// <summary>Raised on the crewman and broadcast when a crewman goes critical or dies.</summary>
[ByRefEvent]
public readonly record struct WFCrewMemberDownEvent(
    EntityUid Mob,
    string Group,
    ProtoId<WFCrewRolePrototype>? Role,
    bool Dead);

/// <summary>Raised on a crew pilot and broadcast when its orders change.</summary>
[ByRefEvent]
public readonly record struct WFPilotOrdersChangedEvent(EntityUid Mob, WFPilotOrder Orders);

/// <summary>Raised on a crew pilot and broadcast when it reaches its last waypoint and switches to Hold.</summary>
[ByRefEvent]
public readonly record struct WFPilotOrdersCompletedEvent(EntityUid Mob);

/// <summary>Raised on a crew pilot and broadcast when it takes a helm.</summary>
[ByRefEvent]
public readonly record struct WFHelmTakenEvent(EntityUid Mob, EntityUid Console, EntityUid Grid);

/// <summary>Raised on a crew pilot and broadcast when it lets go of the helm, for whatever reason.</summary>
[ByRefEvent]
public readonly record struct WFHelmReleasedEvent(EntityUid Mob);
