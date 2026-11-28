using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.StockExchange;

/// <summary>The catalogue and rules of one exchange. Orders may rest only on goods it lists.</summary>
[Prototype("wgStockMarket")]
public sealed partial class WGStockMarketPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Materials the market trades, one unit of an order is one sheet.</summary>
    [DataField]
    public List<ProtoId<MaterialPrototype>> Materials = new();

    /// <summary>Price of an order with no better quote, used for the first trade of a good.</summary>
    [DataField]
    public bool BasePriceFromPrototype = true;
}
