namespace Content.Server._WF.NpcCrew.Components;

/// <summary>A crewman assigned to operate a ship's gunnery console.</summary>
[RegisterComponent]
public sealed partial class WFGunnerDutyComponent : Component
{
    [DataField] public EntityUid? Console;
    [DataField] public float Range = 3000f;
    [ViewVariables] public bool AtConsole;
}
