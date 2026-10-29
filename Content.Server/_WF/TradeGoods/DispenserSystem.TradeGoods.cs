using Content.Server._Mono.VendingMachine;
using Content.Server.Popups;
using Content.Shared._Crescent.Dispenser;

namespace Content.Server._Crescent.Dispenser;

public sealed partial class DispenserSystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private VendingMachinePurchaseSystem _vendingPurchase = default!;

    /// <summary>
    /// Refuses goods bought from a vendor on the dispenser's own grid, so trade goods only pay once hauled elsewhere.
    /// </summary>
    private bool RefuseLocalGoods(EntityUid uid, DispenserComponent component, EntityUid used, EntityUid user)
    {
        if (Transform(uid).GridUid is not { } grid || !_vendingPurchase.WasPurchasedOnGrid(used, grid))
            return false;

        _audioSystem.PlayPvs(component.DenySound, uid);
        _popup.PopupEntity(Loc.GetString("wf-trade-chute-local-goods"), uid, user);
        return true;
    }

    /// <summary>Tells the user this chute doesn't buy what they offered; its manifest lists what it does.</summary>
    private void RefuseUnwantedGoods(EntityUid uid, EntityUid user)
    {
        _popup.PopupEntity(Loc.GetString("wf-trade-chute-not-bought"), uid, user);
    }
}
