using System.Linq;
using Content.Shared._WG.Companies;
using Content.Shared._WG.StockExchange;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.StockExchange;

public sealed partial class WGStockSystem
{
    /// <summary>Handles the terminal's window: every action acts for the sender's account.</summary>
    private void InitializeUi()
    {
        SubscribeLocalEvent<WGStockTerminalComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<WGStockTerminalComponent, WGStockRequest>(OnRequest);
    }

    private void OnOpened(Entity<WGStockTerminalComponent> ent, ref BoundUIOpenedEvent args) =>
        SendState(ent, args.Actor);

    private void OnRequest(Entity<WGStockTerminalComponent> ent, ref WGStockRequest args)
    {
        // Quick buys and sells work for anyone; limit orders and storage need a faction or company account.
        var owner = ResolveOwner(args.Actor, out var ownerError);
        var market = EnsureMarket(ent, ent.Comp);
        string? error = null;
        string? success = null;
        switch (args.Action)
        {
            case WGStockAction.Refresh:
                break;
            case WGStockAction.BuyNow:
                BuyNow(owner, args.Actor, ent.Owner, market, new WGStockGood(args.Good), args.Quantity, out error);
                break;
            case WGStockAction.SellNow:
                SellNow(owner, args.Actor, market, new WGStockGood(args.Good), args.Quantity, out error);
                break;
            case WGStockAction.SellHeld:
                SellHeld(args.Actor, owner, market, args.Quantity > 0 ? args.Quantity : null, null, out error);
                break;
            case WGStockAction.PlaceBuy:
                if (RequireOwner(owner, ownerError) is { } missingBuy)
                {
                    error = missingBuy;
                    break;
                }
                PlaceBuy(owner!, args.Actor, market, new WGStockGood(args.Good), args.Price, args.Quantity, out error);
                break;
            case WGStockAction.PlaceSell:
                if (RequireOwner(owner, ownerError) is { } missingSell)
                {
                    error = missingSell;
                    break;
                }
                PlaceSell(owner!, market, new WGStockGood(args.Good), args.Price, args.Quantity, out error);
                break;
            case WGStockAction.CancelOrder:
                if (RequireOwner(owner, ownerError) is { } missingCancel)
                {
                    error = missingCancel;
                    break;
                }
                CancelOrder(owner!, market, args.OrderId, out error);
                break;
            case WGStockAction.DepositFunds:
                if (RequireOwner(owner, ownerError) is { } missingDeposit)
                {
                    error = missingDeposit;
                    break;
                }
                DepositFunds(args.Actor, owner!, args.Amount, out error);
                break;
            case WGStockAction.WithdrawFunds:
                if (RequireOwner(owner, ownerError) is { } missingWithdraw)
                {
                    error = missingWithdraw;
                    break;
                }
                WithdrawFunds(args.Actor, owner!, args.Amount, out error);
                break;
            case WGStockAction.BuySlots:
                if (RequireOwner(owner, ownerError) is { } missingSlots)
                {
                    error = missingSlots;
                    break;
                }
                BuySlots(args.Actor, owner!, args.Amount, out error);
                break;
            case WGStockAction.StorePersistent:
                if (RequireOwner(owner, ownerError) is { } missingStore)
                {
                    error = missingStore;
                    break;
                }
                StorePersistent(owner!, args.MaterialId, args.Sheets, out error);
                break;
            case WGStockAction.WithdrawPersistent:
                if (RequireOwner(owner, ownerError) is { } missingPersistent)
                {
                    error = missingPersistent;
                    break;
                }
                WithdrawPersistent(owner!, args.Actor, args.MaterialId, args.Sheets, out error);
                break;
            case WGStockAction.WithdrawRound:
                if (RequireOwner(owner, ownerError) is { } missingRound)
                {
                    error = missingRound;
                    break;
                }
                WithdrawRound(args.Actor, owner!, args.MaterialId, args.Sheets, out error);
                break;
            case WGStockAction.DepositHeld:
                if (RequireOwner(owner, ownerError) is { } missingHeld)
                {
                    error = missingHeld;
                    break;
                }
                DepositHeld(args.Actor, owner!.AccountComp, out error);
                break;
            default:
                error = "wg-stock-error-action";
                break;
        }

        if (error == null && args.Action != WGStockAction.Refresh)
            success = "wg-stock-done";
        SendState(ent, args.Actor, error ?? success);
    }

    /// <summary>The error to report when an action needs a faction or company but the actor has none.</summary>
    private static string? RequireOwner(OwnerInfo? owner, string? ownerError) =>
        owner == null ? ownerError ?? "wg-stock-error-no-owner" : null;

    /// <summary>Buys persistent silo space for the acting character's company.</summary>
    private void BuySlots(EntityUid actor, OwnerInfo owner, int sheets, out string? error)
    {
        error = null;
        if (owner.Kind != WGStockOwnerKind.Company)
        {
            error = "wg-stock-error-no-persistent";
            return;
        }
        if (!_players.TryGetSessionByEntity(actor, out var session) || !_companies.StockBuySiloSlots(session, sheets, out error))
            return;
        if (_companies.StockCompany(session) is { } company)
            RefreshPersistent(owner, company);
    }

    private void WithdrawRound(EntityUid actor, OwnerInfo owner, string material, int sheets, out string? error)
    {
        error = null;
        if (!TakeMaterial(owner.AccountComp, material, sheets, false))
        {
            error = "wg-stock-error-stock";
            return;
        }
        GiveMaterial(actor, material, sheets);
    }

    private void SendState(EntityUid terminal, EntityUid actor, string? message = null)
    {
        var state = State(terminal, actor, message);
        _ui.ServerSendUiMessage(terminal, WGStockUiKey.Key, new WGStockStateMessage(state), actor);
    }

    /// <summary>The state one viewer sees in one terminal.</summary>
    private WGStockState State(EntityUid terminal, EntityUid actor, string? message)
    {
        var term = Comp<WGStockTerminalComponent>(terminal);
        var market = EnsureMarket(terminal, term);
        var state = new WGStockState
        {
            Mode = term.Mode,
            Message = message == null ? null : Loc.GetString(message),
            SlotCost = (int) WGStockPolicy.PersistentSlotCost,
            Goods = Goods(market),
            Orders = market.Comp.Orders.Select(order => OrderState(order, default)).ToArray(),
        };

        if (ResolveOwner(actor, out _) is not { } owner)
            return state;

        if (owner.Kind == WGStockOwnerKind.Company &&
            _players.TryGetSessionByEntity(actor, out var session) &&
            _companies.StockCompany(session) is { } company)
        {
            RefreshPersistent(owner, company);
            owner = owner with { Balance = company.Balance, Frozen = company.Frozen, Permissions = company.Permissions };
        }

        state.HasOwner = true;
        state.Kind = owner.Kind;
        state.OwnerName = owner.Name;
        state.OwnerTag = owner.Tag;
        state.Balance = owner.Kind == WGStockOwnerKind.Company
            ? _companies.GetBalance(owner.CompanyId) ?? owner.Balance
            : owner.AccountComp.FactionBalance;
        state.PersonalBalance = _players.TryGetSessionByEntity(actor, out var playerSession) &&
                                _bank.TryGetBalance(playerSession, out var balance)
            ? balance
            : 0;
        state.Frozen = owner.Frozen;
        state.PersistentCapacity = owner.AccountComp.PersistentCapacity;
        state.PersistentUsed = owner.AccountComp.PersistentMaterials.Values.Sum();
        state.CanWithdrawSilo = owner.Kind == WGStockOwnerKind.Company &&
                                (owner.Permissions & WGCompanyPermissions.SiloWithdraw) != 0;
        state.CanBuySlots = owner.Kind == WGStockOwnerKind.Company &&
                            (owner.Permissions & WGCompanyPermissions.EditDetails) != 0;
        state.RoundMaterials = owner.AccountComp.RoundMaterials.OrderBy(pair => pair.Key)
            .Select(pair => MaterialState(pair.Key, pair.Value)).ToArray();
        state.PersistentMaterials = owner.AccountComp.PersistentMaterials.OrderBy(pair => pair.Key)
            .Select(pair => MaterialState(pair.Key, pair.Value)).ToArray();
        state.Orders = market.Comp.Orders.Select(order => OrderState(order, owner.Account)).ToArray();
        return state;
    }

    private WGStockOrderState OrderState(WGStockOrder order, EntityUid viewer) => new()
    {
        Id = order.Id,
        Good = order.Good.Id,
        Name = GoodName(order.Good),
        Side = order.Side,
        Price = order.Price,
        Quantity = order.Quantity,
        Owner = Comp<WGStockAccountComponent>(order.Account).Name,
        Mine = viewer.IsValid() && order.Account == viewer,
    };

    private WGStockMaterialState MaterialState(string id, int sheets) => new()
    {
        Id = id,
        Name = _protos.TryIndex<Content.Shared.Materials.MaterialPrototype>(id, out var material)
            ? Loc.GetString(material.Name)
            : id,
        Sheets = sheets,
    };

}
