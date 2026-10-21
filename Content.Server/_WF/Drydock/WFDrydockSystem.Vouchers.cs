using Content.Shared._WF.Drydock;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>A repair voucher in the actor's hands or worn slots, if any.</summary>
    private EntityUid? FindRepairVoucher(EntityUid actor)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(actor))
        {
            if (HasComp<WFDrydockRepairVoucherComponent>(item))
                return item;
        }

        return null;
    }

    /// <summary>Starts a repair paid by the actor's voucher, which is used up; false if the actor has none.</summary>
    private bool TryStartVoucherRepair(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship, EntityUid actor,
        List<WFDrydockWorkItem> items)
    {
        if (FindRepairVoucher(actor) is not { } voucher)
            return false;

        var returned = Comp<WFDrydockRepairVoucherComponent>(voucher).Returned?.Id ?? MetaData(voucher).EntityPrototype?.ID;
        QueueDel(voucher);
        StartJob(console, comp, ship, actor, items, 0, voucher: returned ?? string.Empty);
        return true;
    }

    /// <summary>Hands an aborted voucher job's voucher back to its payer.</summary>
    private void ReturnVoucher(WFDrydockJobComponent job)
    {
        if (job.Voucher is not { Length: > 0 } voucher || job.Payer is not { } payer || TerminatingOrDeleted(payer))
            return;

        var item = Spawn(voucher, Transform(payer).Coordinates);
        _hands.PickupOrDrop(payer, item);
    }
}
