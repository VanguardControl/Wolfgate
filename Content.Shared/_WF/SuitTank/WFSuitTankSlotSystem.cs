using System.Diagnostics.CodeAnalysis;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Components;
using Robust.Shared.Containers;

namespace Content.Shared._WF.SuitTank;

/// <summary>
/// Manages the gas tank slot in suits and finds which mob a slotted tank belongs to.
/// </summary>
public sealed class WFSuitTankSlotSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedGasTankSystem _gasTank = default!;
    [Dependency] private SharedItemSystem _item = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFSuitTankSlotComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WFSuitTankSlotComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<WFSuitTankSlotComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<WFSuitTankSlotComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<WFSuitTankSlotComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<WFSuitTankSlotComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
    }

    private void OnInsertAttempt(Entity<WFSuitTankSlotComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        // Jetpacks are gas tanks too, but only fly from the back or suit storage.
        if (args.Slot == ent.Comp.Slot && HasComp<JetpackComponent>(args.Item))
            args.Cancelled = true;
    }

    private void OnInit(Entity<WFSuitTankSlotComponent> ent, ref ComponentInit args)
    {
        _itemSlots.AddItemSlot(ent, WFSuitTankSlotComponent.SlotId, ent.Comp.Slot);
    }

    private void OnRemove(Entity<WFSuitTankSlotComponent> ent, ref ComponentRemove args)
    {
        _itemSlots.RemoveItemSlot(ent, ent.Comp.Slot);
    }

    private void OnInserted(Entity<WFSuitTankSlotComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == WFSuitTankSlotComponent.SlotId)
            _item.VisualsChanged(ent);
    }

    private void OnRemoved(Entity<WFSuitTankSlotComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == WFSuitTankSlotComponent.SlotId)
            _item.VisualsChanged(ent);
    }

    private void OnUnequipped(Entity<WFSuitTankSlotComponent> ent, ref GotUnequippedEvent args)
    {
        // The tank never changes parent here, so the gas tank system's own check would miss it.
        if (ent.Comp.Slot.Item is { } tank
            && TryComp<GasTankComponent>(tank, out var gas)
            && gas.User == args.Equipee)
        {
            _gasTank.DisconnectFromInternals((tank, gas), forced: true);
        }
    }

    /// <summary>
    /// Gets the tank in the slot of the suit worn in the outer clothing slot.
    /// </summary>
    public bool TryGetWornTank(EntityUid wearer, out Entity<GasTankComponent> tank)
    {
        tank = default;
        if (!_inventory.TryGetSlotEntity(wearer, "outerClothing", out var suit)
            || !TryComp<WFSuitTankSlotComponent>(suit, out var slot)
            || slot.Slot.Item is not { } item
            || !TryComp<GasTankComponent>(item, out var gas))
        {
            return false;
        }

        tank = (item, gas);
        return true;
    }

    /// <summary>
    /// Gets the mob wearing the suit that holds this tank in its slot.
    /// </summary>
    public bool TryGetWearer(EntityUid tank, [NotNullWhen(true)] out EntityUid? wearer)
    {
        wearer = null;
        if (!_container.TryGetContainingContainer((tank, null, null), out var container)
            || container.ID != WFSuitTankSlotComponent.SlotId
            || !HasComp<WFSuitTankSlotComponent>(container.Owner)
            || !_inventory.TryGetContainingSlot(container.Owner, out var slot)
            || (slot.SlotFlags & SlotFlags.OUTERCLOTHING) == 0)
        {
            return false;
        }

        wearer = Transform(container.Owner).ParentUid;
        return true;
    }
}
