using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Traders;

/// <summary>
/// Lets a trader sell the stock of a vending machine prototype, at that machine's prices.
/// </summary>
[RegisterComponent]
public sealed partial class TraderShopComponent : Component
{
    /// <summary>
    /// Most of any one item a single basket may hold.
    /// </summary>
    public const int MaxPerLine = 20;

    /// <summary>
    /// Most distinct items a single basket may hold.
    /// </summary>
    public const int MaxLines = 12;

    /// <summary>
    /// Vending machine entity prototypes whose packs, pricing and taxes are mirrored. Their
    /// stock is concatenated in order; an item listed twice keeps the first machine's price.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> Vendors = new();

    /// <summary>
    /// Multiplies every price. Below one is a discount.
    /// </summary>
    [DataField]
    public float PriceMultiplier = 1f;

    /// <summary>
    /// If above zero, the trader carries only this many items picked at random from the mirrored
    /// machines, each in a limited quantity that runs out.
    /// </summary>
    [DataField]
    public int RandomStock;

    /// <summary>
    /// Fewest of each randomly stocked item.
    /// </summary>
    [DataField]
    public int StockMin = 1;

    /// <summary>
    /// Most of each randomly stocked item.
    /// </summary>
    [DataField]
    public int StockMax = 4;

    /// <summary>
    /// What is left of each randomly stocked item; null while the stock is unlimited or not rolled yet.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, int>? Limited;

    /// <summary>
    /// Carton an order is packed into when it runs to more than one item.
    /// </summary>
    [DataField]
    public EntProtoId Parcel = "WFTraderParcel";
}
