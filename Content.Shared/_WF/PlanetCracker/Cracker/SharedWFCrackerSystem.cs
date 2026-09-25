using System.Numerics;
using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.Map;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>
/// Cracker geometry shared by server, client and radar; abstract, so each assembly needs a concrete WFCrackerSystem.
/// </summary>
public abstract partial class SharedWFCrackerSystem : EntitySystem
{
    [Dependency] protected SharedTransformSystem TransformSystem = default!;

    /// <summary>
    /// Initial crack fall speed in levels per second (above the 0.1 transit exit band); shared so hull and chunk fall in step.
    /// </summary>
    public const float FallSeedVelocity = 0.3f;

    /// <summary>World centre of the hull's chunk berth, Distance tiles out along the marker's own facing.</summary>
    public bool TryGetBerthCentre(Entity<WFPlanetCrackerComponent> ent, out MapCoordinates centre)
    {
        centre = MapCoordinates.Nullspace;

        if (ent.Comp.Berth is not { } netBerth || !TryGetEntity(netBerth, out var berth))
            return false;

        if (!TryComp<WFChunkBerthComponent>(berth, out var berthComp))
            return false;

        var xform = Transform(berth.Value);

        if (xform.MapID == MapId.Nullspace)
            return false;

        var (position, rotation) = TransformSystem.GetWorldPositionRotation(xform);

        // Angle.Zero is Direction.South in Robust, so a marker facing north sits at 180 degrees.
        centre = new MapCoordinates(position + rotation.ToWorldVec() * berthComp.Distance, xform.MapID);
        return true;
    }

    /// <summary>World rectangle of the hull's chunk berth: the berth centre, the marker's Size and its world rotation.</summary>
    public bool TryGetBerthRect(Entity<WFPlanetCrackerComponent> ent, out Box2Rotated rect)
    {
        rect = default;

        if (!TryGetBerthCentre(ent, out var centre))
            return false;

        if (ent.Comp.Berth is not { } netBerth || !TryGetEntity(netBerth, out var berth))
            return false;

        if (!TryComp<WFChunkBerthComponent>(berth, out var berthComp))
            return false;

        var rotation = TransformSystem.GetWorldRotation(berth.Value);

        rect = new Box2Rotated(Box2.CenteredAround(centre.Position, berthComp.Size), rotation, centre.Position);
        return true;
    }

    /// <summary>Cut circle of a pair: the midpoint as a raw world XY and the cut radius.</summary>
    public bool TryGetCircle(EntityUid a, EntityUid b, out Vector2 centreXY, out float radius)
    {
        centreXY = Vector2.Zero;
        radius = 0f;

        if (!TryComp<WFGravityAnchorComponent>(a, out var anchorA) || !HasComp<WFGravityAnchorComponent>(b))
            return false;

        var posA = TransformSystem.GetWorldPosition(a);
        var posB = TransformSystem.GetWorldPosition(b);

        centreXY = (posA + posB) / 2f;
        radius = SharedWFGravityAnchorSystem.GetCutRadius((posA - posB).Length(), anchorA.CutPadding);
        return true;
    }

    /// <summary>
    /// Raw XY delta from the berth centre to the cut circle centre; plain subtraction, as the two maps share world XY.
    /// </summary>
    public bool TryGetBerthOffset(Entity<WFPlanetCrackerComponent> ent, EntityUid a, EntityUid b, out Vector2 offset)
    {
        offset = Vector2.Zero;

        if (!TryGetBerthCentre(ent, out var centre))
            return false;

        if (!TryGetCircle(a, b, out var circleCentre, out _))
            return false;

        offset = circleCentre - centre.Position;
        return true;
    }

    /// <summary>The sector body an orbit layer map belongs to, walked through the layer's own back-link.</summary>
    public bool TryGetPlanetFromOrbit(EntityUid mapUid, out Entity<WFSectorPlanetComponent> planet)
    {
        planet = default;

        if (!TryComp<WFOrbitLayerComponent>(mapUid, out var orbit))
            return false;

        if (orbit.Planet is not { } netPlanet || !TryGetEntity(netPlanet, out var body))
            return false;

        if (!TryComp<WFSectorPlanetComponent>(body, out var sector))
            return false;

        planet = (body.Value, sector);
        return true;
    }

    /// <summary>Whether the planet this hull is orbiting has already been cracked; false when the hull is not in orbit.</summary>
    public bool IsPlanetCracked(Entity<WFPlanetCrackerComponent> ent)
    {
        if (Transform(ent.Owner).MapUid is not { } mapUid)
            return false;

        return TryGetPlanetFromOrbit(mapUid, out var planet) && planet.Comp.Cracked;
    }

    /// <summary>
    /// Crack duration: base time scaled by pair distance over the reference distance, then by the part multiplier.
    /// </summary>
    public static TimeSpan GetCrackDuration(float distance, float partMultiplier, TimeSpan baseTime, float referenceDistance)
    {
        if (referenceDistance <= 0f)
            return baseTime * partMultiplier;

        return baseTime * (distance / referenceDistance) * partMultiplier;
    }
}
