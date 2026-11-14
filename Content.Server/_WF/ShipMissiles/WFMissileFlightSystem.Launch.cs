using System.Numerics;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Shared._WF.ShipMissiles;

namespace Content.Server._WF.ShipMissiles;

/// <summary>Launch side of a missile: the bearing it is handed at release, and the wider lock it gets while stabilising.</summary>
public sealed partial class WFMissileFlightSystem
{
    /// <summary>
    /// The direction the release impulse goes in: the designated point if the console named one, else the locked
    /// target, clamped to what the missile can take off the rail and blended by <see cref="WFMissileFlightComponent.SnapFraction"/>.
    /// </summary>
    private Vector2 LaunchBearing(TransformComponent xform, WFMissileFlightComponent flight,
        TargetSeekingComponent? carrier, Vector2 ownPos, Vector2 nose)
    {
        var goal = nose;

        if (flight.Designated is { } designated && designated.MapId == xform.MapID)
            goal = designated.Position - ownPos;
        else if (carrier?.CurrentTarget is { } target && !TerminatingOrDeleted(target))
        {
            var targetXform = Transform(target);
            if (targetXform.MapID == xform.MapID)
                goal = _transform.GetWorldPosition(targetXform) - ownPos;
        }

        if (goal.LengthSquared() <= 0f)
            return nose;

        goal = MissileGuidance.ClampBearing(nose, goal, flight.LaunchOffBoresight);
        return MissileGuidance.BlendDirection(nose, goal, flight.SnapFraction);
    }

    /// <summary>
    /// Locks the nearest seekable hull inside the transient's wider cone, which upstream's own narrow scan arc would
    /// miss. Once the transient ends, upstream's rules take over again.
    /// </summary>
    private void AcquireAtLaunch(EntityUid uid, TransformComponent xform, WFMissileFlightComponent flight,
        TargetSeekingComponent carrier, Vector2 ownPos)
    {
        // A missile without diverters can't steer onto a lock, so it must not raise one.
        if (flight.LaunchAcquireCone <= 0f || flight.LaunchAcquireRange <= 0f || flight.Divert0 <= 0f ||
            carrier.SeekingDisabled)
            return;

        // Capture from the fire-control click before scoring, so the first pick is descriptor-aware.
        // Fire-path capture usually runs first; this is the fallback for direct spawns and clicks in empty space.
        if (flight.DescriptorSeeking && !flight.DescriptorCaptured && flight.Designated is { } designated)
            CaptureDescriptorFromCoordinate(flight, designated, xform);

        var nose = _transform.GetWorldRotation(xform).ToWorldVec();
        var shooterGrid = _projectiles.TryGetComponent(uid, out var projectile) && projectile.Shooter is { } shooter
            ? Transform(shooter).GridUid
            : null;

        EntityUid? best = null;
        var bestCost = float.MaxValue;

        var query = EntityQueryEnumerator<TargetSeekingTargetComponent, TransformComponent>();
        while (query.MoveNext(out var markerUid, out _, out var markerXform))
        {
            if (markerXform.MapID != xform.MapID)
                continue;

            var position = _transform.GetWorldPosition(markerXform);
            var distance = Vector2.Distance(ownPos, position);
            if (distance > flight.LaunchAcquireRange)
                continue;

            if (!MissileGuidance.WithinCone(position - ownPos, nose, flight.LaunchAcquireCone))
                continue;

            var target = markerXform.GridUid ?? markerUid;
            if (markerXform.GridUid != null && markerXform.GridUid == shooterGrid)
                continue;

            if (_wfHack.IsLockedOut(shooterGrid, target))
                continue;

            var cost = CostOf(flight, target, markerXform, distance);
            if (cost >= bestCost)
                continue;

            best = target;
            bestCost = cost;
        }

        if (best != null)
        {
            _targetSeeking.SetSeekerTarget((uid, carrier), best.Value, xform);
            if (flight.DescriptorSeeking && !flight.DescriptorCaptured)
                TargetDescriptorCapture.FromEntity(EntityManager, _physics, _transform, flight, best.Value);
        }
    }

    /// <summary>Cost of a candidate for acquisition: descriptor match for smart missiles, distance otherwise.</summary>
    private float CostOf(WFMissileFlightComponent flight, EntityUid target, TransformComponent targetXform, float distance)
    {
        if (!flight.DescriptorSeeking || !flight.DescriptorCaptured)
            return distance;

        _bodies.TryGetComponent(target, out var targetBody);
        var targetVel = _physics.GetMapLinearVelocity(target, targetBody, targetXform);
        var targetRadius = TargetDescriptorCapture.Radius(EntityManager, target, targetXform);
        var match = MissileGuidance.DescriptorMatch(
            targetVel, targetRadius,
            flight.DescriptorVelocity, flight.DescriptorRadius,
            flight.DescriptorSizeTolerance, flight.DescriptorSpeedTolerance, flight.DescriptorHeadingTolerance);
        return match + distance * 0.001f;
    }
}
