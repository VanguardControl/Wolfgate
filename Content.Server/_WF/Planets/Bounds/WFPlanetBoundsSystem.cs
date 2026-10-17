using System.Numerics;
using Content.Server.Salvage;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Planets.Bounds;

/// <summary>
/// Confines a world to a circle: marks every layer with its bounds when the network is built, so the loader never
/// generates terrain outside it, and rings the ground and the layers below it with an impassable boundary.
/// </summary>
public sealed partial class WFPlanetBoundsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private RestrictedRangeSystem _restricted = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Tiles a hull must keep between its edge and the circle to descend, or be pushed back to.</summary>
    public const float HullMargin = 4f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetNetworkBuiltEvent>(OnNetworkBuilt);
    }

    /// <summary>The radius a surface gets this round, or zero for unbounded.</summary>
    public int RadiusFor(WFPlanetSurfacePrototype surface)
    {
        if (!_cfg.GetCVar(PlanetCVars.Bounds))
            return 0;

        var cvar = _cfg.GetCVar(PlanetCVars.Radius);
        return Math.Max(cvar > 0 ? cvar : surface.Radius, 0);
    }

    private void OnNetworkBuilt(ref WFPlanetNetworkBuiltEvent args)
    {
        var radius = RadiusFor(args.Surface);
        if (radius <= 0 || !TryComp<WFPlanetNetworkComponent>(args.Network, out var network))
            return;

        var centre = network.Centre;

        foreach (var layer in network.Layers)
        {
            if (layer == network.OrbitMap)
                continue;

            Mark(layer, centre, radius);
        }

        foreach (var layer in network.LowerLayers)
        {
            Mark(layer, centre, radius);
        }

        if (TryComp<WFOrbitLayerComponent>(network.OrbitMap, out var orbit))
        {
            orbit.BoundsCentre = centre;
            orbit.BoundsRadius = radius;
            Dirty(network.OrbitMap, orbit);
        }

        // Only where someone can walk: a hull in the air is turned back by the drag sweep instead.
        _restricted.CreateBoundary(new EntityCoordinates(args.Ground, centre), radius);

        foreach (var layer in network.LowerLayers)
        {
            _restricted.CreateBoundary(new EntityCoordinates(layer, centre), radius);
        }
    }

    private void Mark(EntityUid layer, Vector2 centre, int radius)
    {
        var bounds = EnsureComp<WFPlanetBoundsComponent>(layer);
        bounds.Centre = centre;
        bounds.Radius = radius;
        Dirty(layer, bounds);
    }

    /// <summary>The circle of a map, from a layer's own bounds or an orbit layer's radar copy; false where unbounded.</summary>
    public bool TryGetBounds(EntityUid map, out Vector2 centre, out float radius)
    {
        if (TryComp<WFPlanetBoundsComponent>(map, out var bounds))
        {
            centre = bounds.Centre;
            radius = bounds.Radius;
            return radius > 0f;
        }

        if (TryComp<WFOrbitLayerComponent>(map, out var orbit) && orbit.BoundsRadius > 0f)
        {
            centre = orbit.BoundsCentre;
            radius = orbit.BoundsRadius;
            return true;
        }

        centre = Vector2.Zero;
        radius = 0f;
        return false;
    }

    /// <summary>How far a hull reaches from its origin: the half-diagonal of its bounds.</summary>
    public float HullReach(EntityUid grid)
    {
        return TryComp<MapGridComponent>(grid, out var comp) ? comp.LocalAABB.Size.Length() / 2f : 0f;
    }

    /// <summary>Whether a hull, with its margin, lies wholly inside its map's circle; true on an unbounded map.</summary>
    public bool IsHullInside(EntityUid grid)
    {
        var xform = Transform(grid);

        if (xform.MapUid is not { } map || !TryGetBounds(map, out var centre, out var radius))
            return true;

        var reach = HullReach(grid) + HullMargin;
        return Vector2.Distance(_transform.GetWorldPosition(xform), centre) + reach <= radius;
    }

    /// <summary>Moves a hull to the nearest spot where it lies wholly inside its map's circle, if it is outside.</summary>
    public void ClampHull(EntityUid grid)
    {
        var xform = Transform(grid);

        if (xform.MapUid is not { } map || !TryGetBounds(map, out var centre, out var radius))
            return;

        var reach = HullReach(grid) + HullMargin;
        var position = _transform.GetWorldPosition(xform);
        var offset = position - centre;
        var distance = offset.Length();
        var allowed = Math.Max(radius - reach, 0f);

        if (distance <= allowed || distance <= 0f)
            return;

        _transform.SetWorldPosition(grid, centre + offset * (allowed / distance));
    }
}
