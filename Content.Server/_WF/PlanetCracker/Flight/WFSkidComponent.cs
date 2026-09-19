using Robust.Shared.Map;

namespace Content.Server._WF.PlanetCracker.Flight;

/// <summary>
/// A hull grinding out a hard landing: still an ordinary grid, still repairable, but shedding leading-edge tiles and
/// flattening whatever it slides through until the ground friction pass has taken its speed away.
/// </summary>
[RegisterComponent]
public sealed partial class WFSkidComponent : Component
{
    /// <summary>Damage accumulated on each leading-edge tile, keyed by grid index; a tile over the threshold goes.</summary>
    [DataField]
    public Dictionary<Vector2i, float> TileDamage = new();

    /// <summary>The looping scrape, stopped and cleared when the hull comes to rest.</summary>
    [DataField]
    public EntityUid? Loop;

    public EntityUid? ImpactStream;
    public TimeSpan NextImpactSound;
    public TimeSpan NextLoopRefresh;

    /// <summary>When the leading edge is next chewed on; the sweep is throttled rather than run every tick.</summary>
    [DataField]
    public TimeSpan NextBite;

    /// <summary>
    /// When the obstacles under the hull are next flattened. Its own clock rather than the bite's: the wall pass runs
    /// every tick for as long as the hull is overlapping something, and each thing broken is a networked sound.
    /// </summary>
    [DataField]
    public TimeSpan NextPlough;

    /// <summary>Ground cells already scarred during this skid, preventing repeated terrain edits and decals.</summary>
    public HashSet<Vector2i> ScarredTiles = new();
    public EntityUid? ScarMap;

    /// <summary>Detached wreckage slides and scars terrain without recursive explosive hull grinding.</summary>
    public bool Debris;
}
