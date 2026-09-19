using Content.Server._NF.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Flight;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.PlanetCracker.Flight;

/// <summary>
/// Flight over a planet is slow flight. A world's surface streams in around whatever is above it, so a hull crossing
/// it at shuttle speeds outruns its own terrain and the chunk loader behind it; every planet layer therefore has a
/// speed cap and a heavy linear damping, and orbit - where hulls actually sit - has the tightest pair of the two.
/// Thrusters are untouched: they fight the drag instead of beating it.
/// A per-tick sweep over grids rather than subscriptions: the map a grid is on changes through transit, FTL and CE's
/// own level hops, none of which this feature owns, and damping has to be handed back on every one of them.
/// </summary>
public sealed partial class WFPlanetDragSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <summary>
    /// How far over its layer's cap a hull that has lost lift may go. A fall is supposed to read as a fall: the glide
    /// F10 seeds and compounds is the one speed on a planet that is not the pilot's doing.
    /// </summary>
    public const float LiftLostAllowance = 1.5f;

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

            if (!TryGetLimits(uid, xform, out var maxSpeed, out var damping))
            {
                Release(uid, body);
                continue;
            }

            Hold(uid, body, damping);
            Clamp(uid, body, maxSpeed);
        }
    }

    /// <summary>The cap and damping this grid flies under right now, or false when nothing should touch it.</summary>
    private bool TryGetLimits(EntityUid grid, TransformComponent xform, out float maxSpeed, out float damping)
    {
        maxSpeed = 0f;
        damping = 0f;

        // A transit gap carries no layer marker of its own, so a hull falling between layers is never dragged: the
        // fall is CE's, and F10 measures a landing against the speed it arrives with.
        if (xform.MapUid is not { } mapUid || !TryComp<WFPlanetLayerComponent>(mapUid, out var layer))
            return false;

        // The ground layer is where a hull has arrived. Taxiing, skidding and CE's own friction own it from there.
        if (HasComp<CEZGroundLayerComponent>(mapUid) || (TryComp<CEZMapComponent>(mapUid, out var zMap) && zMap.Depth == 0))
            return false;

        // The chunk hangs in its berth under the projectors and rides the hull that cut it; a force-anchored grid is
        // not going anywhere under its own power at all.
        if (HasComp<WFPlanetChunkComponent>(grid) || HasComp<ForceAnchorComponent>(grid))
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

    /// <summary>Puts the layer's damping on a grid, remembering the grid's own the first time it does.</summary>
    private void Hold(EntityUid grid, PhysicsComponent body, float damping)
    {
        if (!TryComp<WFPlanetDragComponent>(grid, out var comp))
        {
            comp = AddComp<WFPlanetDragComponent>(grid);
            comp.OriginalDamping = body.LinearDamping;
        }

        _physics.SetLinearDamping(grid, body, damping);
    }

    /// <summary>Hands a grid its own damping back the moment it is off a dragged layer, however it got off.</summary>
    private void Release(EntityUid grid, PhysicsComponent body)
    {
        if (!TryComp<WFPlanetDragComponent>(grid, out var comp))
            return;

        _physics.SetLinearDamping(grid, body, comp.OriginalDamping);
        RemComp<WFPlanetDragComponent>(grid);
    }

    /// <summary>Holds a grid under the layer's cap; damping alone only sets a terminal speed, it does not bound one.</summary>
    private void Clamp(EntityUid grid, PhysicsComponent body, float maxSpeed)
    {
        if (maxSpeed <= 0f)
            return;

        if (HasComp<WFLiftLostComponent>(grid))
            maxSpeed *= LiftLostAllowance;

        var speedSquared = body.LinearVelocity.LengthSquared();

        // Non-finite is not a speed: scaling it would hand the hull a NaN heading rather than a slower one, which is
        // the same trap F10's glide maths had to be taught about.
        if (!float.IsFinite(speedSquared) || speedSquared <= maxSpeed * maxSpeed)
            return;

        _physics.SetLinearVelocity(grid, body.LinearVelocity * (maxSpeed / MathF.Sqrt(speedSquared)), body: body);
    }
}
