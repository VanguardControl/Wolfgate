using Content.Shared._WG.StockExchange;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.Client._WG.StockExchange;

/// <summary>The exchange console's window. Its state is sent to this viewer alone.</summary>
public sealed class WGStockBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WGStockWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = new WGStockWindow(request => SendMessage(request));
        _window.OpenCentered();
        SendMessage(new WGStockRequest(WGStockAction.Refresh));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WGStockStateMessage state)
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
