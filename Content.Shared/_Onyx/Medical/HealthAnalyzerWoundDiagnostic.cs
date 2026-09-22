using Content.Shared._Shitmed.Targeting; // WOLFGATE: D10 keeps TargetBodyPart under _Shitmed; Onyx's _Onyx.Targeting copy is not ported.
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE (W5)
using Content.Shared._WF.Wolfmed.Reagents; // WOLFGATE (CONSC)
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
    bool NecrosisRisk = false, // WOLFGATE (W5): a tourniquet or a deep burn is killing it.
    bool Mechanical = false, // WOLFGATE (W6): a chassis, so the generic labels get their -mechanical variants.
    bool Overheating = false, // WOLFGATE (W6): the part is running too hot to work properly.
    // WOLFGATE (UI4): what has already been done here. A clamped, sutured or cauterised wound bleeds at
    // zero, so nothing else in this payload can tell a treated part from an untouched one.
    WolfmedPartTreatments Treatments = WolfmedPartTreatments.None,
    // WOLFGATE (EVISC): organ slots this part carries that nothing is in, which is how the procedure
    // window can tell a patient whose organs are back in from one still waiting for them.
    ushort MissingOrgans = 0)
{
    public bool HasFindings =>
        Fracture != FractureGrade.None || BleedingRate > 0f || ScarCount > 0 || Pain > FixedPoint2.Zero ||
        VisibleWounds.Count > 0 || Functionality != BodyPartFunctionalityState.Functional ||
        InternalBleedingRate > 0f || EmbeddedObjects > 0 || // WOLFGATE (W1)
        Infection != WolfmedInfectionStage.None || Necrotic || NecrosisRisk || // WOLFGATE (W5)
        Overheating; // WOLFGATE (W6)
}

[Serializable, NetSerializable]
public readonly record struct HealthAnalyzerVisibleWound(
    LocId Name,
    LocId? StageName,
    int Count,
    // WOLFGATE (UI2): the analyzer groups and tints its wound rows by this; resolved from the prototype.
    WolfmedWoundCategory Category = WolfmedWoundCategory.Other,
    // WOLFGATE (UI3): the wound prototype id, which is what the treatment advice keys are derived from.
    string Prototype = "");

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

    /// <summary>WOLFGATE (CONSC): strongest painkiller tier in the patient, or None.</summary>
    public readonly WolfmedPainReliefTier PainRelief;

    /// <summary>WOLFGATE (CONSC): seconds of pain relief left.</summary>
    public readonly float PainReliefSeconds;

    /// <summary>WOLFGATE (CONSC): sedation, 0 to 1. Past its threshold the patient stops breathing.</summary>
    public readonly float Sedation;

    public HealthAnalyzerWoundDiagnostics(
        Dictionary<TargetBodyPart, HealthAnalyzerWoundDiagnostic> parts,
        float sepsis = 0f, // WOLFGATE (W5)
        WolfmedPainReliefTier painRelief = WolfmedPainReliefTier.None, // WOLFGATE (CONSC)
        float painReliefSeconds = 0f, // WOLFGATE (CONSC)
        float sedation = 0f) // WOLFGATE (CONSC)
    {
        Parts = parts;
        Sepsis = sepsis; // WOLFGATE (W5)
        PainRelief = painRelief; // WOLFGATE (CONSC)
        PainReliefSeconds = painReliefSeconds; // WOLFGATE (CONSC)
        Sedation = sedation; // WOLFGATE (CONSC)
    }
}
