using System.Numerics;
using Content.Shared.Ghost;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.LockOn;

public sealed partial class WFLockOnSystem
{
    /// <summary>
    /// Finds what a radar click landed on. A free entity on the clicked tile wins over the grid beneath it, so
    /// rocks and debris can be locked directly; otherwise the grid itself is the target, so a ship or asteroid
    /// locks as one thing. The ship's own grid and anything on it are never valid targets.
    /// </summary>
    public EntityUid? ResolveTarget(EntityUid console, EntityUid ship, MapCoordinates point)
    {
        if (point.MapId == MapId.Nullspace ||
            !float.IsFinite(point.Position.X) ||
            !float.IsFinite(point.Position.Y))
            return null;

        var half = new Vector2(ClickRadius, ClickRadius);
        var box = new Box2(point.Position - half, point.Position + half);
        var found = new HashSet<EntityUid>();
        _lookup.GetEntitiesIntersecting(point.MapId, box, found,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries);

        EntityUid? free = null;
        var freeDistance = float.MaxValue;
        EntityUid? onGrid = null;
        var onGridDistance = float.MaxValue;

        foreach (var uid in found)
        {
            if (uid == console ||
                TerminatingOrDeleted(uid) ||
                !EntityManager.EntityExists(uid) ||
                HasComp<MapComponent>(uid) ||
                HasComp<GhostComponent>(uid))
                continue;

            if (!TryComp<TransformComponent>(uid, out var xform) || xform.MapID != point.MapId)
                continue;

            // Everything aboard the ship doing the locking is off limits, including its own hull.
            if (xform.GridUid == ship)
                continue;

            var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(xform), point.Position);
            if (xform.GridUid == null)
            {
                if (distance < freeDistance)
                {
                    free = uid;
                    freeDistance = distance;
                }
            }
            else if (distance < onGridDistance)
            {
                onGrid = xform.GridUid;
                onGridDistance = distance;
            }
        }

        if (free is { } entity)
            return entity;

        if (_map.TryFindGridAt(point, out var gridUid, out _) && gridUid != ship && !TerminatingOrDeleted(gridUid))
            return gridUid;

        return onGrid;
    }
}
