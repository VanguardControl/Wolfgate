namespace Content.Server._WF.PlanetCracker.Sanction;

/// <summary>Which sanction notices this planet has already raised; the latch that keeps one cut to one notice.</summary>
/// <remarks>
/// Server-only and not networked: nothing client-side reads it, and the body already networks Sanctioned and Cracked
/// (WFSectorPlanetComponent). UnsavedComponent for the same reason WFPlanetNetworkComponent.cs:10 and
/// WFSectorPlanetComponent.cs:9 carry it - this is round state, and a saved map must not come back pre-latched.
/// It holds no deadline, so it deliberately carries no TimeOffsetSerializer/AutoPausedField/AutoGenerateComponentPause;
/// the omission is not the F1/F3 pause trap.
/// </remarks>
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
