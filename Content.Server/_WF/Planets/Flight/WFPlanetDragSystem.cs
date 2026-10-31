using System.Numerics;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.Planets.Flight;

/// <summary>Speed cap and heavy damping on planet layers so hulls don't outrun terrain streaming.</summary>
// A per-tick sweep, not subscriptions: grids change maps through transit, FTL and CE hops this module doesn't own.
public sealed partial class WFPlanetDragSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFPlanetBoundsSystem _bounds = default!;

    /// <summary>Speed cap multiplier for a hull that has lost lift and is gliding.</summary>
    public const float LiftLostAllowance = 1.5f;

    /// <summary>Metres a second a hull past the world's edge is pushed back towards its centre.</summary>
    public const float PushBackSpeed = 2f;

    /// <summary>Metres past the edge a hull may get, through the push back, before it is put back inside outright.</summary>
    public const float HardEdgeSlack = 12f;

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MapGridComponent, PhysicsComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var body, out var xform))
        {
            // A z-layer map is itself a grid: never drag the layer.
            if (HasComp<MapComponent>(uid))
                continue;

            // Every layer of a bounded world turns hulls back at its edge, the gaps between layers and the ground too.
            TurnBack(uid, body, xform);

            if (!TryGetLimits(uid, xform, out var maxSpeed, out var damping))
            {
                Release(uid, body);
                continue;
            }

            Hold(uid, body, damping);
            Clamp(uid, body, maxSpeed);
        }
    }

    /// <summary>
    /// A hull flying out past a bounded world's circle loses its outward speed and is pushed back in: a soft wall,
    /// where the ground below has its hard one. Below orbit, one that still gets well past it is put back inside.
    /// </summary>
    private void TurnBack(EntityUid grid, PhysicsComponent body, TransformComponent xform)
    {
        // Only what moves under its own power: cities, wrecks and broken-off terrain stay where they were put.
        if (body.BodyType != BodyType.Dynamic || HasComp<WFDetachedTerrainComponent>(grid) || HasComp<ForceAnchorComponent>(grid)
            || xform.MapUid is not { } map || !_bounds.TryGetBounds(map, out var centre, out var radius))
            return;

        var reach = _bounds.HullReach(grid) + WFPlanetBoundsSystem.HullMargin;
        var offset = _bounds.HullCentre(grid) - centre;
        var distance = offset.Length();

        if (distance <= 0f || distance + reach <= radius)
            return;

        // Orbit is where ships arrive from FTL, anywhere; there the push back alone brings them in. On the ground
        // friction eats the push, so the edge is hard there.
        var grounded = HasComp<CEZGroundLayerComponent>(map) || TryComp<CEZMapComponent>(map, out var zMap) && zMap.Depth == 0;
        if ((grounded || distance + reach > radius + HardEdgeSlack) && !HasComp<WFOrbitLayerComponent>(map))
        {
            _bounds.ClampHull(grid);
            _physics.SetLinearVelocity(grid, Vector2.Zero, body: body);
            return;
        }

        var outward = offset / distance;
        var velocity = body.LinearVelocity;
        var along = Vector2.Dot(velocity, outward);

        if (along > 0f)
            velocity -= outward * along;

        _physics.SetLinearVelocity(grid, velocity - outward * PushBackSpeed, body: body);
    }

    /// <summary>The speed cap and damping for this grid, or false when it should not be dragged.</summary>
    private bool TryGetLimits(EntityUid grid, TransformComponent xform, out float maxSpeed, out float damping)
    {
        maxSpeed = 0f;
        damping = 0f;

        // Transit gaps have no layer marker, so falls are never dragged.
        if (xform.MapUid is not { } mapUid || !TryComp<WFPlanetLayerComponent>(mapUid, out var layer))
            return false;

        // The ground layer is left to CE friction and skids.
        if (HasComp<CEZGroundLayerComponent>(mapUid) || (TryComp<CEZMapComponent>(mapUid, out var zMap) && zMap.Depth == 0))
            return false;

        if (HasComp<WFDetachedTerrainComponent>(grid) || HasComp<ForceAnchorComponent>(grid))
            return false;

        if (TryComp<WFOrbitLayerComponent>(mapUid, out var orbit))
        {
            maxSpeed = orbit.MaxSpeed;
            damping = orbit.LinearDamping;
            return true;
        }

        maxSpeed = layer.MaxSpeed;
        damping = layer.LinearDamping;
        return true;
    }

    /// <summary>Applies the layer's damping, saving the grid's own the first time.</summary>
    private void Hold(EntityUid grid, PhysicsComponent body, float damping)
    {
        if (!TryComp<WFPlanetDragComponent>(grid, out var comp))
        {
            comp = AddComp<WFPlanetDragComponent>(grid);
            comp.OriginalDamping = body.LinearDamping;
        }

        _physics.SetLinearDamping(grid, body, damping);
    }

    /// <summary>Restores the grid's own damping once it leaves a dragged layer.</summary>
    private void Release(EntityUid grid, PhysicsComponent body)
    {
        if (!TryComp<WFPlanetDragComponent>(grid, out var comp))
            return;

        _physics.SetLinearDamping(grid, body, comp.OriginalDamping);
        RemComp<WFPlanetDragComponent>(grid);
    }

    /// <summary>Clamps a grid to the layer's speed cap; damping alone doesn't bound speed.</summary>
    private void Clamp(EntityUid grid, PhysicsComponent body, float maxSpeed)
    {
        if (maxSpeed <= 0f)
            return;

        if (HasComp<WFLiftLostComponent>(grid))
            maxSpeed *= LiftLostAllowance;

        var speedSquared = body.LinearVelocity.LengthSquared();

        // Skip non-finite speed; scaling it would write NaN velocity.
        if (!float.IsFinite(speedSquared) || speedSquared <= maxSpeed * maxSpeed)
            return;

        _physics.SetLinearVelocity(grid, body.LinearVelocity * (maxSpeed / MathF.Sqrt(speedSquared)), body: body);
    }
}
