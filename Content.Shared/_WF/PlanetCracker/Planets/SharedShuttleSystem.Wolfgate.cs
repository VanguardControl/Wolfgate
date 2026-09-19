using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Planets;

namespace Content.Shared.Shuttles.Systems;

public abstract partial class SharedShuttleSystem
{
    /// <summary>
    /// An orbit layer is never an FTL destination - entering orbit is the shuttle console's own action and needs no
    /// drive - and a planet network is never left from a surface, air or cloud layer: you climb to orbit first.
    /// </summary>
    protected bool WfAllowFTL(EntityUid shuttleUid, EntityUid targetMapUid)
    {
        // Inbound: the orbit layer is reached from the console's orbit button, which runs its own range check.
        if (HasComp<WFOrbitLayerComponent>(targetMapUid))
            return false;

        if (_xformQuery.GetComponent(shuttleUid).MapUid is not { } shuttleMapUid)
            return true;

        // Outbound: never from mid-transit, and never off a surface, air or cloud layer.
        if (HasComp<CEZTransitMapComponent>(shuttleMapUid))
            return false;

        return !HasComp<WFPlanetLayerComponent>(shuttleMapUid) || HasComp<WFOrbitLayerComponent>(shuttleMapUid);
    }
}
