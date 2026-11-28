namespace Content.Shared._WG.StockExchange;

/// <summary>Who owns a market account: a gameplay faction or a player-run company.</summary>
public enum WGStockOwnerKind : byte
{
    Faction,
    Company,
}

/// <summary>What a market terminal shows.</summary>
public enum WGStockTerminalMode : byte
{
    Market,
    MaterialSilo,
}

/// <summary>Whether an order adds to the bids or the asks.</summary>
public enum WGStockOrderSide : byte
{
    Buy,
    Sell,
}

/// <summary>Limits shared by the exchange, its terminals and its windows.</summary>
public static class WGStockPolicy
{
    /// <summary>One sheet of material is this many material units, as a lathe sees them.</summary>
    public const int UnitsPerSheet = 100;

    /// <summary>The least an order price or quantity can be.</summary>
    public const int Minimum = 1;

    /// <summary>The most one order can ask or bid per unit.</summary>
    public const int MaximumPrice = 1_000_000_000;

    /// <summary>The most one order can trade.</summary>
    public const int MaximumQuantity = 100_000;

    /// <summary>The most orders one account can have open in one market.</summary>
    public const int MaximumOpenOrders = 50;

    /// <summary>What one sheet of persistent company silo space costs, from the company bank.</summary>
    public const long PersistentSlotCost = 1_000_000;

    /// <summary>The most sheets a company's persistent silo can be bought up to.</summary>
    public const int MaximumPersistentSheets = 10_000;

    /// <summary>The most a company can buy in one terminal action.</summary>
    public const int MaximumSlotsAtOnce = 100;

    /// <summary>The price of buying persistent silo space for one sheet.</summary>
    public static long SlotCost(int sheets) => Math.Clamp(sheets, 1, MaximumSlotsAtOnce) * PersistentSlotCost;

    /// <summary>Material units in a number of sheets.</summary>
    public static int Units(int sheets) => sheets * UnitsPerSheet;

    /// <summary>Whole sheets in a number of material units, rounded down.</summary>
    public static int Sheets(int units) => units / UnitsPerSheet;

    /// <summary>A character with a gameplay faction acts for it, even when they also belong to a player company.</summary>
    public static bool FactionTakesPrecedence(string? faction) =>
        !string.IsNullOrEmpty(faction) && faction != "None";

    /// <summary>Whether an order size is acceptable.</summary>
    public static bool ValidQuantity(int quantity) => quantity is >= Minimum and <= MaximumQuantity;

    /// <summary>Whether an order price is acceptable.</summary>
    public static bool ValidPrice(int price) => price is >= Minimum and <= MaximumPrice;

    /// <summary>How bought sheets fill a company's persistent silo first, then its round storage.</summary>
    public static (int Persistent, int Round) DeliverySplit(int sheets, int capacity, int used)
    {
        var free = Math.Max(0, capacity - used);
        var persistent = Math.Clamp(sheets, 0, free);
        return (persistent, Math.Max(0, sheets - persistent));
    }

    /// <summary>Whether a number of sheets fits in a persistent silo of this size.</summary>
    public static bool Fits(int usedSheets, int capacitySheets, int sheets) =>
        sheets > 0 && usedSheets + sheets <= capacitySheets && capacitySheets <= MaximumPersistentSheets;
}
