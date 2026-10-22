using Content.Shared.Containers.ItemSlots;
using Content.Shared.Whitelist;

namespace Content.Shared._WF.SuitTank;

/// <summary>
/// Gives a suit a slot for a gas tank that feeds its wearer's internals and draws on the wearer.
/// </summary>
[RegisterComponent]
public sealed partial class WFSuitTankSlotComponent : Component
{
    public const string SlotId = "wf_suit_tank";

    [DataField]
    public ItemSlot Slot = new()
    {
        Name = "wf-suit-tank-slot-name",
        Whitelist = new EntityWhitelist { Components = new[] { "GasTank" } },
    };
}
