using Robust.Shared.Serialization;

namespace Content.Shared._WG.StockExchange;

[Serializable, NetSerializable]
public enum WGStockSellUiKey : byte
{
    Key,
}

/// <summary>One buyer's share of a material on a sell pad.</summary>
[Serializable, NetSerializable]
public sealed class WGStockSellReceiptLine
{
    public string Buyer = string.Empty;
    public string Material = string.Empty;
    public int Quantity;
    public int Price;
    public long Total;
}

/// <summary>The sell console's receipt for the pad it is linked to.</summary>
[Serializable, NetSerializable]
public sealed class WGStockSellReceiptState : BoundUserInterfaceState
{
    public bool Linked;
    public string? Message;
    public WGStockSellReceiptLine[] Lines = Array.Empty<WGStockSellReceiptLine>();
    public long Total;
}

/// <summary>Asks the console to rebuild its receipt.</summary>
[Serializable, NetSerializable]
public sealed class WGStockSellRefreshMessage : BoundUserInterfaceMessage;

/// <summary>Asks the console to sell everything on its linked pad that has a buy order.</summary>
[Serializable, NetSerializable]
public sealed class WGStockSellAllMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WGStockSellStateMessage(WGStockSellReceiptState state) : BoundUserInterfaceMessage
{
    public readonly WGStockSellReceiptState State = state;
}
