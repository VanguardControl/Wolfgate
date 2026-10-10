namespace Content.Server._WF.ReaverCampaign.Components;

/// <summary>A Reaver raid, on its encounter entity: what it was sent at and from where.</summary>
[RegisterComponent]
public sealed partial class WFReaverRaidComponent : Component
{
    /// <summary>The player ship or station it was sent at.</summary>
    [ViewVariables]
    public EntityUid Target;

    /// <summary>Whether the target is a station.</summary>
    [ViewVariables]
    public bool Station;

    [ViewVariables]
    public EntityUid Stronghold;
}
