namespace Content.Server._WF.NpcCrew.Components;

/// <summary>Lets a captain suspend its crew's course during an alert.</summary>
[RegisterComponent]
public sealed partial class WFCaptainComponent : Component
{
    /// <summary>Stop the ship during an alert and resume the interrupted course on all-clear.</summary>
    [DataField]
    public bool HeaveTo = true;
}
