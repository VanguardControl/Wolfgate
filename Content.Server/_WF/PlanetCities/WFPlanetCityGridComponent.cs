using Content.Shared._WF.PlanetCities;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetCities;

/// <summary>A fixed city's level standing on a flight or underground layer: terrain and buildings, not a vessel.</summary>
[RegisterComponent]
public sealed partial class WFPlanetCityGridComponent : Component
{
    [DataField]
    public ProtoId<WFFixedCityPrototype> City;
}
