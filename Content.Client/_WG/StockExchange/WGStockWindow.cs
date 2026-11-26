using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.StockExchange;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.StockExchange;

/// <summary>The exchange and material silo window.</summary>
public sealed class WGStockWindow : FancyWindow
{
    private readonly Action<WGStockRequest> _send;
    private readonly Label _status;
    private readonly BoxContainer _marketTab;
    private readonly BoxContainer _storageTab;
    private readonly BoxContainer _goods;
    private readonly BoxContainer _details;
    private readonly BoxContainer _orders;
    private readonly BoxContainer _storage;
    private readonly LineEdit _price;
    private readonly LineEdit _quantity;
    private LineEdit _funds = null!;
    private readonly Button _buy;
    private readonly Button _sell;
    private readonly Button _buyNow;
    private readonly Button _sellNow;
    private readonly Button _sellHeld;
    private readonly Button _marketTabButton;
    private readonly Button _storageTabButton;
    private Button _depositFunds = null!;
    private Button _withdrawFunds = null!;
    private readonly Button _depositHeld;
    private WGStockState? _state;
    private string? _selected;

    public WGStockWindow(Action<WGStockRequest> send)
    {
        _send = send;
        Title = Loc.GetString("wg-stock-title");
        SetSize = new Vector2(940, 640);
        MinSize = new Vector2(760, 480);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6) };
        ContentsContainer.AddChild(root);

        _status = new Label { Margin = new Thickness(0, 0, 0, 6) };
        root.AddChild(_status);

        var tabs = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        root.AddChild(tabs);
        _marketTabButton = new Button { Text = Loc.GetString("wg-stock-tab-market"), ToggleMode = true, Pressed = true };
        _storageTabButton = new Button { Text = Loc.GetString("wg-stock-tab-storage"), ToggleMode = true };
        tabs.AddChild(_marketTabButton);
        tabs.AddChild(_storageTabButton);

        _marketTab = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, VerticalExpand = true };
        root.AddChild(_marketTab);
        _storageTab = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true, Visible = false };
        root.AddChild(_storageTab);

        var goodsScroll = new ScrollContainer { HScrollEnabled = false, MinWidth = 320 };
        _goods = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        goodsScroll.AddChild(_goods);
        _marketTab.AddChild(goodsScroll);

        var right = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(6, 0, 0, 0),
        };
        _marketTab.AddChild(right);

        _details = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        right.AddChild(_details);

        var trade = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(0, 4) };
        right.AddChild(trade);
        var tradeRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        _price = new LineEdit { PlaceHolder = Loc.GetString("wg-stock-price"), MinWidth = 100 };
        _quantity = new LineEdit { PlaceHolder = Loc.GetString("wg-stock-quantity"), MinWidth = 100 };
        tradeRow.AddChild(_price);
        tradeRow.AddChild(_quantity);
        trade.AddChild(tradeRow);

        _buy = new Button { Text = Loc.GetString("wg-stock-place-buy") };
        _sell = new Button { Text = Loc.GetString("wg-stock-place-sell") };
        _buyNow = new Button { Text = Loc.GetString("wg-stock-buy-now") };
        _sellNow = new Button { Text = Loc.GetString("wg-stock-sell-now") };
        _sellHeld = new Button { Text = Loc.GetString("wg-stock-sell-held") };
        _buy.OnPressed += _ => Trade(false, false);
        _sell.OnPressed += _ => Trade(true, false);
        _buyNow.OnPressed += _ => Trade(false, true);
        _sellNow.OnPressed += _ => Trade(true, true);
        _sellHeld.OnPressed += _ => _send(new WGStockRequest(WGStockAction.SellHeld));
        var tradeButtons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        tradeButtons.AddChild(_buy);
        tradeButtons.AddChild(_sell);
        tradeButtons.AddChild(_buyNow);
        tradeButtons.AddChild(_sellNow);
        tradeButtons.AddChild(_sellHeld);
        trade.AddChild(tradeButtons);

        _orders = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var ordersScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        ordersScroll.AddChild(_orders);
        right.AddChild(ordersScroll);

        BuildFunds(_storageTab);
        _depositHeld = new Button { Text = Loc.GetString("wg-stock-deposit-held") };
        _depositHeld.OnPressed += _ => _send(new WGStockRequest(WGStockAction.DepositHeld));
        _storageTab.AddChild(_depositHeld);
        var storageScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _storage = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        storageScroll.AddChild(_storage);
        _storageTab.AddChild(storageScroll);

        _marketTabButton.OnPressed += _ => SetTab(true);
        _storageTabButton.OnPressed += _ => SetTab(false);
    }

    private void SetTab(bool market)
    {
        _marketTab.Visible = market;
        _storageTab.Visible = !market;
        _marketTabButton.Pressed = market;
        _storageTabButton.Pressed = !market;
    }

    private void BuildFunds(BoxContainer parent)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 6) };
        _funds = new LineEdit { PlaceHolder = Loc.GetString("wg-stock-amount"), MinWidth = 120 };
        _depositFunds = new Button { Text = Loc.GetString("wg-stock-deposit") };
        _withdrawFunds = new Button { Text = Loc.GetString("wg-stock-withdraw") };
        _depositFunds.OnPressed += _ => Funds(WGStockAction.DepositFunds);
        _withdrawFunds.OnPressed += _ => Funds(WGStockAction.WithdrawFunds);
        row.AddChild(_funds);
        row.AddChild(_depositFunds);
        row.AddChild(_withdrawFunds);
        parent.AddChild(row);
    }

    private void Funds(WGStockAction action)
    {
        if (!int.TryParse(_funds.Text, out var amount) || amount <= 0)
            return;
        _send(new WGStockRequest(action) { Amount = amount });
    }

    private void Trade(bool sell, bool now)
    {
        if (_selected is not { } good)
            return;
        if (!int.TryParse(_quantity.Text, out var quantity) || quantity <= 0)
            return;
        var request = new WGStockRequest(now
            ? sell ? WGStockAction.SellNow : WGStockAction.BuyNow
            : sell ? WGStockAction.PlaceSell : WGStockAction.PlaceBuy)
        {
            Good = good,
            Quantity = quantity,
        };
        if (!now)
        {
            if (!int.TryParse(_price.Text, out var price) || price <= 0)
                return;
            request.Price = price;
        }
        _send(request);
    }

    public void UpdateState(WGStockState state)
    {
        _state = state;
        Title = Loc.GetString("wg-stock-title");
        _status.Text = StateText(state);
        _buy.Disabled = !state.HasOwner;
        _sell.Disabled = !state.HasOwner;
        _depositFunds.Disabled = !state.HasOwner;
        _withdrawFunds.Disabled = !state.HasOwner;
        if (state.Mode != WGStockTerminalMode.Market)
            SetTab(false);
        RebuildGoods();
        RebuildDetails();
        RebuildOrders();
        RebuildStorage();
    }

    private static string StateText(WGStockState state)
    {
        if (!state.HasOwner)
            return state.Message ?? Loc.GetString("wg-stock-no-owner");
        var kind = Loc.GetString(state.Kind == WGStockOwnerKind.Faction ? "wg-stock-kind-faction" : "wg-stock-kind-company");
        return Loc.GetString("wg-stock-status", ("kind", kind), ("name", state.OwnerName), ("balance", state.Balance),
            ("bank", state.PersonalBalance)) + (state.Message == null ? string.Empty : $"  {state.Message}");
    }

    private void RebuildGoods()
    {
        _goods.RemoveAllChildren();
        if (_state == null)
            return;
        foreach (var good in _state.Goods.OrderBy(good => good.Name))
        {
            var bid = good.BestBid > 0 ? good.BestBid.ToString() : "-";
            var ask = good.BestAsk > 0 ? good.BestAsk.ToString() : "-";
            var button = new Button
            {
                Text = Loc.GetString("wg-stock-good-row", ("name", good.Name), ("bid", bid), ("ask", ask)),
                ToggleMode = true,
                Pressed = _selected == good.Id,
                Margin = new Thickness(0, 0, 0, 2),
            };
            button.Label.HorizontalAlignment = HAlignment.Left;
            button.OnPressed += _ =>
            {
                _selected = good.Id;
                _price.Text = (good.BestAsk > 0 ? good.BestAsk : good.BasePrice).ToString();
                RebuildGoods();
                RebuildDetails();
            };
            _goods.AddChild(button);
        }
    }

    private WGStockGoodState? Selected()
    {
        if (_state == null || _selected is not { } selected)
            return null;
        return _state.Goods.FirstOrDefault(good => good.Id == selected);
    }

    private void RebuildDetails()
    {
        _details.RemoveAllChildren();
        if (Selected() is not { } good)
        {
            _details.AddChild(new Label { Text = Loc.GetString("wg-stock-pick-good") });
            return;
        }
        _details.AddChild(new Label
        {
            Text = Loc.GetString("wg-stock-good-title", ("name", good.Name), ("base", good.BasePrice), ("bid", good.BestBid),
                ("ask", good.BestAsk)),
        });
        _details.AddChild(new Label
        {
            Text = Loc.GetString("wg-stock-good-depth", ("bids", good.BidQuantity), ("asks", good.AskQuantity)),
        });
    }

    private void RebuildOrders()
    {
        _orders.RemoveAllChildren();
        if (_state == null)
            return;
        _orders.AddChild(new Label { Text = Loc.GetString("wg-stock-orders") });
        if (_state.Orders.Length == 0)
        {
            _orders.AddChild(new Label { Text = Loc.GetString("wg-stock-no-orders") });
            return;
        }
        foreach (var order in _state.Orders.OrderBy(order => order.Side).ThenBy(order => order.Price))
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            row.AddChild(new Label
            {
                Text = Loc.GetString("wg-stock-order-row", ("side", Loc.GetString(order.Side == WGStockOrderSide.Buy
                        ? "wg-stock-buy" : "wg-stock-sell")), ("name", order.Name), ("price", order.Price),
                    ("quantity", order.Quantity), ("owner", order.Mine ? Loc.GetString("wg-stock-you") : order.Owner)),
                HorizontalExpand = true,
            });
            if (order.Mine)
            {
                var cancel = new Button { Text = Loc.GetString("wg-stock-cancel") };
                var id = order.Id;
                cancel.OnPressed += _ => _send(new WGStockRequest(WGStockAction.CancelOrder) { OrderId = id });
                row.AddChild(cancel);
            }
            _orders.AddChild(row);
        }
    }

    private void RebuildStorage()
    {
        _storage.RemoveAllChildren();
        if (_state == null)
            return;

        _storage.AddChild(new Label { Text = Loc.GetString("wg-stock-round-materials") });
        if (_state.RoundMaterials.Length == 0)
            _storage.AddChild(new Label { Text = Loc.GetString("wg-stock-empty") });
        foreach (var material in _state.RoundMaterials)
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            row.AddChild(new Label
            {
                Text = Loc.GetString("wg-stock-material-row", ("name", material.Name), ("sheets", material.Sheets)),
                HorizontalExpand = true,
            });
            row.AddChild(MaterialButton(WGStockAction.WithdrawRound, material.Id, 1, Loc.GetString("wg-stock-take-one")));
            row.AddChild(MaterialButton(WGStockAction.WithdrawRound, material.Id, material.Sheets, Loc.GetString("wg-stock-take-all")));
            if (_state.Kind == WGStockOwnerKind.Company)
            {
                var store = MaterialButton(WGStockAction.StorePersistent, material.Id, 1, Loc.GetString("wg-stock-store-one"));
                store.Disabled = _state.PersistentUsed >= _state.PersistentCapacity;
                row.AddChild(store);
            }
            _storage.AddChild(row);
        }

        _storage.AddChild(new Label
        {
            Text = Loc.GetString("wg-stock-persistent-materials", ("used", _state.PersistentUsed),
                ("capacity", _state.PersistentCapacity)),
            Margin = new Thickness(0, 8, 0, 0),
        });
        if (_state.Kind != WGStockOwnerKind.Company)
        {
            _storage.AddChild(new Label { Text = Loc.GetString("wg-stock-no-persistent") });
        }
        else
        {
            if (_state.PersistentMaterials.Length == 0)
                _storage.AddChild(new Label { Text = Loc.GetString("wg-stock-empty") });
            foreach (var material in _state.PersistentMaterials)
            {
                var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
                row.AddChild(new Label
                {
                    Text = Loc.GetString("wg-stock-material-row", ("name", material.Name), ("sheets", material.Sheets)),
                    HorizontalExpand = true,
                });
                var one = MaterialButton(WGStockAction.WithdrawPersistent, material.Id, 1, Loc.GetString("wg-stock-take-one"));
                var all = MaterialButton(WGStockAction.WithdrawPersistent, material.Id, material.Sheets, Loc.GetString("wg-stock-take-all"));
                one.Disabled = !_state.CanWithdrawSilo;
                all.Disabled = !_state.CanWithdrawSilo;
                row.AddChild(one);
                row.AddChild(all);
                _storage.AddChild(row);
            }
            var buy = new Button
            {
                Text = Loc.GetString("wg-stock-buy-slot", ("cost", _state.SlotCost)),
                Disabled = !_state.CanBuySlots,
                Margin = new Thickness(0, 4, 0, 0),
            };
            buy.OnPressed += _ => _send(new WGStockRequest(WGStockAction.BuySlots) { Amount = 1 });
            _storage.AddChild(buy);
        }
    }

    private Button MaterialButton(WGStockAction action, string material, int sheets, string text)
    {
        var button = new Button { Text = text };
        button.OnPressed += _ => _send(new WGStockRequest(action) { MaterialId = material, Sheets = sheets });
        return button;
    }
}
