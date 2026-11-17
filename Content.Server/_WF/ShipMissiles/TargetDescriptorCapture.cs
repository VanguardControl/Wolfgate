using System.Numerics;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.ShipMissiles;

/// <summary>Snapshots a target's identity onto a <see cref="WFMissileFlightComponent"/>.</summary>
public static class TargetDescriptorCapture
{
    /// <summary>Approximate radius of a target's hull, or zero when it has none.</summary>
    public static float Radius(IEntityManager entities, EntityUid targetUid, TransformComponent xform)
    {
        var gridUid = xform.GridUid
                      ?? (entities.HasComponent<MapGridComponent>(targetUid) ? targetUid : null);
        if (gridUid != null && entities.TryGetComponent<MapGridComponent>(gridUid.Value, out var grid))
        {
            var aabb = grid.LocalAABB;
            return MathF.Max(aabb.Width, aabb.Height) * 0.5f;
        }
        return 0f;
    }

    /// <summary>Snapshot the target onto the missile's descriptor, if not yet captured.</summary>
    public static void FromEntity(
        IEntityManager entities, SharedPhysicsSystem physics, SharedTransformSystem xform,
        WFMissileFlightComponent flight, EntityUid targetUid)
    {
        if (flight.DescriptorCaptured || !entities.EntityExists(targetUid))
            return;

        var targetXform = entities.GetComponent<TransformComponent>(targetUid);
        entities.TryGetComponent<PhysicsComponent>(targetUid, out var body);

        flight.DescriptorPosition = xform.GetWorldPosition(targetXform);
        flight.DescriptorVelocity = physics.GetMapLinearVelocity(targetUid, body, targetXform);
        flight.DescriptorGrid = targetXform.GridUid ?? targetUid;
        flight.DescriptorRadius = Radius(entities, targetUid, targetXform);
        flight.DescriptorCaptured = true;
    }
}
