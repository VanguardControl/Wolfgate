using Content.Server._WF.Encounters.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Materials;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Chop-shop consoles: a break hands over a share of the yield as sheets at the console and pays the rest in cash, and
/// breaking lawful property posts a warrant on the breaker.
/// </summary>
public sealed partial class WFDrydockSystem
{
    [Dependency] private WFShipbreakerWarrantSystem _warrants = default!;

    /// <summary>Splits a break yield into the sheets a chop shop hands over and the cash it pays for the rest.</summary>
    private (Dictionary<string, int> Sheets, int Cash) SplitChopYield(WFDrydockChopShopComponent chop,
        Dictionary<string, int> yield, int tiles)
    {
        var sheets = new Dictionary<string, int>();
        var cash = (double) chop.TileCash * tiles;
        foreach (var (id, units) in yield)
        {
            var kept = (int) (units * chop.MaterialShare);
            if (kept > 0)
                sheets[id] = kept;

            if (_prototype.TryIndex<MaterialPrototype>(id, out var material))
                cash += (units - kept) * material.Price * chop.CashRate;
        }

        return (sheets, (int) cash);
    }

    /// <summary>Hands over a finished or aborted chop-shop break's sheets at the console and pays its cash to the breaker.</summary>
    private (Dictionary<string, int> Sheets, int Cash) PayChopShop(EntityUid console, WFDrydockChopShopComponent chop,
        WFDrydockJobComponent job)
    {
        var (sheets, cash) = SplitChopYield(chop, job.Yield, job.Done - job.Skipped);
        var drop = DropSpot(console);
        foreach (var (id, amount) in sheets)
        {
            _sheets.SpawnMultipleFromMaterial(amount, id, drop);
        }

        if (cash > 0 && (job.Payer is not { } payer || TerminatingOrDeleted(payer) || !_bank.TryBankDeposit(payer, cash, false)))
            Log.Warning($"Could not pay {cash} for a chop-shop break at {ToPrettyString(console)} to {ToPrettyString(job.Payer)}.");

        return (sheets, cash);
    }

    /// <summary>Hull tiles a grid has.</summary>
    private int TileCount(EntityUid ship)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return 0;

        var count = 0;
        var tiles = _map.GetAllTilesEnumerator(ship, grid);
        while (tiles.MoveNext(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Whether breaking a ship is a crime: it carries a deed in someone else's name, or it belongs to a live encounter
    /// of a lawful faction. Derelicts, outlaws' ships and ships freed from their encounter are anyone's.
    /// </summary>
    public bool IsLawfulProperty(WFDrydockChopShopComponent chop, EntityUid ship, EntityUid? breaker)
    {
        if (TryComp<ShuttleDeedComponent>(ship, out var deed))
            return breaker == null || deed.ShuttleOwner != Name(breaker.Value);

        if (!TryComp<WFEncounterGridComponent>(ship, out var member) ||
            !TryComp<WFEncounterComponent>(member.Encounter, out var encounter) ||
            !_prototype.TryIndex(encounter.Prototype, out var prototype))
            return false;

        if (encounter.Ships.TryGetValue(member.Key, out var state) && state.Derelict)
            return false;

        var entry = prototype.Ships.Find(s => s.Key == member.Key);
        return entry != null && !chop.OutlawFactions.Contains(entry.Faction);
    }

    /// <summary>Posts a warrant on whoever started breaking lawful property at a chop shop.</summary>
    private void PostBreakWarrant(EntityUid console, WFDrydockChopShopComponent chop, EntityUid ship, EntityUid breaker)
    {
        if (chop.WarrantReward <= 0)
            return;

        var shipName = Name(ship);
        _warrants.Issue(breaker, shipName, chop.WarrantReward,
            Loc.GetString("drydock-chop-warrant-description", ("name", Name(breaker)), ("ship", shipName)),
            Loc.GetString("drydock-chop-warrant-author"));

        Log.Info($"{ToPrettyString(breaker)} began breaking lawful ship {ToPrettyString(ship)} at {ToPrettyString(console)}.");
        _popup.PopupEntity(Loc.GetString("drydock-chop-warrant-posted"), breaker, breaker, Shared.Popups.PopupType.LargeCaution);
    }
}
