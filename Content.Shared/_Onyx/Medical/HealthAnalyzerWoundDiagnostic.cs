using Content.Shared._Shitmed.Targeting; // WOLFGATE: D10 keeps TargetBodyPart under _Shitmed; Onyx's _Onyx.Targeting copy is not ported.
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE (W5)
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Medical;

[Serializable, NetSerializable]
public readonly record struct HealthAnalyzerWoundDiagnostic(
    FractureGrade Fracture,
    FractureTreatment FractureTreatment,
    float BleedingRate,
    BleedingTreatment BleedingTreatment,
    ushort ScarCount,
    FixedPoint2 Pain,
    List<HealthAnalyzerVisibleWound> VisibleWounds,
    BodyPartFunctionalityState Functionality,
    float InternalBleedingRate,
    HealthAnalyzerClottingPhase ClottingPhase,
    ushort EmbeddedObjects = 0, // WOLFGATE (W1): rounds and shrapnel still in the part.
    WolfmedInfectionStage Infection = WolfmedInfectionStage.None, // WOLFGATE (W5): worst stage on the part.
    bool Necrotic = false, // WOLFGATE (W5): the part is dead tissue.
    bool NecrosisRisk = false) // WOLFGATE (W5): a tourniquet or a deep burn is killing it.
{
    public bool HasFindings =>
        Fracture != FractureGrade.None || BleedingRate > 0f || ScarCount > 0 || Pain > FixedPoint2.Zero ||
        VisibleWounds.Count > 0 || Functionality != BodyPartFunctionalityState.Functional ||
        InternalBleedingRate > 0f || EmbeddedObjects > 0 || // WOLFGATE (W1)
        Infection != WolfmedInfectionStage.None || Necrotic || NecrosisRisk; // WOLFGATE (W5)
}

[Serializable, NetSerializable]
public readonly record struct HealthAnalyzerVisibleWound(LocId Name, LocId? StageName, int Count);

[Serializable, NetSerializable]
public enum HealthAnalyzerClottingPhase : byte
{
    NotApplicable,
    None,
    InProgress,
    Complete,
    Mixed,
}

[Serializable, NetSerializable]
public sealed class HealthAnalyzerWoundDiagnostics
{
    public readonly Dictionary<TargetBodyPart, HealthAnalyzerWoundDiagnostic> Parts;

    /// <summary>WOLFGATE (W5): systemic infection, 0 to 100. Body-level, so it sits beside the parts.</summary>
    public readonly float Sepsis;

    public HealthAnalyzerWoundDiagnostics(
        Dictionary<TargetBodyPart, HealthAnalyzerWoundDiagnostic> parts,
        float sepsis = 0f) // WOLFGATE (W5)
    {
        Parts = parts;
        Sepsis = sepsis; // WOLFGATE (W5)
    }
}
