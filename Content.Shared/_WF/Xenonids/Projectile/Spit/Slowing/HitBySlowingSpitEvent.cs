// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared.Inventory;

namespace Content.Shared._WF.Xenonids.Projectile.Spit.Slowing;

[ByRefEvent]
public record struct HitBySlowingSpitEvent(SlotFlags TargetSlots, bool Cancelled = false) : IInventoryRelayEvent;
