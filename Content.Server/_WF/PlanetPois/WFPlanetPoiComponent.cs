using Content.Shared.Procedural;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetPois;

/// <summary>On a ground layer: every point of interest placed on it this round.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetPoiComponent : Component
{
    [ViewVariables]
    public List<WFPlanetPoiSite> Sites = new();
}

/// <summary>One placed site: what was generated, where, how big it came out and the signal that marks it.</summary>
public sealed class WFPlanetPoiSite
{
    public ProtoId<DungeonConfigPrototype> Dungeon;
    public LocId Name;
    /// <summary>Where the generator was pointed; the site spreads round it.</summary>
    public Vector2i Origin;

    /// <summary>The tiles the site came out with, and the box round them; the signal sits over the box's middle.</summary>
    public int TileCount;
    public Box2i Bounds;
    public EntityUid Signal;
}

/// <summary>
/// On an orbit-layer marker over a site: an unidentified signal until someone on the ground gets close, then the
/// site's name.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPoiSignalComponent : Component
{
    [ViewVariables]
    public EntityUid Ground;

    [ViewVariables]
    public Vector2i Origin;

    [ViewVariables]
    public LocId Name;

    [ViewVariables]
    public float RevealRange = 48f;

    [ViewVariables]
    public bool Revealed;
}
