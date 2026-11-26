using System.Linq;
using Content.Client.UserInterface.Fragments;
using Content.Shared._WG.StockExchange;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.StockExchange;

/// <summary>The Exchange Market Remote PDA app: every market's quotes and the viewer's stores, read only.</summary>
public sealed partial class WGStockRemoteUi : UIFragment
{
    private WGStockRemoteFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new WGStockRemoteFragment();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is WGStockRemoteState remote)
            _fragment?.UpdateState(remote);
    }
}

public sealed class WGStockRemoteFragment : BoxContainer
{
    private readonly Label _status;
    private readonly BoxContainer _markets;
    private readonly BoxContainer _holdings;

    public WGStockRemoteFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;
        HorizontalExpand = true;
        Margin = new Thickness(6);

        AddChild(new Label
        {
            Text = Loc.GetString("wg-stock-remote-title"),
            StyleClasses = { "LabelHeading" },
        });
        _status = new Label { Margin = new Thickness(0, 2, 0, 6) };
        AddChild(_status);

        var tabs = new TabContainer { VerticalExpand = true, HorizontalExpand = true };
        AddChild(tabs);

        var markets = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalExpand = true, HorizontalExpand = true };
        var marketsScroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        _markets = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        marketsScroll.AddChild(_markets);
        markets.AddChild(marketsScroll);
        tabs.AddChild(markets);
        tabs.SetTabTitle(0, Loc.GetString("wg-stock-remote-markets"));

        var holdings = new BoxContainer { Orientation = LayoutOrientation.Vertical, VerticalExpand = true, HorizontalExpand = true };
        var holdingsScroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        _holdings = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        holdingsScroll.AddChild(_holdings);
        holdings.AddChild(holdingsScroll);
        tabs.AddChild(holdings);
        tabs.SetTabTitle(1, Loc.GetString("wg-stock-remote-holdings"));
    }

    public void UpdateState(WGStockRemoteState state)
    {
        _status.Text = state.HasOwner
            ? Loc.GetString("wg-stock-remote-status", ("kind", Loc.GetString(state.Kind == WGStockOwnerKind.Faction
                ? "wg-stock-kind-faction" : "wg-stock-kind-company")), ("name", state.OwnerName))
            : Loc.GetString("wg-stock-remote-no-owner");

        _markets.RemoveAllChildren();
        if (state.Markets.Length == 0)
            _markets.AddChild(new Label { Text = Loc.GetString("wg-stock-remote-no-markets") });
        foreach (var market in state.Markets)
        {
            _markets.AddChild(new Label
            {
                Text = Loc.GetString("wg-stock-remote-market-header", ("name", market.Name)),
                StyleClasses = { "LabelHeading" },
                Margin = new Thickness(0, 6, 0, 2),
            });
            foreach (var good in market.Goods.OrderBy(good => good.Name))
            {
                _markets.AddChild(new Label
                {
                    Text = Loc.GetString("wg-stock-remote-good-row", ("name", good.Name), ("bid", good.BestBid),
                        ("ask", good.BestAsk), ("bids", good.BidQuantity), ("asks", good.AskQuantity)),
                });
            }
        }

        _holdings.RemoveAllChildren();
        if (!state.HasOwner)
        {
            _holdings.AddChild(new Label { Text = Loc.GetString("wg-stock-remote-no-owner") });
            return;
        }

        _holdings.AddChild(new Label { Text = Loc.GetString("wg-stock-remote-round-materials"), StyleClasses = { "LabelHeading" } });
        AddMaterials(_holdings, state.RoundMaterials);
        _holdings.AddChild(new Label
        {
            Text = Loc.GetString("wg-stock-remote-persistent-materials", ("used", state.PersistentUsed),
                ("capacity", state.PersistentCapacity)),
            StyleClasses = { "LabelHeading" },
            Margin = new Thickness(0, 6, 0, 0),
        });
        if (state.Kind == WGStockOwnerKind.Company)
            AddMaterials(_holdings, state.PersistentMaterials);
        else
            _holdings.AddChild(new Label { Text = Loc.GetString("wg-stock-no-persistent") });
    }

    private static void AddMaterials(BoxContainer parent, WGStockMaterialState[] materials)
    {
        if (materials.Length == 0)
        {
            parent.AddChild(new Label { Text = Loc.GetString("wg-stock-empty") });
            return;
        }
        foreach (var material in materials)
        {
            parent.AddChild(new Label
            {
                Text = Loc.GetString("wg-stock-material-row", ("name", material.Name), ("sheets", material.Sheets)),
            });
        }
    }
}
