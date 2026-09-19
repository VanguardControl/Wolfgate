using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Every timer, threshold and dose in the infection, sepsis and necrosis model. One shipped instance
/// (<see cref="WolfmedInfectionSystem.DefaultProfile"/>); the CVars scale what is written here rather than
/// replacing it, so an admin tunes speed and a downstream server tunes shape.
/// </summary>
[Prototype("wolfmedInfectionProfile")]
public sealed partial class WolfmedInfectionProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    // Infection progress. One wound's contamination runs 0 to SepsisAt; the stage is read off the same number.

    /// <summary>Progress an untreated wound of risk 1 gains per minute.</summary>
    [DataField]
    public float ProgressPerMinute = 6f;

    /// <summary>Progress at which the wound itself starts to hurt and to widen.</summary>
    [DataField]
    public float LocalAt = 25f;

    /// <summary>Progress at which the infection leaves the wound: fever and toxins.</summary>
    [DataField]
    public float SpreadingAt = 60f;

    /// <summary>Progress at which the body goes septic.</summary>
    [DataField]
    public float SepsisAt = 100f;

    /// <summary>Progress a cleaned wound sheds per minute while it is still only locally infected.</summary>
    [DataField]
    public float CleanDecayPerMinute = 20f;

    /// <summary>Progress an antibiotic clears per unit metabolised.</summary>
    [DataField]
    public float AntibioticPerUnit = 12f;

    /// <summary>
    /// Progress multiplier per bleeding treatment. A wound that is closed or dressed early is most of the
    /// prevention in the model; anything absent from this table counts as untreated.
    /// </summary>
    [DataField]
    public Dictionary<BleedingTreatment, float> TreatmentMultipliers = new()
    {
        [BleedingTreatment.Bandaged] = 0.15f,
        [BleedingTreatment.Clamped] = 0.5f,
        [BleedingTreatment.Sutured] = 0f,
        [BleedingTreatment.Cauterized] = 0f,
    };

    /// <summary>Progress multiplier for a wound something dirty has been in: a knife digging a round out.</summary>
    [DataField]
    public float ContaminationMultiplier = 2.5f;

    // Local stage.

    /// <summary>Pain the part takes per minute while the wound is locally infected.</summary>
    [DataField]
    public FixedPoint2 LocalPainPerMinute = FixedPoint2.New(4);

    /// <summary>
    /// Severity the wound regains per minute while infected. Expresses "slower healing": treatment has to
    /// outrun the infection instead of being cancelled by it.
    /// </summary>
    [DataField]
    public FixedPoint2 SeverityPerMinute = FixedPoint2.New(1);

    /// <summary>Total severity one infection may ever add back, so a wound cannot grow without bound.</summary>
    [DataField]
    public FixedPoint2 MaxSeverityAdded = FixedPoint2.New(15);

    // Spreading stage.

    /// <summary>Body temperature a fever climbs towards, in kelvin.</summary>
    [DataField]
    public float FeverTemperature = 313f;

    /// <summary>Kelvin per second the fever climbs while it is below that.</summary>
    [DataField]
    public float FeverRise = 0.05f;

    /// <summary>Poison dealt to the body per minute per spreading wound.</summary>
    [DataField]
    public FixedPoint2 SpreadingPoisonPerMinute = FixedPoint2.New(1);

    // Sepsis.

    /// <summary>Sepsis progress gained per minute per source (a spreading wound, a necrotic part).</summary>
    [DataField]
    public float SepsisPerMinute = 12f;

    /// <summary>Sepsis progress shed per minute once every source is gone.</summary>
    [DataField]
    public float SepsisRecoveryPerMinute = 8f;

    /// <summary>Sepsis progress an antibiotic clears per unit metabolised.</summary>
    [DataField]
    public float SepsisAntibioticPerUnit = 6f;

    /// <summary>Poison per minute at full sepsis; it scales linearly from a quarter of this at onset.</summary>
    [DataField]
    public FixedPoint2 SepsisPoisonPerMinute = FixedPoint2.New(6);

    // Necrosis.

    /// <summary>How long a tourniquet may stay on a limb before the limb under it dies.</summary>
    [DataField]
    public TimeSpan TourniquetOnset = TimeSpan.FromMinutes(10);

    /// <summary>Fraction of the onset at which the patient and the analyzer are warned.</summary>
    [DataField]
    public float WarnFraction = 0.6f;

    /// <summary>How long a severed limb stays viable. Reattached later than this, it is dead tissue.</summary>
    [DataField]
    public TimeSpan ReattachGrace = TimeSpan.FromMinutes(5);

    /// <summary>The wound dead tissue leaves on the part.</summary>
    [DataField]
    public ProtoId<WoundPrototype> NecrosisWound = "WolfmedNecrosisWound";

    /// <summary>Severity of that wound.</summary>
    [DataField]
    public FixedPoint2 NecrosisSeverity = FixedPoint2.New(60);
}
