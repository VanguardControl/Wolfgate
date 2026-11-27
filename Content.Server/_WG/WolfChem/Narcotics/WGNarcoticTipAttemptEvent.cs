namespace Content.Server._WG.WolfChem.Narcotics;

/// <summary>Raised broadcast before security is tipped about a sale; cancel it to keep the tip back.</summary>
[ByRefEvent]
public record struct WGNarcoticTipAttemptEvent(EntityUid Seller, LocId Goods)
{
    public bool Cancelled;
}
