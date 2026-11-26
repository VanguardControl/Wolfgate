using Robust.Shared.Serialization;

namespace Content.Shared._WG.StockExchange;

/// <summary>One market as the remote app shows it.</summary>
[Serializable, NetSerializable]
public sealed class WGStockRemoteMarketState
{
    public string Name = string.Empty;
    public WGStockGoodState[] Goods = Array.Empty<WGStockGoodState>();
}

/// <summary>The Exchange Market Remote app's view: every market's quotes and the viewer's own stores. Read only.</summary>
[Serializable, NetSerializable]
public sealed class WGStockRemoteState : BoundUserInterfaceState
{
    public bool HasOwner;
    public WGStockOwnerKind Kind;
    public string OwnerName = string.Empty;
    public int PersistentCapacity;
    public int PersistentUsed;
    public WGStockMaterialState[] RoundMaterials = Array.Empty<WGStockMaterialState>();
    public WGStockMaterialState[] PersistentMaterials = Array.Empty<WGStockMaterialState>();
    public WGStockRemoteMarketState[] Markets = Array.Empty<WGStockRemoteMarketState>();
}
