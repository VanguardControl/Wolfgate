using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Surgery;

/// <summary>Shared half of the wound surgeries: listing conditions and step completion checks.</summary>
/// <remarks>
/// Every SurgeryValidEvent and SurgeryStepCompleteCheckEvent handler has to be shared - the surgery BUI runs
/// GetNextStep/IsStepComplete and CanPerformStep client-side, so a server-only check highlights the wrong row.
/// The mutating SurgeryStepEvent half lives in Content.Server (WolfmedWoundSurgerySystem) because
/// WoundBleedingSystem and OrganHealthSystem are server-assembly classes (D13).
/// </remarks>
public sealed class WolfmedSurgeryConditionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WoundFractureSystem _fractures = default!;
    [Dependency] private WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WolfmedSurgeryWoundConditionComponent, SurgeryValidEvent>(OnWoundValid);
        SubscribeLocalEvent<WolfmedSurgeryFractureConditionComponent, SurgeryValidEvent>(OnFractureValid);
        SubscribeLocalEvent<WolfmedSurgeryOrganDamagedConditionComponent, SurgeryValidEvent>(OnOrganValid);
        SubscribeLocalEvent<WolfmedSurgeryClampBleedingEffectComponent, SurgeryStepCompleteCheckEvent>(OnClampCheck);
        SubscribeLocalEvent<WolfmedSurgeryTreatWoundEffectComponent, SurgeryStepCompleteCheckEvent>(OnTreatCheck);
        SubscribeLocalEvent<WolfmedSurgeryMendFractureEffectComponent, SurgeryStepCompleteCheckEvent>(OnFractureCheck);
        SubscribeLocalEvent<WolfmedSurgeryOrganHealEffectComponent, SurgeryStepCompleteCheckEvent>(OnOrganCheck);
        SubscribeLocalEvent<WolfmedSurgeryIncisionTreatmentEffectComponent, SurgeryStepCompleteCheckEvent>(OnIncisionCheck);
    }

    private void OnWoundValid(Entity<WolfmedSurgeryWoundConditionComponent> ent, ref SurgeryValidEvent args)
    {
        var found = FindWound(args.Part, ent.Comp.WoundPrototype, ent.Comp.State, ent.Comp.Visibility,
            ent.Comp.Bleeding, ent.Comp.InternalBleeding) != null;

        if (found == ent.Comp.Inverse)
            args.Cancelled = true;
    }

    private void OnFractureValid(Entity<WolfmedSurgeryFractureConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (_fractures.GetFracture(args.Part) is not { } fracture ||
            (ent.Comp.Grade is { } grade ? fracture.Comp2.Grade != grade : fracture.Comp2.Grade < ent.Comp.MinGrade) ||
            ent.Comp.Treatment is { } treatment && fracture.Comp2.Treatment != treatment)
            args.Cancelled = true;
    }

    private void OnOrganValid(Entity<WolfmedSurgeryOrganDamagedConditionComponent> ent, ref SurgeryValidEvent args)
    {
        // Damaged but still alive: OrganHealthSystem.Update destroys any organ at Health <= 0 on the next tick,
        // so a dead organ is an insert job, not a heal job (P4-D24).
        var treatable = TryFindOrgan(args.Part, ent.Comp.Slot, out var organ) &&
                        organ.Comp.Health > FixedPoint2.Zero &&
                        organ.Comp.Health < organ.Comp.MaxHealth;

        if (treatable == ent.Comp.Inverse)
            args.Cancelled = true;
    }

    private void OnClampCheck(Entity<WolfmedSurgeryClampBleedingEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (FindWound(args.Part, ent.Comp.WoundPrototype, bleeding: true) != null)
            args.Cancelled = true;
    }

    private void OnTreatCheck(Entity<WolfmedSurgeryTreatWoundEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (FindWound(args.Part, ent.Comp.WoundPrototype, internalBleeding: ent.Comp.InternalBleeding) != null)
            args.Cancelled = true;
    }

    private void OnFractureCheck(Entity<WolfmedSurgeryMendFractureEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        // Complete when the fracture is gone, already at or past the target treatment, or when the target can
        // never be reached: CanTreat gates Reduced on Grade >= profile.ReductionMinimumGrade (Simple on the
        // organic profile), so a Hairline fracture can never be Reduced and a "wait for Reduced" check would
        // stall the surgery forever (P4-D20).
        if (_fractures.GetFracture(args.Part) is not { } fracture)
            return;

        if (fracture.Comp2.Treatment >= ent.Comp.Treatment || !CanEverReach(fracture, ent.Comp.Treatment))
            return;

        args.Cancelled = true;
    }

    private void OnOrganCheck(Entity<WolfmedSurgeryOrganHealEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryFindOrgan(args.Part, ent.Comp.Slot, out var organ) || organ.Comp.Health < organ.Comp.MaxHealth)
            args.Cancelled = true;
    }

    private void OnIncisionCheck(Entity<WolfmedSurgeryIncisionTreatmentEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (!TryComp(args.Part, out WoundableComponent? woundable))
            return;

        foreach (var wound in _wounds.GetWounds((args.Part, woundable)))
        {
            if (wound.Comp.Prototype != ent.Comp.Wound)
                continue;

            var pending = ent.Comp.Treatment switch
            {
                WolfmedIncisionTreatment.Clamp => TryComp(wound, out WoundBleedingComponent? bleeding) &&
                                                  bleeding.Treatment < BleedingTreatment.Clamped,
                _ => wound.Comp.State is WoundState.Open or WoundState.Stabilized,
            };

            if (!pending)
                continue;

            args.Cancelled = true;
            return;
        }
    }

    /// <summary>Re-states WoundFractureSystem.CanTreat's reachability rule without touching the private original.</summary>
    private bool CanEverReach(Entity<WoundComponent, WoundFractureComponent> fracture, FractureTreatment treatment)
    {
        if (fracture.Comp2.Grade == FractureGrade.None)
            return false;

        if (treatment != FractureTreatment.Reduced)
            return treatment == FractureTreatment.Mended;

        return _fractures.TryGetProfile(fracture.Comp1.HoldingPart, out var profile) &&
               fracture.Comp2.Grade >= profile.ReductionMinimumGrade;
    }

    /// <summary>The highest-severity wound on a part matching a condition's filters, or null.</summary>
    public Entity<WoundComponent>? FindWound(
        Entity<WoundableComponent?> part,
        ProtoId<WoundPrototype>? prototypeId = null,
        WoundState? state = null,
        WoundVisibility? visibility = null,
        bool bleeding = false,
        bool internalBleeding = false)
    {
        if (!Resolve(part, ref part.Comp, false))
            return null;

        Entity<WoundComponent>? selected = null;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (HasComp<WoundScarComponent>(wound) ||
                prototypeId is { } id && wound.Comp.Prototype != id ||
                state is { } woundState && wound.Comp.State != woundState)
                continue;

            if (!_prototypes.TryIndex(wound.Comp.Prototype, out WoundPrototype? prototype) ||
                visibility is { } woundVisibility && prototype.Visibility != woundVisibility)
                continue;

            if (bleeding && (!TryComp(wound, out WoundBleedingComponent? bleedingComp) ||
                    wound.Comp.State != WoundState.Open || bleedingComp.CurrentRate <= 0f))
                continue;

            if (internalBleeding && (!TryComp(wound, out WoundInternalBleedingComponent? internalComp) ||
                    wound.Comp.State != WoundState.Open || internalComp.Severity <= FixedPoint2.Zero))
                continue;

            if (selected is { } current && current.Comp.Severity >= wound.Comp.Severity)
                continue;

            selected = wound;
        }

        return selected;
    }

    /// <summary>Sum of non-scar wound severity on a part whose prototype damages any type in the group.</summary>
    public FixedPoint2 GetGroupSeverity(Entity<WoundableComponent?> part, ProtoId<DamageGroupPrototype> group)
    {
        if (!Resolve(part, ref part.Comp, false) || !_prototypes.TryIndex(group, out var groupPrototype))
            return FixedPoint2.Zero;

        var types = groupPrototype.DamageTypes.ToHashSet();
        var severity = FixedPoint2.Zero;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (HasComp<WoundScarComponent>(wound) ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out WoundPrototype? prototype) ||
                // Wolfgate's DamageGroupPrototype.DamageTypes is List<string>, Onyx's is List<ProtoId<...>>.
                !prototype.DamageTypes.Keys.Any(type => types.Contains(type.Id)))
                continue;

            severity += wound.Comp.Severity;
        }

        return severity;
    }

    /// <summary>The named organ slot on a part, if it carries Wolfmed health data.</summary>
    public bool TryFindOrgan(EntityUid part, string slot, out Entity<WolfmedOrganComponent> organ)
    {
        foreach (var (id, comp) in _body.GetPartOrgans(part))
        {
            if (comp.SlotId != slot || !TryComp(id, out WolfmedOrganComponent? health))
                continue;

            organ = (id, health);
            return true;
        }

        organ = default;
        return false;
    }
}
