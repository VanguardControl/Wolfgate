using Robust.Shared.Serialization;

namespace Content.Shared._WG.StockExchange;

[Serializable, NetSerializable]
public enum WGStockUiKey : byte
{
    Key,
}

/// <summary>What a terminal button asks the server to do. The acting account is always the sender's.</summary>
[Serializable, NetSerializable]
public enum WGStockAction : byte
{
    Refresh,
    DepositFunds,
    WithdrawFunds,
    PlaceBuy,
    PlaceSell,
    CancelOrder,
    BuyNow,
    SellNow,
    SellHeld,
    BuySlots,
    StorePersistent,
    WithdrawPersistent,
    WithdrawRound,
    DepositHeld,
}

/// <summary>One tradable good and its best quotes.</summary>
[Serializable, NetSerializable]
public sealed class WGStockGoodState
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public int BasePrice;
    public int BestBid;
    public int BestAsk;
    public int BidQuantity;
    public int AskQuantity;
}

/// <summary>One resting order in a market's book.</summary>
[Serializable, NetSerializable]
public sealed class WGStockOrderState
{
    public int Id;
    public string Good = string.Empty;
    public string Name = string.Empty;
    public WGStockOrderSide Side;
    public int Price;
    public int Quantity;
    public string Owner = string.Empty;
    public bool Mine;
}

/// <summary>A material in a silo or an exchange account.</summary>
[Serializable, NetSerializable]
public sealed class WGStockMaterialState
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public int Sheets;
}

/// <summary>Everything one viewer sees in one terminal.</summary>
[Serializable, NetSerializable]
public sealed class WGStockState
{
    public WGStockTerminalMode Mode;
    public bool HasOwner;
    public WGStockOwnerKind Kind;
    public string OwnerName = string.Empty;
    public string OwnerTag = string.Empty;
    public long Balance;
    public int PersonalBalance;
    public bool Frozen;
    public int PersistentCapacity;
    public int PersistentUsed;
    public bool CanWithdrawSilo;
    public bool CanBuySlots;
    public int SlotCost;
    public int UnitsPerSheet = WGStockPolicy.UnitsPerSheet;
    public WGStockGoodState[] Goods = Array.Empty<WGStockGoodState>();
    public WGStockOrderState[] Orders = Array.Empty<WGStockOrderState>();
    public WGStockMaterialState[] RoundMaterials = Array.Empty<WGStockMaterialState>();
    public WGStockMaterialState[] PersistentMaterials = Array.Empty<WGStockMaterialState>();
    public string? Message;
}

[Serializable, NetSerializable]
public sealed class WGStockStateMessage(WGStockState state) : BoundUserInterfaceMessage
{
    public readonly WGStockState State = state;
}

/// <summary>A request from a terminal window.</summary>
[Serializable, NetSerializable]
public sealed class WGStockRequest(WGStockAction action) : BoundUserInterfaceMessage
{
    public readonly WGStockAction Action = action;

    public string Good = string.Empty;
    public int Price;
    public int Quantity;
    public int OrderId;
    public int Amount;
    public string MaterialId = string.Empty;
    public int Sheets;
}
