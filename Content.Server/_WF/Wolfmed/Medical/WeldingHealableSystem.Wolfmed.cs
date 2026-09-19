using System.Linq;
using Content.Server._EinsteinEngines.Silicon.WeldingHealing;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Tools.Components;

namespace Content.Server._EinsteinEngines.Silicon.WeldingHealable;

public sealed partial class WeldingHealableSystem
{
    private static readonly HashSet<TreatmentCapability> RepairCapabilities = new() { TreatmentCapability.Mechanical };

    [Dependency] private WoundHealingSystem _woundHealing = default!;
    [Dependency] private WoundDamageRoutingSystem _woundRouting = default!;
    [Dependency] private ItemToggleSystem _repairToggle = default!;

    private void InitializeWoundRepair()
    {
        SubscribeLocalEvent<WoundHostComponent, InteractUsingEvent>(OnWoundRepair,
            before: [typeof(FlammableSystem)]);
        SubscribeLocalEvent<WoundHostComponent, WoundRepairFinishedEvent>(OnWoundRepairFinished);
    }

    private void OnWoundRepair(Entity<WoundHostComponent> body, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp(args.Used, out WeldingHealingComponent? healing) ||
            !CanUseRepairTool(body, args.User, args.Used, healing))
            return;

        if (!_woundHealing.TryGetTargetedPart(body, args.User, out var requested))
            return;

        var selected = _woundHealing.ResolveHealingPart(body, requested, healing.Damage,
            null, RepairCapabilities, null, 0f, healWounds: true);
        if (selected is not { } partUid || !CanRepairPart(body, partUid, healing))
            return;

        var delay = healing.DoAfterDelay * (body.Owner == args.User ? healing.SelfHealPenalty : 1f);
        args.Handled = StartWoundRepair(body, partUid, args.User, args.Used, healing, delay);
    }

    // WOLFGATE (W6): a tool with no fuel cost is not a welder. A wrench repairs a dented chassis and has
    // nothing to burn, so the fuel check and the fuel spend below are both skipped for it.
    private bool CanUseRepairTool(EntityUid body, EntityUid user, EntityUid tool, WeldingHealingComponent healing) =>
        (body != user || healing.AllowSelfHeal) &&
        _toolSystem.HasQuality(tool, healing.QualityNeeded) &&
        (healing.FuelCost <= 0 || _toolSystem.GetWelderFuelAndCapacity(tool).fuel >= healing.FuelCost);

    private bool CanRepairPart(EntityUid body, EntityUid part, WeldingHealingComponent healing)
    {
        if (!_woundHealing.IsCompatiblePart(body, part, null, RepairCapabilities) ||
            !TryComp(part, out DamageableComponent? damageable) ||
            damageable.DamageContainerID is not { } container || !healing.DamageContainers.Contains(container))
            return false;

        return _woundHealing.HasTreatableWounds(part, healing.Damage, null) ||
               healing.Damage.DamageDict.Any(entry => entry.Value < FixedPoint2.Zero &&
                   damageable.Damage.DamageDict.GetValueOrDefault(entry.Key) > FixedPoint2.Zero);
    }

    private bool StartWoundRepair(EntityUid body, EntityUid part, EntityUid user, EntityUid tool,
        WeldingHealingComponent healing, float delay) =>
        _toolSystem.UseTool(tool, user, body, delay, healing.QualityNeeded,
            new WoundRepairFinishedEvent { Part = GetNetEntity(part), Delay = delay });

    private void OnWoundRepairFinished(Entity<WoundHostComponent> body, ref WoundRepairFinishedEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } tool ||
            !TryComp(tool, out WeldingHealingComponent? healing) ||
            !_repairToggle.IsActivated(tool) ||
            !CanUseRepairTool(body, args.User, tool, healing) ||
            !TryGetEntity(args.Part, out var part) || part is not { } partUid ||
            !CanRepairPart(body, partUid, healing))
            return;

        // WOLFGATE (W6): only a fuelled tool has to have fuel to spend. A wrench has none.
        var fuelled = healing.FuelCost > 0;
        if (fuelled && (!TryComp(tool, out WelderComponent? welder) ||
                        !_solutionContainer.TryGetSolution(tool, welder.FuelSolutionName, out _)))
            return;

        args.Handled = true;
        var user = args.User;
        _woundRouting.WithTreatmentCapabilities(body, RepairCapabilities, () =>
            _woundRouting.TryApplyPartDamage(body, partUid, healing.Damage, user,
                ignoreResistances: true, healWounds: true));
        if (fuelled && TryComp(tool, out WelderComponent? spender) &&
            _solutionContainer.TryGetSolution(tool, spender.FuelSolutionName, out var solution))
            _solutionContainer.RemoveReagent(solution.Value, spender.FuelReagent, healing.FuelCost);
        _popup.PopupEntity(Loc.GetString("comp-repairable-repair", ("target", body.Owner), ("tool", tool)),
            body, args.User);

        if (CanRepairPart(body, partUid, healing) && CanUseRepairTool(body, args.User, tool, healing))
            StartWoundRepair(body, partUid, args.User, tool, healing, args.Delay);
    }

}
