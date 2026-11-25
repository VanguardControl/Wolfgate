using Content.Shared._WG.Proteans;

namespace Content.Shared.Inventory;

public abstract partial class InventorySystem
{
    /// <summary>
    /// Shows a refused equip above the target, to the actor, when the refusal is a Protean turning down a hardsuit,
    /// and returns true. Otherwise returns false and the caller shows its usual cursor popup.
    /// </summary>
    public bool WgEquipRefusalOnMob(string reason, EntityUid actor, EntityUid target)
    {
        if (!TryComp(target, out WGProteanComponent? protean) || reason != protean.HardsuitRefuse)
            return false;

        _popup.PopupClient(Loc.GetString(reason), target, actor);
        return true;
    }
}
