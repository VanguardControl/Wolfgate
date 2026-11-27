using Robust.Shared.Prototypes;

namespace Content.Server._WG.PoiArrivals;

/// <summary>A point of interest held back at round start that warps into the sector later in the shift.</summary>
[Prototype("wgPoiArrival")]
public sealed partial class WGPoiArrivalPrototype : IPrototype
{
    /// <summary>The ID of the point of interest that arrives.</summary>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The earliest time into the shift it arrives.</summary>
    [DataField]
    public TimeSpan MinDelay = TimeSpan.FromMinutes(30);

    /// <summary>The latest time into the shift it arrives.</summary>
    [DataField]
    public TimeSpan MaxDelay = TimeSpan.FromMinutes(90);

    /// <summary>Announced to the sector on arrival, with the station's name and its range and bearing from the sector core.</summary>
    [DataField(required: true)]
    public LocId Announcement;

    /// <summary>Who the announcement is from.</summary>
    [DataField]
    public LocId Sender = "wg-poi-arrival-sender";

    /// <summary>The announcement's colour.</summary>
    [DataField]
    public Color? Color;
}
