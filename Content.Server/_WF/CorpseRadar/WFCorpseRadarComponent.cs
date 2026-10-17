namespace Content.Server._WF.CorpseRadar;

/// <summary>Marks a dead player's body whose radar blip this module added, so revival removes only that blip.</summary>
[RegisterComponent]
public sealed partial class WFCorpseRadarComponent : Component;
