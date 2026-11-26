using System.Linq;
using Content.Shared._WG.StockExchange;
using Content.Shared.Cargo.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Inventory;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.StockExchange;

public sealed partial class WGStockSystem
{
    [Dependency] private InventorySystem _inventory = default!;

    private static readonly ProtoId<StackPrototype> CashStack = "Credit";

    /// <summary>Cash the character carries, including inside bags and other containers.</summary>
    private IEnumerable<EntityUid> CashEntities(EntityUid actor)
    {
        var seen = new HashSet<EntityUid>();
        var pending = new Stack<EntityUid>(_inventory.GetHandOrInventoryEntities(actor));
        while (pending.Count > 0)
        {
            var uid = pending.Pop();
            if (!seen.Add(uid))
                continue;
            yield return uid;
            if (!TryComp<ContainerManagerComponent>(uid, out var containers))
                continue;
            foreach (var container in containers.Containers.Values)
            {
                foreach (var contained in container.ContainedEntities)
                    pending.Push(contained);
            }
        }
    }

    /// <summary>Cash the character carries, in hands, inventory slots or containers.</summary>
    private long CashAvailable(EntityUid actor)
    {
        long total = 0;
        foreach (var item in CashEntities(actor))
        {
            if (TerminatingOrDeleted(item) || !HasComp<CashComponent>(item) ||
                !TryComp<StackComponent>(item, out var stack) || stack.StackTypeId != CashStack)
                continue;
            total += stack.Count;
        }
        return total;
    }

    /// <summary>Takes up to the amount from carried cash, returning what was actually taken.</summary>
    private long TakeCash(EntityUid actor, long amount)
    {
        if (amount <= 0)
            return 0;

        long taken = 0;
        foreach (var item in CashEntities(actor).ToList())
        {
            if (taken >= amount || TerminatingOrDeleted(item) || !HasComp<CashComponent>(item) ||
                !TryComp<StackComponent>(item, out var stack) || stack.StackTypeId != CashStack)
                continue;

            var take = (int) Math.Min(stack.Count, amount - taken);
            if (take <= 0)
                continue;
            _stacks.SetCount(item, stack.Count - take, stack);
            taken += take;
        }
        return taken;
    }

    /// <summary>Whether cash plus, if the character has one, their faction or company can cover a purchase.</summary>
    private bool CanPayWithCashFirst(OwnerInfo? owner, EntityUid actor, long amount)
    {
        if (amount <= 0)
            return false;

        var cash = CashAvailable(actor);
        if (cash >= amount)
            return true;
        if (owner == null || owner.Frozen)
            return false;

        var funds = owner.Kind == WGStockOwnerKind.Company
            ? _companies.GetBalance(owner.CompanyId) ?? 0
            : owner.AccountComp.FactionBalance;
        return cash + funds >= amount;
    }

    /// <summary>How many units at this price the character's cash and, if they have one, account can cover.</summary>
    private long AffordableUnits(OwnerInfo? owner, EntityUid actor, int price)
    {
        if (price <= 0)
            return 0;
        var funds = CashAvailable(actor);
        if (owner is { Frozen: false })
        {
            funds += owner.Kind == WGStockOwnerKind.Company
                ? _companies.GetBalance(owner.CompanyId) ?? 0
                : owner.AccountComp.FactionBalance;
        }
        return funds / price;
    }

    /// <summary>Spends carried cash first, then the character's faction or company account.</summary>
    private bool PayWithCashFirst(OwnerInfo? owner, EntityUid actor, long amount, string note)
    {
        if (!CanPayWithCashFirst(owner, actor, amount))
            return false;

        var taken = TakeCash(actor, amount);
        var remaining = amount - taken;
        return remaining <= 0 || owner != null && Pay(owner, remaining, note);
    }

    /// <summary>Puts thalers into the character's hands.</summary>
    private void DispenseCash(EntityUid actor, int amount)
    {
        if (amount <= 0)
            return;
        var cash = _stacks.Spawn(amount, CashStack, Transform(actor).Coordinates);
        GiveTo(actor, cash);
    }

    /// <summary>Where a buyer with no account receives goods: a linked pad, another pad on the console's grid, or the console itself.</summary>
    private bool TryGetDeliveryPad(EntityUid terminal, out EntityUid pad)
    {
        pad = EntityUid.Invalid;
        if (TryComp<DeviceLinkSourceComponent>(terminal, out var source))
        {
            foreach (var sink in _deviceLink.GetLinkedSinks((terminal, source), WGStockTerminalComponent.DeliveryPort))
            {
                if (!HasComp<WGStockDeliveryPadComponent>(sink))
                    continue;
                pad = sink;
                return true;
            }
        }

        var grid = Transform(terminal).GridUid ?? Transform(terminal).MapUid;
        var pads = EntityQueryEnumerator<WGStockDeliveryPadComponent, TransformComponent>();
        while (pads.MoveNext(out var uid, out _, out var xform))
        {
            if ((xform.GridUid ?? xform.MapUid) != grid)
                continue;
            pad = uid;
            return true;
        }
        return false;
    }

    /// <summary>Spawns bought material on the attached pad immediately.</summary>
    private void DeliverToPad(EntityUid terminal, WGStockGood good, int sheets)
    {
        if (sheets <= 0 || !_protos.TryIndex<MaterialPrototype>(good.Id, out var material) ||
            material.StackEntity == null)
            return;

        var coordinates = TryGetDeliveryPad(terminal, out var pad)
            ? Transform(pad).Coordinates
            : Transform(terminal).Coordinates;
        _stacks.SpawnMultiple(material.StackEntity.Value, sheets, coordinates);
    }
}
