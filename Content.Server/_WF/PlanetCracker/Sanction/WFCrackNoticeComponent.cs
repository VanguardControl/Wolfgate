namespace Content.Server._WF.PlanetCracker.Sanction;

/// <summary>Sanction notices this planet has raised; unsaved round state keeping one cut to one notice.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFCrackNoticeComponent : Component
{
    /// <summary>Whether the begin-crack notice has gone out for the cut currently under way; cleared at AnchorsLocked.</summary>
    [DataField]
    public bool AnnouncedCracking;

    /// <summary>Whether the extraction notice has gone out for this world; never cleared, the cut cannot repeat.</summary>
    [DataField]
    public bool AnnouncedExtraction;
}
