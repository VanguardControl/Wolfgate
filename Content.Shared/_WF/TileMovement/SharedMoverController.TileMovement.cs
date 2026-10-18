using System.Numerics;
using Content.Shared._WF.TileMovement;
using Content.Shared.Doors.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    /// <summary>
    /// Distance from a tile centre that counts as on it.
    /// </summary>
    private const float TileMoveEpsilon = 0.01f;

    /// <summary>
    /// Replaces free movement with SS13-style stepping between tile centres for <see cref="WFTileMovementComponent"/>.
    /// </summary>
    private void ApplyTileMovement(
        EntityUid uid,
        TransformComponent xform,
        PhysicsComponent body,
        ref Vector2 velocity,
        ref Vector2 wishDir,
        float frameTime)
    {
        if (!TryComp<WFTileMovementComponent>(uid, out var tile))
            return;

        // Off-grid or parented to something else (lockers, vehicles): move freely.
        if (xform.GridUid is not { } grid
            || xform.ParentUid != grid
            || !MapGridQuery.TryComp(grid, out var gridComp))
        {
            StopTileStep(uid, tile);
            return;
        }

        if (tile.Grid != grid)
            StopTileStep(uid, tile);

        var gridRot = _transform.GetWorldRotation(xform.ParentUid);
        var speed = wishDir.Length();
        var dir = SnapTileDirection((-gridRot).RotateVec(wishDir));
        var pos = xform.LocalPosition;
        var tileSize = gridComp.TileSize;
        var now = Timing.CurTime;

        if (tile.Moving)
        {
            var span = (tile.Target - tile.Origin).Length();

            // Knocked well off the path (thrown, shoved): drop the step.
            if ((tile.Target - pos).Length() > span + tileSize * 0.5f)
                StopTileStep(uid, tile);
            else if (dir != Vector2i.Zero && dir == -tile.Direction && !tile.Returning)
                ReverseTileStep(uid, tile, dir, now);
            else if (now > tile.Deadline)
            {
                if (tile.Returning)
                    StopTileStep(uid, tile);
                else
                {
                    ReverseTileStep(uid, tile, -tile.Direction, now);
                    tile.Returning = true;
                }
            }
        }

        if (!tile.Moving)
        {
            var center = TileCenter(pos, tileSize);

            if (dir == Vector2i.Zero || speed <= 0f)
            {
                velocity = gridRot.RotateVec(MoveToward(center - pos, tile.SettleSpeed, frameTime));
                wishDir = Vector2.Zero;
                return;
            }

            if (!TryPickTileStep(grid, gridComp, uid, center, dir, out var stepDir))
            {
                // Blocked: face the way but stay put.
                velocity = gridRot.RotateVec(MoveToward(center - pos, tile.SettleSpeed, frameTime));
                wishDir = gridRot.RotateVec(((Vector2) dir).Normalized()) * speed;
                return;
            }

            StartTileStep(uid, tile, grid, center, stepDir, speed, tileSize, now);
        }

        var remaining = tile.Target - pos;
        var remainingLength = remaining.Length();
        // Live speed while the key is held so slowdowns apply mid-step; the stored one finishes the step otherwise.
        var stepSpeed = speed > 0f ? speed : tile.Speed;
        var travel = stepSpeed * frameTime;
        Vector2 localVelocity;

        if (remainingLength <= travel)
        {
            // Arriving this tick: chain into the next tile if the key is still held.
            if (!tile.Returning
                && dir != Vector2i.Zero
                && speed > 0f
                && TryPickTileStep(grid, gridComp, uid, tile.Target, dir, out var nextDir))
            {
                var arrived = tile.Target;
                StartTileStep(uid, tile, grid, arrived, nextDir, speed, tileSize, now);
                var leftover = travel - remainingLength;
                localVelocity = (remaining + ((Vector2) nextDir).Normalized() * leftover) / frameTime;
            }
            else
            {
                StopTileStep(uid, tile);
                localVelocity = remaining / frameTime;
            }
        }
        else
        {
            localVelocity = remaining / remainingLength * stepSpeed;
        }

        velocity = gridRot.RotateVec(localVelocity);
        wishDir = tile.Moving
            ? gridRot.RotateVec(((Vector2) tile.Direction).Normalized()) * stepSpeed
            : Vector2.Zero;
    }

    private void StartTileStep(
        EntityUid uid,
        WFTileMovementComponent tile,
        EntityUid grid,
        Vector2 origin,
        Vector2i dir,
        float speed,
        float tileSize,
        TimeSpan now)
    {
        var step = (Vector2) dir * tileSize;
        tile.Moving = true;
        tile.Returning = false;
        tile.Grid = grid;
        tile.Origin = origin;
        tile.Target = origin + step;
        tile.Direction = dir;
        tile.Speed = speed;
        tile.Deadline = now + TileStepAllowance(step.Length(), speed);
        Dirty(uid, tile);
    }

    private void ReverseTileStep(EntityUid uid, WFTileMovementComponent tile, Vector2i dir, TimeSpan now)
    {
        (tile.Origin, tile.Target) = (tile.Target, tile.Origin);
        tile.Direction = dir;
        tile.Deadline = now + TileStepAllowance((tile.Target - tile.Origin).Length(), tile.Speed);
        Dirty(uid, tile);
    }

    private void StopTileStep(EntityUid uid, WFTileMovementComponent tile)
    {
        if (!tile.Moving && tile.Grid == null)
            return;

        tile.Moving = false;
        tile.Returning = false;
        tile.Grid = null;
        Dirty(uid, tile);
    }

    /// <summary>
    /// Time a step may take before it counts as blocked.
    /// </summary>
    private static TimeSpan TileStepAllowance(float distance, float speed)
    {
        return TimeSpan.FromSeconds(distance / MathF.Max(speed, 0.1f) * 2f + 0.25f);
    }

    /// <summary>
    /// Picks the step for a held direction, falling back to its cardinal parts when a diagonal is walled off.
    /// </summary>
    private bool TryPickTileStep(
        EntityUid grid,
        MapGridComponent gridComp,
        EntityUid mob,
        Vector2 from,
        Vector2i dir,
        out Vector2i stepDir)
    {
        stepDir = dir;
        if (!IsTileBlocked(grid, gridComp, mob, from + (Vector2) dir * gridComp.TileSize))
            return true;

        if (dir.X == 0 || dir.Y == 0)
            return false;

        foreach (var part in new[] { new Vector2i(dir.X, 0), new Vector2i(0, dir.Y) })
        {
            if (IsTileBlocked(grid, gridComp, mob, from + (Vector2) part * gridComp.TileSize))
                continue;

            stepDir = part;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a hard fixture the mob collides with (a wall, not a door or wallmount) covers the tile centre.
    /// </summary>
    private bool IsTileBlocked(EntityUid grid, MapGridComponent gridComp, EntityUid mob, Vector2 center)
    {
        if (!TryComp<FixturesComponent>(mob, out var mobFixtures))
            return false;

        // Sensors (slips, flammability) carry masks the mob doesn't physically collide with.
        var mobLayer = 0;
        var mobMask = 0;
        foreach (var fixture in mobFixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            mobLayer |= fixture.CollisionLayer;
            mobMask |= fixture.CollisionMask;
        }

        var indices = new Vector2i(
            (int) MathF.Floor(center.X / gridComp.TileSize),
            (int) MathF.Floor(center.Y / gridComp.TileSize));

        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(grid, gridComp, indices);
        while (anchored.MoveNext(out var ent))
        {
            if (!PhysicsQuery.TryComp(ent, out var other)
                || !other.CanCollide
                || HasComp<DoorComponent>(ent)
                || !TryComp<FixturesComponent>(ent, out var fixtures))
            {
                continue;
            }

            var entXform = XformQuery.GetComponent(ent.Value);
            var transform = new Transform(entXform.LocalPosition, entXform.LocalRotation);
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard
                    || ((fixture.CollisionLayer & mobMask) == 0 && (fixture.CollisionMask & mobLayer) == 0))
                {
                    continue;
                }

                for (var i = 0; i < fixture.Shape.ChildCount; i++)
                {
                    if (fixture.Shape.ComputeAABB(transform, i).Contains(center))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Snaps an input vector to one of the eight neighbour directions.
    /// </summary>
    private static Vector2i SnapTileDirection(Vector2 input)
    {
        var length = input.Length();
        if (length <= 0f)
            return Vector2i.Zero;

        // sin(22.5°): inputs within 22.5° of an axis drop the other component.
        const float threshold = 0.38f;
        var x = MathF.Abs(input.X) > length * threshold ? Math.Sign(input.X) : 0;
        var y = MathF.Abs(input.Y) > length * threshold ? Math.Sign(input.Y) : 0;
        return new Vector2i(x, y);
    }

    private static Vector2 TileCenter(Vector2 pos, float tileSize)
    {
        return new Vector2(
            (MathF.Floor(pos.X / tileSize) + 0.5f) * tileSize,
            (MathF.Floor(pos.Y / tileSize) + 0.5f) * tileSize);
    }

    /// <summary>
    /// Velocity that covers <paramref name="offset"/> at up to <paramref name="speed"/> without overshooting.
    /// </summary>
    private static Vector2 MoveToward(Vector2 offset, float speed, float frameTime)
    {
        var length = offset.Length();
        if (length <= TileMoveEpsilon)
            return Vector2.Zero;

        return length <= speed * frameTime
            ? offset / frameTime
            : offset / length * speed;
    }
}
