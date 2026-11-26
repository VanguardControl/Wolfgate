using Content.Shared._WG.StockExchange;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.StockExchange;

/// <summary>A tradable material, with one unit of an order being one sheet.</summary>
public readonly record struct WGStockGood(string Id);

/// <summary>One resting bid or ask. Goods or funds are held by the market until it trades or is cancelled.</summary>
public sealed class WGStockOrder
{
    public int Id;
    public EntityUid Account;
    public WGStockGood Good;
    public WGStockOrderSide Side;
    public int Price;
    public int Quantity;
    public TimeSpan Created;
}

/// <summary>One grid's exchange: its profile and every order resting on it.</summary>
[RegisterComponent]
public sealed partial class WGStockMarketComponent : Component
{
    [DataField]
    public ProtoId<WGStockMarketPrototype> Profile = "WGStockMarketDefault";

    [ViewVariables]
    public readonly List<WGStockOrder> Orders = new();

    [ViewVariables]
    public int NextOrderId = 1;
}

/// <summary>An account's listings and internal storage; one per faction in the round or per player company.</summary>
[RegisterComponent]
public sealed partial class WGStockAccountComponent : Component
{
    public WGStockOwnerKind Kind;
    public string Faction = string.Empty;
    public int CompanyId;
    public string Name = string.Empty;
    public string Tag = string.Empty;

    /// <summary>Trade proceeds held for a faction; a company's money stays in its bank.</summary>
    public long FactionBalance;

    /// <summary>Sheets of each material held for this round.</summary>
    public readonly Dictionary<string, int> RoundMaterials = new();

    /// <summary>Sheets of each material kept between rounds; companies only.</summary>
    public readonly Dictionary<string, int> PersistentMaterials = new();

    /// <summary>Persistent silo capacity in sheets; companies only, and zero until bought.</summary>
    public int PersistentCapacity;

    /// <summary>Ids of this account's orders still resting in a market.</summary>
    public readonly HashSet<int> Orders = new();
}

/// <summary>Marks the Exchange Market Remote PDA app, which shows markets and holdings without trading.</summary>
[RegisterComponent]
public sealed partial class WGStockRemoteCartridgeComponent : Component;

/// <summary>A sell pad's computer. It reads a pad linked with the mapping tools and sells the materials on it.</summary>
[RegisterComponent]
public sealed partial class WGStockSellConsoleComponent : Component
{
    /// <summary>The device-link port it sends from; a pad uses <see cref="WGStockSellSpotComponent.SinkPort"/>.</summary>
    public const string SourcePort = "WGStockSellConsole";
}
