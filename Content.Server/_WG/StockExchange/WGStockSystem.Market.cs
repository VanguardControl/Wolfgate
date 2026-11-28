using System.Linq;
using Content.Shared._WG.StockExchange;
using Content.Shared.Materials;

namespace Content.Server._WG.StockExchange;

public sealed partial class WGStockSystem
{
    /// <summary>Places a limit bid, holding its money until it trades or is cancelled.</summary>
    private bool PlaceBuy(OwnerInfo owner, EntityUid actor, Entity<WGStockMarketComponent> market, WGStockGood good,
        int price, int quantity, out string? error)
    {
        error = null;
        if (owner.Frozen)
        {
            error = "wg-stock-error-frozen";
            return false;
        }
        if (!WGStockPolicy.ValidPrice(price) || !WGStockPolicy.ValidQuantity(quantity) || !IsListed(market.Comp, good))
        {
            error = "wg-stock-error-order";
            return false;
        }
        if (OpenOrders(market, owner.Account) >= WGStockPolicy.MaximumOpenOrders)
        {
            error = "wg-stock-error-orders";
            return false;
        }
        var escrow = (long) price * quantity;
        if (PaymentError(owner, actor, escrow) is { } paymentError)
        {
            error = paymentError;
            return false;
        }
        if (!PayWithCashFirst(owner, actor, escrow, $"bid {quantity} {GoodName(good)} @ {price}"))
        {
            error = "wg-stock-error-failed";
            return false;
        }
        AddOrder(market, owner.Account, good, WGStockOrderSide.Buy, price, quantity);
        Match(market, good);
        return true;
    }

    /// <summary>Places a limit ask, holding its goods until it trades or is cancelled.</summary>
    private bool PlaceSell(OwnerInfo owner, Entity<WGStockMarketComponent> market, WGStockGood good, int price, int quantity,
        out string? error)
    {
        error = null;
        if (owner.Frozen)
        {
            error = "wg-stock-error-frozen";
            return false;
        }
        if (!WGStockPolicy.ValidPrice(price) || !WGStockPolicy.ValidQuantity(quantity) || !IsListed(market.Comp, good))
        {
            error = "wg-stock-error-order";
            return false;
        }
        if (OpenOrders(market, owner.Account) >= WGStockPolicy.MaximumOpenOrders)
        {
            error = "wg-stock-error-orders";
            return false;
        }
        if (!TakeGood(owner.AccountComp, good, quantity))
        {
            error = "wg-stock-error-stock";
            return false;
        }
        AddOrder(market, owner.Account, good, WGStockOrderSide.Sell, price, quantity);
        Match(market, good);
        return true;
    }

    /// <summary>Buys from the cheapest asks now. Without an account, the goods go to the attached pad.</summary>
    private bool BuyNow(OwnerInfo? owner, EntityUid actor, EntityUid terminal, Entity<WGStockMarketComponent> market,
        WGStockGood good, int quantity, out string? error)
    {
        error = null;
        if (owner is { Frozen: true })
        {
            error = "wg-stock-error-frozen";
            return false;
        }
        if (!WGStockPolicy.ValidQuantity(quantity) || !IsListed(market.Comp, good))
        {
            error = "wg-stock-error-order";
            return false;
        }

        var remaining = quantity;
        while (remaining > 0)
        {
            if (BestSell(market, good, owner?.Account) is not { } sell)
                break;
            var take = (int) Math.Min(Math.Min(remaining, sell.Quantity), AffordableUnits(owner, actor, sell.Price));
            if (take <= 0)
            {
                if (remaining == quantity)
                {
                    error = "wg-stock-error-balance";
                    return false;
                }
                break;
            }
            var cost = (long) sell.Price * take;
            if (PaymentError(owner, actor, cost) is { } paymentError)
            {
                if (remaining == quantity)
                {
                    error = paymentError;
                    return false;
                }
                break;
            }
            if (!PayWithCashFirst(owner, actor, cost, $"buy {take} {GoodName(good)} @ {sell.Price}"))
                break;
            var seller = OwnerFromAccount(sell.Account, Comp<WGStockAccountComponent>(sell.Account));
            Receive(seller, cost, $"sale {take} {GoodName(good)} @ {sell.Price}");
            if (owner != null)
                Deliver(owner.Account, good, take);
            else
                DeliverToPad(terminal, good, take);
            if (owner != null)
                NotifyTrade(owner.Account, true, good, take, sell.Price);
            NotifyTrade(sell.Account, false, good, take, sell.Price);
            Reduce(market, sell, take);
            remaining -= take;
        }

        if (remaining == quantity)
        {
            error = "wg-stock-error-no-asks";
            return false;
        }
        return true;
    }

    /// <summary>Sells from the account's round stock, or from the hand when there is no account.</summary>
    private bool SellNow(OwnerInfo? owner, EntityUid actor, Entity<WGStockMarketComponent> market, WGStockGood good,
        int quantity, out string? error)
    {
        error = null;
        if (owner is { Frozen: true })
        {
            error = "wg-stock-error-frozen";
            return false;
        }
        if (!WGStockPolicy.ValidQuantity(quantity) || !IsListed(market.Comp, good))
        {
            error = "wg-stock-error-order";
            return false;
        }
        if (owner == null)
            return SellHeld(actor, null, market, quantity, good, out error);

        if (!TakeGood(owner.AccountComp, good, quantity))
        {
            error = "wg-stock-error-stock";
            return false;
        }

        var remaining = SellToBids(owner, actor, market, good, quantity);
        if (remaining > 0)
        {
            AddGood(owner.AccountComp, good, remaining);
            if (remaining == quantity)
            {
                error = "wg-stock-error-no-bids";
                return false;
            }
        }
        return true;
    }

    /// <summary>Sells what the acting character holds to the best bids.</summary>
    private bool SellHeld(EntityUid actor, OwnerInfo? owner, Entity<WGStockMarketComponent> market, int? requested,
        WGStockGood? onlyGood, out string? error)
    {
        error = null;
        if (!_hands.TryGetActiveItem(actor, out var held))
        {
            error = "wg-stock-error-empty-hand";
            return false;
        }

        if (!HasComp<MaterialComponent>(held.Value) || MaterialId(held.Value) is not { } material)
        {
            error = "wg-stock-error-not-material";
            return false;
        }

        var good = new WGStockGood(material);
        if (onlyGood is { } wanted && good != wanted)
        {
            error = "wg-stock-error-not-listed";
            return false;
        }

        var quantity = SheetsIn(held.Value, EntityManager);
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

        var want = requested is > 0 ? Math.Min(quantity, requested.Value) : quantity;
        var sold = Math.Min(want, available);
        var remaining = SellToBids(owner, actor, market, good, sold);
        sold -= remaining;
        if (sold <= 0)
        {
            error = "wg-stock-error-no-bids";
            return false;
        }

        if (sold < quantity)
        {
            if (TryComp<Content.Shared.Stacks.StackComponent>(held.Value, out var stack))
                _stacks.SetCount(held.Value, stack.Count - EntityUnits(held.Value, sold), stack);
            else
                Del(held.Value);
        }
        else
        {
            Del(held.Value);
        }
        return true;
    }

    /// <summary>The entities to remove for a number of sold sheets, as many as the material composition needs.</summary>
    private int EntityUnits(EntityUid uid, int sheets)
    {
        if (!TryComp<PhysicalCompositionComponent>(uid, out var composition))
            return sheets;
        var perEntity = composition.MaterialComposition.Values.Sum();
        if (perEntity <= 0)
            return sheets;
        return Math.Max(1, (int) Math.Ceiling((double) sheets * WGStockPolicy.UnitsPerSheet / perEntity));
    }

    /// <summary>Sells goods to resting bids, returning how much no bid would take. Without an account, pays cash.</summary>
    private int SellToBids(OwnerInfo? seller, EntityUid actor, Entity<WGStockMarketComponent> market, WGStockGood good,
        int quantity)
    {
        var remaining = quantity;
        while (remaining > 0)
        {
            if (BestBuy(market, good, seller?.Account) is not { } buy)
                break;
            var take = Math.Min(remaining, buy.Quantity);
            var proceeds = (long) buy.Price * take;
            // The buyer already escrowed at its limit, which is the price used here.
            if (seller != null)
                Receive(seller, proceeds, $"sale {take} {GoodName(good)} @ {buy.Price}");
            else
                DispenseCash(actor, (int) proceeds);
            var buyer = OwnerFromAccount(buy.Account, Comp<WGStockAccountComponent>(buy.Account));
            Deliver(buyer.Account, good, take);
            if (seller != null)
                NotifyTrade(seller.Account, false, good, take, buy.Price);
            NotifyTrade(buyer.Account, true, good, take, buy.Price);
            Reduce(market, buy, take);
            remaining -= take;
        }
        return remaining;
    }

    /// <summary>Matches crossing orders in price then time order.</summary>
    private void Match(Entity<WGStockMarketComponent> market, WGStockGood good)
    {
        while (true)
        {
            if (BestSell(market, good, null) is not { } sell)
                break;
            if (BestBuy(market, good, sell.Account) is not { } buy || buy.Price < sell.Price)
                break;

            var take = Math.Min(buy.Quantity, sell.Quantity);
            var price = sell.Price;
            var cost = (long) price * take;
            var refund = (long) (buy.Price - price) * take;
            var buyer = OwnerFromAccount(buy.Account, Comp<WGStockAccountComponent>(buy.Account));
            var seller = OwnerFromAccount(sell.Account, Comp<WGStockAccountComponent>(sell.Account));
            if (refund > 0)
                Receive(buyer, refund, "order refund");
            Receive(seller, cost, $"sale {take} {GoodName(good)} @ {price}");
            Deliver(buyer.Account, good, take);
            NotifyTrade(buyer.Account, true, good, take, price);
            NotifyTrade(seller.Account, false, good, take, price);
            Reduce(market, buy, take);
            Reduce(market, sell, take);
        }
    }

    /// <summary>Cancels one of the account's orders, returning its held money or goods.</summary>
    private bool CancelOrder(OwnerInfo owner, Entity<WGStockMarketComponent> market, int orderId, out string? error)
    {
        error = null;
        var order = market.Comp.Orders.FirstOrDefault(candidate => candidate.Id == orderId && candidate.Account == owner.Account);
        if (order == null)
        {
            error = "wg-stock-error-order";
            return false;
        }

        if (order.Side == WGStockOrderSide.Buy)
            Receive(owner, (long) order.Price * order.Quantity, "order cancelled");
        else
            AddGood(owner.AccountComp, order.Good, order.Quantity);

        market.Comp.Orders.Remove(order);
        owner.AccountComp.Orders.Remove(order.Id);
        return true;
    }

    private int OpenOrders(Entity<WGStockMarketComponent> market, EntityUid account) =>
        market.Comp.Orders.Count(order => order.Account == account);

    private void AddOrder(Entity<WGStockMarketComponent> market, EntityUid account, WGStockGood good, WGStockOrderSide side,
        int price, int quantity)
    {
        var order = new WGStockOrder
        {
            Id = market.Comp.NextOrderId++,
            Account = account,
            Good = good,
            Side = side,
            Price = price,
            Quantity = quantity,
            Created = _timing.CurTime,
        };
        market.Comp.Orders.Add(order);
        Comp<WGStockAccountComponent>(account).Orders.Add(order.Id);
    }

    private void Reduce(Entity<WGStockMarketComponent> market, WGStockOrder order, int quantity)
    {
        order.Quantity -= quantity;
        if (order.Quantity > 0)
            return;
        market.Comp.Orders.Remove(order);
        Comp<WGStockAccountComponent>(order.Account).Orders.Remove(order.Id);
    }

    private static WGStockOrder? BestBuy(Entity<WGStockMarketComponent> market, WGStockGood good, EntityUid? exclude) =>
        market.Comp.Orders
            .Where(order => order.Side == WGStockOrderSide.Buy && order.Good == good && order.Quantity > 0 &&
                            (exclude == null || order.Account != exclude))
            .OrderByDescending(order => order.Price)
            .ThenBy(order => order.Created)
            .FirstOrDefault();

    private static WGStockOrder? BestSell(Entity<WGStockMarketComponent> market, WGStockGood good, EntityUid? exclude) =>
        market.Comp.Orders
            .Where(order => order.Side == WGStockOrderSide.Sell && order.Good == good && order.Quantity > 0 &&
                            (exclude == null || order.Account != exclude))
            .OrderBy(order => order.Price)
            .ThenBy(order => order.Created)
            .FirstOrDefault();

    private static int BidQuantity(Entity<WGStockMarketComponent> market, WGStockGood good) =>
        market.Comp.Orders.Where(order => order.Side == WGStockOrderSide.Buy && order.Good == good)
            .Sum(order => order.Quantity);

    private static bool TakeGood(WGStockAccountComponent account, WGStockGood good, int quantity) =>
        TakeMaterial(account, good.Id, quantity, false);

    private static void AddGood(WGStockAccountComponent account, WGStockGood good, int quantity) =>
        AddMaterial(account, good.Id, quantity, false);

    /// <summary>Every good a market lists with its best quotes.</summary>
    private WGStockGoodState[] Goods(Entity<WGStockMarketComponent> market)
    {
        if (!_protos.TryIndex(market.Comp.Profile, out var profile))
            return Array.Empty<WGStockGoodState>();

        var goods = new List<WGStockGoodState>();
        foreach (var material in profile.Materials)
            goods.Add(GoodState(market, new WGStockGood(material.Id)));
        return goods.ToArray();
    }

    private WGStockGoodState GoodState(Entity<WGStockMarketComponent> market, WGStockGood good)
    {
        var bids = market.Comp.Orders.Where(order => order.Side == WGStockOrderSide.Buy && order.Good == good).ToList();
        var asks = market.Comp.Orders.Where(order => order.Side == WGStockOrderSide.Sell && order.Good == good).ToList();
        return new WGStockGoodState
        {
            Id = good.Id,
            Name = GoodName(good),
            BasePrice = BasePrice(good),
            BestBid = bids.Count == 0 ? 0 : bids.Max(order => order.Price),
            BestAsk = asks.Count == 0 ? 0 : asks.Min(order => order.Price),
            BidQuantity = bids.Sum(order => order.Quantity),
            AskQuantity = asks.Sum(order => order.Quantity),
        };
    }
}
