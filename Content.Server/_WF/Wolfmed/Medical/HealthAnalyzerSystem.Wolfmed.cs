// WOLFGATE: HOOK 23 body. Port of Onyx's HealthAnalyzerSystem diagnostic builders (P4-4a), kept out of the
// upstream file so it carries only the one-line call site. The builders are public so a headless test can
// call them (P4-D26) - UpdateScannedUser itself ends in a network send with no capture point.

using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.Medical;

public sealed partial class HealthAnalyzerSystem
{
    [Dependency] private WoundSystem _wounds = default!; // WOLFGATE: HOOK 23
    [Dependency] private PainSystem _pain = default!; // WOLFGATE: HOOK 23
    [Dependency] private BodyPartFunctionalitySystem _functionality = default!; // WOLFGATE: HOOK 23
    [Dependency] private MobThresholdSystem _mobThreshold = default!; // WOLFGATE: HOOK 23
    [Dependency] private IPrototypeManager _prototypes = default!; // WOLFGATE: HOOK 23

    /// <summary>Per-part wound findings for a wound host, or null for anything else.</summary>
    public HealthAnalyzerWoundDiagnostics? BuildWoundDiagnostics(EntityUid body)
    {
        // WOLFGATE (D2): Onyx gates on SurgeryTargetComponent; a Shitmed surgery target without WoundHost
        // (a borg, a synthetic species) has no WoundableComponent anywhere and would report "no findings"
        // instead of "unavailable".
        if (!HasComp<WoundHostComponent>(body))
            return null;

        var result = new Dictionary<TargetBodyPart, HealthAnalyzerWoundDiagnostic>();
        foreach (var (part, bodyPart) in _bodySystem.GetBodyChildren(body))
        {
            // WOLFGATE: SharedTargetingSystem.TryConvert does not exist in Wolfgate; Shitmed's
            // SharedBodySystem.GetTargetBodyPart is the equivalent conversion.
            if (!TryComp(part, out WoundableComponent? woundable) ||
                _bodySystem.GetTargetBodyPart(bodyPart.PartType, bodyPart.Symmetry) is not { } target)
                continue;

            var fracture = FractureGrade.None;
            var fractureTreatment = FractureTreatment.None;
            var bleedingRate = 0f;
            var bleedingTreatment = BleedingTreatment.None;
            var highestBleedingRate = 0f;
            ushort scarCount = 0;
            var visibleWounds = new Dictionary<(LocId Name, LocId? StageName), int>();
            var clottingPhases = new HashSet<HealthAnalyzerClottingPhase>();
            var internalBleedingRate = 0f;
            var pain = TryComp(part, out PainComponent? painComponent)
                ? _pain.GetPain((part, painComponent))
                : FixedPoint2.Zero;

            foreach (var wound in _wounds.GetWounds((part, woundable)))
            {
                if (TryComp(wound, out WoundFractureComponent? foundFracture) &&
                    foundFracture.Treatment != FractureTreatment.Mended &&
                    foundFracture.Grade > fracture)
                {
                    fracture = foundFracture.Grade;
                    fractureTreatment = foundFracture.Treatment;
                }

                if (TryComp(wound, out WoundBleedingComponent? foundBleeding) && foundBleeding.CurrentRate > 0f)
                {
                    bleedingRate += foundBleeding.CurrentRate;
                    if (foundBleeding.CurrentRate > highestBleedingRate)
                    {
                        highestBleedingRate = foundBleeding.CurrentRate;
                        bleedingTreatment = foundBleeding.Treatment;
                    }
                }

                if (HasComp<WoundScarComponent>(wound))
                    scarCount++;

                if (TryComp(wound, out WoundInternalBleedingComponent? internalBleeding) &&
                    wound.Comp.State == WoundState.Open && internalBleeding.Severity > FixedPoint2.Zero)
                    internalBleedingRate += internalBleeding.Rate * internalBleeding.Severity.Float();

                if (TryComp(wound, out WoundBleedingComponent? clotting) && wound.Comp.State == WoundState.Open)
                {
                    clottingPhases.Add(clotting.AutomaticClottingAt != null
                        ? HealthAnalyzerClottingPhase.InProgress
                        : clotting.NaturalClotting > 0f && clotting.CurrentRate <= 0f
                            ? HealthAnalyzerClottingPhase.Complete
                            : HealthAnalyzerClottingPhase.None);
                }

                if (!_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                    prototype.Visibility != WoundVisibility.Visible ||
                    wound.Comp.State is WoundState.Healed or WoundState.Scarred)
                    continue;

                var stageName = prototype.GetStageDefinition(wound.Comp.Severity)?.Name;
                var key = (prototype.Name, stageName);
                visibleWounds[key] = visibleWounds.GetValueOrDefault(key) + 1;
            }

            var wounds = visibleWounds
                .OrderBy(entry => entry.Key.Name)
                .ThenBy(entry => entry.Key.StageName)
                .Select(entry => new HealthAnalyzerVisibleWound(entry.Key.Name, entry.Key.StageName, entry.Value))
                .ToList();
            var clottingPhase = clottingPhases.Count switch
            {
                0 => HealthAnalyzerClottingPhase.NotApplicable,
                1 => clottingPhases.Single(),
                _ => HealthAnalyzerClottingPhase.Mixed,
            };

            var diagnostic = new HealthAnalyzerWoundDiagnostic(
                fracture,
                fractureTreatment,
                bleedingRate,
                bleedingTreatment,
                scarCount,
                pain,
                wounds,
                _functionality.GetState((part, woundable)),
                internalBleedingRate,
                clottingPhase);

            if (diagnostic.HasFindings)
                result[target] = diagnostic;
        }

        return new HealthAnalyzerWoundDiagnostics(result);
    }

    /// <summary>Organ health rows in medical reading order, or null for a non-wound-host.</summary>
    public List<HealthAnalyzerOrganInfo>? BuildOrganInfo(EntityUid body)
    {
        if (!HasComp<WoundHostComponent>(body))
            return null;

        var result = new List<HealthAnalyzerOrganInfo>();
        foreach (var (organ, component) in _bodySystem.GetBodyOrgans(body))
        {
            // WOLFGATE (D8): health lives on WolfmedOrganComponent, and Onyx's OrganCategoryPrototype is Nubody -
            // Shitmed's SlotId already carries the same ids (brain, eyes, lungs, heart, stomach, liver, kidneys).
            if (!TryComp(organ, out WolfmedOrganComponent? health))
                continue;

            result.Add(new HealthAnalyzerOrganInfo(
                GetNetEntity(organ),
                health.Health,
                health.MaxHealth,
                OrganOrder(component.SlotId)));
        }

        result.Sort((left, right) => left.Order.CompareTo(right.Order));
        return result;
    }

    private static int OrganOrder(string? slotId) => slotId?.ToLowerInvariant() switch
    {
        "brain" => 0,
        "eyes" => 1,
        "ears" => 2,
        "tongue" => 3,
        "lungs" => 4,
        "heart" => 5,
        "liver" => 6,
        "stomach" => 7,
        "appendix" => 8,
        "kidneys" => 9,
        _ => 10,
    };

    /// <summary>Reagent contents of the bloodstream, chemical, stomach and lung solutions.</summary>
    public List<HealthAnalyzerChemicalInfo>? BuildChemicalInfo(EntityUid body, BloodstreamComponent? bloodstream)
    {
        var result = new List<HealthAnalyzerChemicalInfo>();
        if (bloodstream != null)
        {
            if (_solutionContainerSystem.TryGetSolution(body, bloodstream.BloodSolutionName, out _, out var blood))
                AddChemicalInfo(result, HealthAnalyzerSolutionType.Bloodstream, blood);

            // WOLFGATE: Wolfgate's bloodstream names its reagent solution ChemicalSolutionName; the wire value
            // stays Metabolites so the payload type does not churn, and the client localises it as "Chemicals".
            if (_solutionContainerSystem.TryGetSolution(body, bloodstream.ChemicalSolutionName, out _, out var chemicals))
                AddChemicalInfo(result, HealthAnalyzerSolutionType.Metabolites, chemicals);
        }

        foreach (var (organ, _) in _bodySystem.GetBodyOrgans(body))
        {
            if (HasComp<StomachComponent>(organ) &&
                _solutionContainerSystem.TryGetSolution(organ, StomachSystem.DefaultSolutionName, out _, out var stomach))
                AddChemicalInfo(result, HealthAnalyzerSolutionType.Stomach, stomach);

            if (TryComp(organ, out LungComponent? lung) &&
                _solutionContainerSystem.TryGetSolution(organ, lung.SolutionName, out _, out var lungs))
                AddChemicalInfo(result, HealthAnalyzerSolutionType.Lung, lungs);
        }

        return result;
    }

    private static void AddChemicalInfo(
        List<HealthAnalyzerChemicalInfo> result,
        HealthAnalyzerSolutionType type,
        Solution solution)
    {
        var reagents = new List<HealthAnalyzerReagentInfo>();
        foreach (var reagent in solution.Contents)
            reagents.Add(new HealthAnalyzerReagentInfo(reagent.Reagent.Prototype, reagent.Quantity));

        result.Add(new HealthAnalyzerChemicalInfo(type, reagents));
    }

    /// <summary>The damage figure that actually decides crit and death on a wound host.</summary>
    public FixedPoint2? BuildVitalDamage(EntityUid body) =>
        HasComp<WoundHostComponent>(body) && TryComp(body, out DamageableComponent? damageable)
            ? _mobThreshold.CheckVitalDamage(body, damageable)
            : null;
}
