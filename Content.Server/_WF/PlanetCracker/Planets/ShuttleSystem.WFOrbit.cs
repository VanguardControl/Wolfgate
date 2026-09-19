using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>Warm-up of an orbital insertion. Short and fixed, so it reads as a manoeuvre rather than a jump.</summary>
    public const float WfOrbitStartupTime = 5f;

    /// <summary>Transit time of an orbital insertion; it must exceed nothing, the arrival phase is carved out of it.</summary>
    public const float WfOrbitTravelTime = 5f;

    /// <summary>
    /// The hard gate on leaving a planet: you climb to orbit first, and you never jump out of mid-transit. Every FTL
    /// start funnels through <see cref="TrySetupFTL"/> or <see cref="FTLToDock"/>, so both ask this rather than trust
    /// the destination-side checks - <see cref="SharedShuttleSystem.CanFTLTo"/>'s WfAllowFTL only ever covered the
    /// paths that consult a destination list, and the console's beacon and free-FTL branches do not.
    /// Our own <see cref="WfFTLToLayer"/> starts from orbit or from the sector map, so it passes.
    /// </summary>
    public bool WfRefusesFtlDeparture(EntityUid shuttleUid)
    {
        if (Transform(shuttleUid).MapUid is not { } mapUid)
            return false;

        if (HasComp<CEZTransitMapComponent>(mapUid))
            return true;

        return HasComp<WFPlanetLayerComponent>(mapUid) && !HasComp<WFOrbitLayerComponent>(mapUid);
    }

    /// <summary>
    /// Moves a hull onto another map through the ordinary FTL transit with no drive, no beacon and no range check.
    /// This is the one entry point planet orbit uses: <see cref="FTLToCoordinates"/> is documented as taking no checks
    /// of its own, so undocking, the hyperspace map, the arrival sweep and the startup/arrival audio all still run,
    /// while <see cref="SharedShuttleSystem.GetFTLRange"/> - which gives a driveless hull a range of zero - never
    /// enters the picture. The caller owns every gate; see WFOrbitEntrySystem.
    /// </summary>
    /// <param name="shuttle">The hull to move.</param>
    /// <param name="targetMap">The destination map entity.</param>
    /// <param name="worldPos">Where on it to arrive, before the free-spot search.</param>
    public bool WfFTLToLayer(Entity<ShuttleComponent> shuttle, EntityUid targetMap, Vector2 worldPos)
    {
        if (!HasComp<MapComponent>(targetMap) || HasComp<FTLComponent>(shuttle.Owner))
            return false;

        // Same pose on the far side when the spot is clear, so an insertion reads as dropping straight down onto the
        // layer rather than being flung somewhere by the arrival scatter.
        var coordinates = new EntityCoordinates(targetMap, worldPos);
        var angle = _transform.GetWorldRotation(shuttle.Owner);

        if (!WfLayerSpotIsClear(shuttle.Owner, targetMap, worldPos)
            && TryGetFTLProximity(shuttle.Owner, coordinates, out var free, out var freeAngle))
        {
            coordinates = free;
            angle = freeAngle;
        }

        FTLToCoordinates(shuttle.Owner, shuttle.Comp, coordinates, angle, WfOrbitStartupTime, WfOrbitTravelTime);
        return HasComp<FTLComponent>(shuttle.Owner);
    }

    /// <summary>
    /// True when the hull's own footprint, carried over to the target map at the same pose, touches no other grid.
    /// The hull keeps its rotation, so its world AABB there is the current one shifted by the position delta.
    /// </summary>
    private bool WfLayerSpotIsClear(EntityUid shuttleUid, EntityUid targetMap, Vector2 worldPos)
    {
        if (!TryComp<MapGridComponent>(shuttleUid, out var grid) || !TryComp<MapComponent>(targetMap, out var map))
            return false;

        var delta = worldPos - _transform.GetWorldPosition(shuttleUid);
        var bounds = _transform.GetWorldMatrix(shuttleUid)
            .TransformBox(grid.LocalAABB)
            .Translated(delta)
            .Enlarged(FTLBufferRange);

        var found = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(map.MapId, bounds, ref found);

        foreach (var other in found)
        {
            if (other.Owner != shuttleUid)
                return false;
        }

        return true;
    }
}
