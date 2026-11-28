using System.Linq;
using Content.Shared._WG.StockExchange;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.Server._WG.StockExchange;

public sealed partial class WGStockSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;

    private void InitializeInteraction()
    {
        SubscribeLocalEvent<WGStockTerminalComponent, InteractUsingEvent>(OnTerminalUsing);
    }

    /// <summary>Sells one material entity to resting bids, removing only the part that finds a buyer.</summary>
    private bool SellEntity(EntityUid actor, OwnerInfo? owner, Entity<WGStockMarketComponent> market, EntityUid entity,
        out string? error)
    {
        error = null;
        if (!HasComp<MaterialComponent>(entity) || MaterialId(entity) is not { } material)
        {
            error = "wg-stock-error-not-material";
            return false;
        }

        var good = new WGStockGood(material);
        var quantity = SheetsIn(entity, EntityManager);
        if (quantity <= 0 || !IsListed(market.Comp, good))
        {
            error = "wg-stock-error-not-listed";
            return false;
        }

        var available = BidQuantity(market, good);
        if (available <= 0)
        {
            error = "wg-stock-error-no-bids";
            return false;
        }

        var sold = Math.Min(quantity, available);
        var unsold = SellToBids(owner, actor, market, good, sold);
        sold -= unsold;
        if (sold <= 0)
        {
            error = "wg-stock-error-no-bids";
            return false;
        }

        if (sold < quantity)
        {
            if (TryComp<StackComponent>(entity, out var stack))
                _stacks.SetCount(entity, stack.Count - EntityUnits(entity, sold), stack);
            else
                Del(entity);
        }
        else
        {
            Del(entity);
        }
        return true;
    }

    /// <summary>A material silo takes what is used on it into the account's round stock.</summary>
    private void OnTerminalUsing(Entity<WGStockTerminalComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Mode == WGStockTerminalMode.Market)
            return;
        var owner = ResolveOwner(args.User, out var error);
        if (owner == null)
        {
            _popup.PopupEntity(Loc.GetString(error!), ent, args.User);
            return;
        }
        if (!DepositEntity(owner.AccountComp, args.Used, out error))
        {
            _popup.PopupEntity(Loc.GetString(error!), ent, args.User);
            return;
        }
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("wg-stock-deposited"), ent, args.User);
    }
}
