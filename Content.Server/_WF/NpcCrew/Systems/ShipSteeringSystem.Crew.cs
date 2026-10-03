using Content.Server._WF.NpcCrew.Components;

namespace Content.Server._Mono.NPC.HTN;

public sealed partial class ShipSteeringSystem
{
    /// <summary>Crew must avoid the destination hull too; final docking already disables avoidance.</summary>
    private bool CrewAvoidsTarget(EntityUid? pilot) => pilot is { } uid && HasComp<WFPilotDutyComponent>(uid);

    /// <summary>The fixed heading a crew pilot is holding, if any.</summary>
    private Angle? CrewHeading(EntityUid pilot) => TryComp<WFPilotDutyComponent>(pilot, out var duty) ? duty.HeadingOverride : null;
}
