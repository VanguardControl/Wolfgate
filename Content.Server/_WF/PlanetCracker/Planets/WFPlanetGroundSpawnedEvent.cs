using Content.Shared._WF.PlanetCracker.Planets;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>Raised broadcast once a planet's ground map exists, before any of it generates.</summary>
[ByRefEvent]
public readonly record struct WFPlanetGroundSpawnedEvent(EntityUid Ground, WFPlanetSurfacePrototype Surface);
