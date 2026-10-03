namespace Content.Server._WF.NpcCrew.Components;

/// <summary>A crewman assigned to operate a ship's gunnery console.</summary>
[RegisterComponent]
public sealed partial class WFGunnerDutyComponent : Component
{
    [DataField] public EntityUid? Console;
    [DataField] public float Range = 3000f;
    [ViewVariables] public bool AtConsole;

    /// <summary>How far off the target the guns are laid right now, by the gunner's skill.</summary>
    [ViewVariables] public System.Numerics.Vector2 AimError;
    [ViewVariables] public TimeSpan NextAimError;
}
