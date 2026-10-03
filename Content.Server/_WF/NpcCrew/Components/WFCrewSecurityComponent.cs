using Content.Shared._WF.NpcCrew;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>Configured responses to unauthorized visitors and arriving ships.</summary>
[RegisterComponent]
public sealed partial class WFCrewSecurityComponent : Component
{
    [DataField]
    public WFCrewSecurityResponse Boarding = WFCrewSecurityResponse.Hostile;

    [DataField]
    public WFCrewSecurityResponse Docking = WFCrewSecurityResponse.Hostile;
}
