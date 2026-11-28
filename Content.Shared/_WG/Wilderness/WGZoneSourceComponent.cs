using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Wilderness;

/// <summary>
/// Makes a grid, or a point marker, the centre of a zone. A grid's zone reaches its radius past the grid's edge; a
/// marker's reaches it from the marker.
/// </summary>
[RegisterComponent]
public sealed partial class WGZoneSourceComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WGZonePrototype> Zone;

    /// <summary>Overrides the zone's own radius.</summary>
    [DataField]
    public float? Radius;

    /// <summary>Whether radars draw this zone's edge; off for a site whose position is kept hidden.</summary>
    [DataField]
    public bool Rings = true;

    /// <summary>Whether claiming the grid ends the zone, as for a wreck someone takes as their ship.</summary>
    [DataField]
    public bool EndsWhenClaimed;
}
