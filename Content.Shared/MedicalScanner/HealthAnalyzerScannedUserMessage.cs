using Content.Shared._Shitmed.Targeting; // Shitmed Change
using Content.Shared._Onyx.Medical; // WOLFGATE: EXT 2 — Wolfmed diagnostic payload types.
using Content.Shared.FixedPoint; // WOLFGATE: EXT 2 — vital damage is FixedPoint2.
using Robust.Shared.Serialization;

namespace Content.Shared.MedicalScanner;

/// <summary>
///     On interacting with an entity retrieves the entity UID for use with getting the current damage of the mob.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerScannedUserMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity? TargetEntity;
    public float Temperature;
    public float BloodLevel;
    public bool? ScanMode;
    public bool? Bleeding;
    public Dictionary<TargetBodyPart, TargetIntegrity>? Body; // Shitmed Change
    public NetEntity? Part; // Shitmed Change
    public bool? Unrevivable;
    public bool? Uncloneable; // Frontier
    public HealthAnalyzerWoundDiagnostics? WoundDiagnostics; // WOLFGATE: EXT 2 — per-part wound findings, null for non-wound-hosts.
    public List<HealthAnalyzerOrganInfo>? Organs; // WOLFGATE: EXT 2 — organ health rows, null for non-wound-hosts.
    public List<HealthAnalyzerChemicalInfo>? Chemicals; // WOLFGATE: EXT 2 — bloodstream/chemical/stomach/lung contents.
    public FixedPoint2? VitalDamage; // WOLFGATE: EXT 2 — the damage figure that decides crit on a wound host.

    public HealthAnalyzerScannedUserMessage(NetEntity? targetEntity, float temperature, float bloodLevel, bool? scanMode, bool? bleeding, bool? unrevivable, bool? uncloneable, Dictionary<TargetBodyPart, TargetIntegrity>? body, NetEntity? part = null, HealthAnalyzerWoundDiagnostics? woundDiagnostics = null, List<HealthAnalyzerOrganInfo>? organs = null, List<HealthAnalyzerChemicalInfo>? chemicals = null, FixedPoint2? vitalDamage = null) // Shitmed Change // WOLFGATE: EXT 2 — four appended optional parameters.
    {
        TargetEntity = targetEntity;
        Temperature = temperature;
        BloodLevel = bloodLevel;
        ScanMode = scanMode;
        Bleeding = bleeding;
        Body = body; // Shitmed Change
        Part = part; // Shitmed Change
        Unrevivable = unrevivable;
        Uncloneable = uncloneable; // Frontier
        // WOLFGATE: EXT 2 start
        WoundDiagnostics = woundDiagnostics;
        Organs = organs;
        Chemicals = chemicals;
        VitalDamage = vitalDamage;
        // WOLFGATE: EXT 2 end
    }
}

// Shitmed Change Start
[Serializable, NetSerializable]
public sealed class HealthAnalyzerPartMessage(NetEntity? owner, TargetBodyPart? bodyPart) : BoundUserInterfaceMessage
{
    public readonly NetEntity? Owner = owner;
    public readonly TargetBodyPart? BodyPart = bodyPart;

}
// Shitmed Change End
