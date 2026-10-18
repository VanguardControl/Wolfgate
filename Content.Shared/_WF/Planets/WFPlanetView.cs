using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.PlanetCities;

namespace Content.Shared._WF.Planets;

/// <summary>How the z-level renderer scales a planet's layers. Monolith draws lower levels full size; a planet keeps the perspective.</summary>
public static class WFPlanetView
{
    /// <summary>Each planet layer below the observer is drawn this much smaller, as every z-level was before Monolith set the factor to 1.</summary>
    public const float LayerShrink = 0.85f;

    /// <summary>
    /// The per-level scale for a map: a planet layer's, or the transit map between two, else the renderer's own. A
    /// <paramref name="viewer"/> on foot (on the ground, a city's level or no grid at all) sees a planet's lower layers
    /// full size, as storeys of a building rather than the ground far below a ship.
    /// </summary>
    public static float Shrink(IEntityManager entMan, EntityUid? map, EntityUid? viewer = null)
    {
        if (map is not { } uid)
            return CESharedZLevelsSystem.ZLevelViewShrink;

        if (viewer is { } who && entMan.TryGetComponent(who, out TransformComponent? xform)
            && !entMan.HasComponent<CEZTransitMapComponent>(xform.MapUid)
            && (xform.GridUid is not { } grid || grid == xform.MapUid || entMan.HasComponent<WFPlanetCityGridComponent>(grid))
            && entMan.HasComponent<WFPlanetLayerComponent>(xform.MapUid))
            return 1f;

        if (entMan.TryGetComponent(uid, out CEZTransitMapComponent? transit))
            uid = transit.LowerMap ?? transit.UpperMap ?? uid;

        return entMan.HasComponent<WFPlanetLayerComponent>(uid) ? LayerShrink : CESharedZLevelsSystem.ZLevelViewShrink;
    }
}
