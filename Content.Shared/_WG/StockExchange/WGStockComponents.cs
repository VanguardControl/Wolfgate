using Robust.Shared.GameStates;

namespace Content.Shared._WG.StockExchange;

/// <summary>A market terminal: the exchange console or a material silo.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGStockTerminalComponent : Component
{
    [DataField]
    public WGStockTerminalMode Mode = WGStockTerminalMode.Market;

    /// <summary>The market profile this terminal asks the grid to use, when it is the first terminal there.</summary>
    [DataField]
    public string Profile = "WGStockMarketDefault";

    /// <summary>The device-link port a delivery pad listens on.</summary>
    public const string DeliveryPort = "WGStockExchangeConsole";
}

/// <summary>Where quick buys by characters with no faction or company appear.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGStockDeliveryPadComponent : Component
{
    /// <summary>The device-link port an exchange console sends to.</summary>
    public const string SinkPort = "WGStockDeliveryPad";
}

/// <summary>Where goods are sold to the best bids. Putting something in it, or clicking it, sells what is there.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGStockSellSpotComponent : Component
{
    /// <summary>The device-link port a sell console sends to.</summary>
    public const string SinkPort = "WGStockSellPad";

    /// <summary>The radius around the pad that its computer reads, including container contents.</summary>
    [DataField]
    public float Radius = 1f;
}
