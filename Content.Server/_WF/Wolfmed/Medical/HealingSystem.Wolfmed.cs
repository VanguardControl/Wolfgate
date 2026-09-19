// WOLFGATE: HOOK 8 body. HealingSystem.cs calls into OnWoundHostDoAfter for wound hosts instead of the flat
// DamageableComponent path; this file is that entire branch, mirroring Onyx's HealingSystem wound branches
// (hooks-a.md 5a/5b/5d). Kept out of the upstream file so it carries only the call site.

using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Medical.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Medical;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server.Medical;

public sealed partial class HealingSystem
{
    [Dependency] private WoundHealingSystem _woundHealing = default!; // WOLFGATE: HOOK 8

    private List<ProtoId<DamageContainerPrototype>>? GetHealingContainers(HealingComponent healing) =>
        healing.DamageContainers?.Select(x => new ProtoId<DamageContainerPrototype>(x)).ToList();

    private void OnWoundHostDoAfter(Entity<DamageableComponent> entity, ref HealingDoAfterEvent args,
        HealingComponent healing)
    {
        if (args.Used is not { } used)
            return;

        EntityUid? requestedPart = null;
        if (args.RequestedPart is { } selected && !TryGetEntity(selected, out requestedPart))
            return;
        if (requestedPart is { } concretePart &&
            _woundHealing.ResolveHealingPart(entity, concretePart,
                _woundHealing.GetTreatableDamage(healing), GetHealingContainers(healing), // W0: TreatedDamageTypes
                healing.TreatmentCapabilities, healing.AllowedWoundStages, healing.BloodlossModifier,
                healing.HealWounds) != concretePart)
        {
            var message = _bodySystem.BodyHasChild(entity, concretePart)
                ? "targeting-selected-part-incompatible"
                : "targeting-selected-part-missing";
            _popupSystem.PopupEntity(Loc.GetString(message), entity, args.User);
            return;
        }

        if (!_woundHealing.TryApplyHealing(entity, requestedPart, (used, healing), args.User,
                out var healed, out var stoppedBleeding))
            return;

        if (healing.ModifyBloodLevel != 0)
            _bloodstreamSystem.TryModifyBloodLevel(entity.Owner, healing.ModifyBloodLevel);

        if (stoppedBleeding)
        {
            if (entity.Owner == args.User)
                _popupSystem.PopupEntity(Loc.GetString("medical-item-stop-bleeding-self"), entity, args.User);
            else
                _popupSystem.PopupEntity(Loc.GetString("medical-item-stop-bleeding", ("target", Identity.Entity(entity.Owner, EntityManager))), entity, args.User);
        }

        var dontRepeat = false;
        if (TryComp<StackComponent>(used, out var stackComp))
        {
            _stacks.Use(used, 1, stackComp);

            if (_stacks.GetCount(used, stackComp) <= 0)
                dontRepeat = true;
        }
        else
        {
            QueueDel(used);
        }

        var total = healed.GetTotal();
        if (entity.Owner != args.User)
        {
            _adminLogger.Add(LogType.Healed,
                $"{EntityManager.ToPrettyString(args.User):user} healed {EntityManager.ToPrettyString(entity.Owner):target} for {total:damage} damage");
        }
        else
        {
            _adminLogger.Add(LogType.Healed,
                $"{EntityManager.ToPrettyString(args.User):user} healed themselves for {total:damage} damage");
        }

        _audio.PlayPvs(healing.HealingEndSound, entity.Owner);

        args.Repeat = !dontRepeat && IsWoundDamaged(entity, healing, requestedPart);
        if (!args.Repeat && !dontRepeat)
            _popupSystem.PopupEntity(Loc.GetString("medical-item-finished-using", ("item", used)), entity.Owner, args.User);
        args.Handled = true;
    }

    /// <summary>Onyx's wound-host half of HasDamage, kept beside Wolfgate's own checks instead of folded into them.</summary>
    private bool IsWoundDamaged(Entity<DamageableComponent> entity, HealingComponent healing, EntityUid? requestedPart)
    {
        if (!TryComp(entity, out WoundHostComponent? host))
            return false;

        // W0: everything below asks about the treatable half of the spec, not the whole item.
        var treatable = _woundHealing.GetTreatableDamage(healing);
        var resolve = new ResolveHealingPartEvent(entity, treatable, GetHealingContainers(healing),
            healing.TreatmentCapabilities, healing.AllowedWoundStages, healing.BloodlossModifier, requestedPart,
            healing.HealWounds);
        RaiseLocalEvent(entity.Owner, ref resolve);
        if (!resolve.Accepted)
            return false;

        if (healing.HealDamage)
        {
            foreach (var (type, amount) in treatable.DamageDict)
            {
                var source = host.LocalizedDamageTypes.Contains(type) ? resolve.Part : entity.Owner;
                if (amount < 0 && source is { } sourceEntity &&
                    TryComp(sourceEntity, out DamageableComponent? sourceDamage) &&
                    sourceDamage.Damage.DamageDict.GetValueOrDefault(type) > 0)
                    return true;
            }
        }

        // W0: `!HealDamage` mirrors TryApplyHealing, which only calls TryHealWounds on that branch - an item
        // that removes damage reaches wounds through the damage it removes. Without the guard a topical with
        // healingMultiplier 0.15 reports work left after the part damage is gone and repeats over the whole
        // stack for nothing.
        if (healing.HealWounds && !healing.HealDamage && resolve.Part is { } woundPart &&
            _woundHealing.HasTreatableWounds(woundPart, treatable, healing.AllowedWoundStages))
            return true;

        if (resolve.Part is { } bleedingPart && healing.BloodlossModifier < 0 &&
            _woundHealing.CanTreatBleeding(bleedingPart))
            return true;

        if (healing.ModifyBloodLevel > 0 && TryComp<BloodstreamComponent>(entity, out var hostBloodstream) &&
            _solutionContainerSystem.ResolveSolution(entity.Owner, hostBloodstream.BloodSolutionName,
                ref hostBloodstream.BloodSolution, out var hostBlood) &&
            hostBlood.Volume < hostBlood.MaxVolume)
            return true;

        return false;
    }
}
