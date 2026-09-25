using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// Score thresholds a vein table is rated against; one document ships, at <see cref="SharedWFSurveySystem.DefaultBands"/>.
/// </summary>
// Kind named explicitly: Robust would derive "wFVeinRatingBands".
[Prototype("wfVeinRatingBands")]
public sealed partial class WFVeinRatingBandsPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Score at or past which a table rates <see cref="WFVeinRating.Fair"/>.</summary>
    [DataField]
    public float Fair = 4000f;

    /// <summary>Score at or past which a table rates <see cref="WFVeinRating.Rich"/>.</summary>
    [DataField]
    public float Rich = 8000f;

    /// <summary>Score at or past which a table rates <see cref="WFVeinRating.VeryRich"/>.</summary>
    [DataField]
    public float VeryRich = 14000f;
}
