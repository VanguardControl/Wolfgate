namespace Content.Server._WG.PoiArrivals;

/// <summary>On an arriving point of interest's grid while it jumps into the sector.</summary>
[RegisterComponent, Access(typeof(WGPoiArrivalSystem))]
public sealed partial class WGPoiArrivalComponent : Component
{
    /// <summary>Its arrival prototype.</summary>
    [DataField]
    public string Arrival = string.Empty;

    /// <summary>The map it was loaded on, deleted once it has arrived.</summary>
    [DataField]
    public EntityUid? Holding;
}
