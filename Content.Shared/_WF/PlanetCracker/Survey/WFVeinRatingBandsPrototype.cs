using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// The score thresholds a vein table is rated against, so ratings are comparable across planets and tunable in YAML.
/// One document ships, at <see cref="SharedWFSurveySystem.DefaultBands"/>.
/// </summary>
/// <remarks>
/// The prototype kind string is declared explicitly. Robust derives an unqualified kind by lowercasing only index 0,
/// which would register this type as "wFVeinRatingBands" and break every "- type: wfVeinRatingBands" document.
/// </remarks>
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
