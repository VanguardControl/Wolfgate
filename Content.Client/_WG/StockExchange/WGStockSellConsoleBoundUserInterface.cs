using Content.Shared._WG.StockExchange;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.Client._WG.StockExchange;

/// <summary>The sale computer's window: a receipt for its linked pad and a Sell all button.</summary>
public sealed class WGStockSellConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WGStockSellConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = new WGStockSellConsoleWindow(() => SendMessage(new WGStockSellAllMessage()));
        _window.OpenCentered();
        SendMessage(new WGStockSellRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WGStockSellStateMessage state)
            _window?.UpdateState(state.State);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Close();
        _window = null;
    }
}
