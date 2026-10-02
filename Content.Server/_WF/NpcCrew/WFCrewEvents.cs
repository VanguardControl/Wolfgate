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
