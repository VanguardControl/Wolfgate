using System.Linq;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.StockExchange;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.StockExchange;

/// <summary>The sale computer: each buyer's share of the pad's materials, separated, with a total.</summary>
public sealed class WGStockSellConsoleWindow : FancyWindow
{
    private readonly Label _status;
    private readonly BoxContainer _lines;
    private readonly Label _total;
    private readonly Button _sellAll;
    private readonly Action _onSellAll;

    public WGStockSellConsoleWindow(Action onSellAll)
    {
        _onSellAll = onSellAll;
        Title = Loc.GetString("wg-stock-sell-console-title");
        SetSize = new Vector2(560, 480);
        MinSize = new Vector2(420, 320);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        ContentsContainer.AddChild(root);

        _status = new Label { Margin = new Thickness(0, 0, 0, 6) };
        root.AddChild(_status);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _lines = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_lines);
        root.AddChild(scroll);

        _total = new Label { Margin = new Thickness(0, 6, 0, 6) };
        root.AddChild(_total);

        _sellAll = new Button { Text = Loc.GetString("wg-stock-sell-console-sell-all") };
        _sellAll.OnPressed += _ => _onSellAll();
        root.AddChild(_sellAll);
    }

    public void UpdateState(WGStockSellReceiptState state)
    {
        _status.Text = state.Message ?? string.Empty;
        _lines.RemoveAllChildren();
        foreach (var line in state.Lines)
        {
            _lines.AddChild(new Label
            {
                Text = Loc.GetString("wg-stock-sell-console-receipt", ("quantity", line.Quantity),
                    ("material", line.Material), ("buyer", line.Buyer), ("total", line.Total), ("price", line.Price)),
            });
            _lines.AddChild(new HSeparator { Margin = new Thickness(0, 2) });
        }

        _total.Text = Loc.GetString("wg-stock-sell-console-total", ("total", state.Total));
        _sellAll.Disabled = !state.Linked || state.Lines.Length == 0;
    }
}
