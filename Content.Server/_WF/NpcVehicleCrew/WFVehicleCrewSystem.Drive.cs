using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Robust.Shared.Map;

namespace Content.Server._WF.NpcVehicleCrew;

/// <summary>The driver: follows a route to the goal through the autopilot, backs out of stuck spots and plans paths.</summary>
public sealed partial class WFVehicleCrewSystem
{
    /// <summary>How near a waypoint counts as reached.</summary>
    private const float ArriveDistance = 1.5f;

    /// <summary>Heading error at which the wheel is turned all the way.</summary>
    private const float SteerFullErrorDegrees = 30f;

    /// <summary>Heading error past which the driver slows for the turn.</summary>
    private const float HardTurnDegrees = 60f;

    /// <summary>A waypoint behind the vehicle and this close is reversed to.</summary>
    private const float ReverseRange = 3f;

    private const float HardTurnSpeed = 2.5f;

    /// <summary>Top speed over the last stretch to the goal, so the vehicle stops on it rather than past it.</summary>
    private const float ApproachSpeed = 3f;

    private const float ApproachDistance = 5f;

    /// <summary>Speed below which a vehicle with the throttle held is not getting anywhere.</summary>
    private const float StuckSpeed = 0.2f;

    /// <summary>Speed below which a braking vehicle lets go rather than start rolling the other way.</summary>
    private const float BrakeStopSpeed = 0.3f;

    private static readonly TimeSpan StuckTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StuckReverseTime = TimeSpan.FromSeconds(1.5);

    /// <summary>Stucks on one leg after which it is skipped.</summary>
    private const int MaxStucks = 3;

    /// <summary>A goal this close is driven to straight, without a path.</summary>
    private const float DirectRange = 4f;

    /// <summary>How far a goal may move before its route is planned again.</summary>
    private const float RepathDrift = 2f;

    private static readonly TimeSpan RepathInterval = TimeSpan.FromSeconds(5);

    /// <summary>How near the path's end the pathfinder must get.</summary>
    private const float PathRange = 1.5f;

    /// <summary>Every this many path polygons is kept as a waypoint.</summary>
    private const int PathStride = 3;

    /// <summary>Hull bearing error a stopped vehicle turning to face a target accepts.</summary>
    private const float FaceToleranceDegrees = 5f;

    /// <summary>Steers the vehicle one tick toward <see cref="WFVehicleCrewComponent.Goal"/>, or holds it still.</summary>
    private void Drive(EntityUid uid, WFVehicleCrewComponent crew, TimeSpan now)
    {
        if (!TryComp<WFVehicleAutopilotComponent>(uid, out var autopilot) ||
            !TryComp<GridVehicleMoverComponent>(uid, out var mover))
        {
            return;
        }

        autopilot.Active = true;
        var here = _transform.GetMapCoordinates(uid);
        if (crew.Goal is not { } goal || goal.MapId != here.MapId)
        {
            crew.StuckSince = null;
            crew.Route.Clear();
            crew.RouteGoal = null;
            if (TryBrake(autopilot, mover))
                return;

            if (crew.Face is { } face && face.MapId == here.MapId)
                Turn(uid, mover, autopilot, here, face);
            else
                SetInput(autopilot, mover, 0f, 0f, Vector2i.Zero);
            return;
        }

        if (TryComp<WFVehicleEngineComponent>(uid, out var engine) && (!engine.Running || engine.ParkingBrake))
            _engine.TryStartForNpc((uid, engine));

        UpdateRoute(uid, crew, here, goal, now);

        var point = goal;
        while (crew.Route.Count > 0)
        {
            if (Distance(here, crew.Route[0]) > ArriveDistance)
            {
                point = crew.Route[0];
                break;
            }

            crew.Route.RemoveAt(0);
            crew.StuckCount = 0;
        }

        var finalDistance = Distance(here, goal);
        if (crew.Route.Count == 0 && finalDistance <= ArriveDistance)
        {
            crew.StuckSince = null;
            crew.StuckCount = 0;
            if (!TryBrake(autopilot, mover))
                SetInput(autopilot, mover, 0f, 0f, Vector2i.Zero);
            return;
        }

        if (now < crew.ReverseUntil)
        {
            SetInput(autopilot, mover, -1f, crew.ReverseSteering, -mover.CurrentDirection);
            return;
        }

        var delta = point.Position - here.Position;
        var heading = _transform.GetWorldRotation(uid);
        var bearing = delta.ToWorldAngle();
        var error = Angle.ShortestDistance(heading, bearing).Degrees;
        float throttle;
        float steering;
        if (Math.Abs(error) > 90 && delta.Length() <= ReverseRange)
        {
            // Close behind: back up to it, swinging the tail toward it. Steering flips in reverse, like a car's.
            var rearError = Angle.ShortestDistance(heading + new Angle(Math.PI), bearing).Degrees;
            throttle = -1f;
            steering = Math.Clamp(-(float) rearError / SteerFullErrorDegrees, -1f, 1f);
        }
        else
        {
            steering = Math.Clamp((float) error / SteerFullErrorDegrees, -1f, 1f);
            throttle = 1f;

            // The mover takes any throttle as full, so speed is held down by braking (coasting barely slows a vehicle).
            var cap = Math.Abs(error) >= HardTurnDegrees ? HardTurnSpeed : float.MaxValue;
            if (crew.Route.Count == 0 && finalDistance < ApproachDistance)
                cap = MathF.Min(cap, ApproachSpeed);
            if (MathF.Abs(mover.CurrentSpeed) > cap)
            {
                crew.StuckSince = null;
                TryBrake(autopilot, mover);
                return;
            }
        }

        UpdateStuck(crew, mover, throttle, steering, now);
        SetInput(autopilot, mover, throttle, steering, GridCardinal(uid, delta));
    }

    /// <summary>Brakes a rolling vehicle by asking for the opposite direction, which the chassis takes as a stop; false once it is as good as stopped.</summary>
    private static bool TryBrake(WFVehicleAutopilotComponent autopilot, GridVehicleMoverComponent mover)
    {
        if (MathF.Abs(mover.CurrentSpeed) <= BrakeStopSpeed)
            return false;

        var against = mover.CurrentSpeed > 0f ? -1f : 1f;
        SetInput(autopilot, mover, against, 0f, mover.CurrentSpeed > 0f ? -mover.CurrentDirection : mover.CurrentDirection);
        return true;
    }

    /// <summary>Turns a stopped vehicle's hull toward a point, for a gun that cannot traverse; only a chassis that turns on the spot can.</summary>
    private void Turn(EntityUid uid, GridVehicleMoverComponent mover, WFVehicleAutopilotComponent autopilot,
        MapCoordinates here, MapCoordinates face)
    {
        var delta = face.Position - here.Position;
        if (!mover.FreeSteering || !mover.TurnInPlace || delta.LengthSquared() < 0.01f)
        {
            SetInput(autopilot, mover, 0f, 0f, Vector2i.Zero);
            return;
        }

        var error = Angle.ShortestDistance(_transform.GetWorldRotation(uid), delta.ToWorldAngle()).Degrees;
        var steering = Math.Abs(error) <= FaceToleranceDegrees ? 0f : Math.Clamp((float) error / SteerFullErrorDegrees, -1f, 1f);
        SetInput(autopilot, mover, 0f, steering, Vector2i.Zero);
    }

    /// <summary>Starts backing out once the throttle has been held for <see cref="StuckTime"/> without moving; three times skips the leg.</summary>
    private void UpdateStuck(WFVehicleCrewComponent crew, GridVehicleMoverComponent mover, float throttle, float steering, TimeSpan now)
    {
        if (throttle == 0f || MathF.Abs(mover.CurrentSpeed) >= StuckSpeed)
        {
            crew.StuckSince = null;
            return;
        }

        crew.StuckSince ??= now;
        if (now - crew.StuckSince.Value < StuckTime)
            return;

        crew.StuckSince = null;
        crew.ReverseUntil = now + StuckReverseTime;
        crew.ReverseSteering = steering > 0f ? -1f : 1f;
        if (++crew.StuckCount < MaxStucks)
        {
            crew.RouteGoal = null;
            return;
        }

        crew.StuckCount = 0;
        if (crew.Route.Count > 0)
            crew.Route.RemoveAt(0);
        else if (crew.Behaviour == WFVehicleBehaviour.Patrol && crew.Waypoints.Count > 0)
            crew.WaypointIndex = (crew.WaypointIndex + 1) % crew.Waypoints.Count;
    }

    /// <summary>Writes the controls the vehicle's chassis reads: throttle and wheel when it steers freely, a grid cardinal otherwise.</summary>
    private static void SetInput(WFVehicleAutopilotComponent autopilot, GridVehicleMoverComponent mover, float throttle,
        float steering, Vector2i direction)
    {
        if (mover.FreeSteering)
        {
            autopilot.Throttle = throttle;
            autopilot.Steering = steering;
            autopilot.Direction = Vector2i.Zero;
            return;
        }

        autopilot.Throttle = 0f;
        autopilot.Steering = 0f;
        autopilot.Direction = throttle == 0f ? Vector2i.Zero : direction;
    }

    /// <summary>The cardinal of the vehicle's grid nearest a world-space direction.</summary>
    private Vector2i GridCardinal(EntityUid uid, Vector2 worldDelta)
    {
        if (worldDelta.LengthSquared() < 0.0001f)
            return Vector2i.Zero;

        var gridRotation = Transform(uid).GridUid is { } grid ? _transform.GetWorldRotation(grid) : Angle.Zero;
        var local = (-gridRotation).RotateVec(worldDelta);
        return MathF.Abs(local.X) >= MathF.Abs(local.Y)
            ? new Vector2i(MathF.Sign(local.X), 0)
            : new Vector2i(0, MathF.Sign(local.Y));
    }

    /// <summary>Plans a route when the goal is new or has moved, at most every <see cref="RepathInterval"/> for a moving one.</summary>
    private void UpdateRoute(EntityUid uid, WFVehicleCrewComponent crew, MapCoordinates here, MapCoordinates goal, TimeSpan now)
    {
        if (crew.RouteGoal is { } planned && Distance(planned, goal) <= RepathDrift)
            return;

        if (crew.RouteGoal != null && now < crew.NextRepath)
            return;

        crew.RouteGoal = goal;
        crew.NextRepath = now + RepathInterval;
        crew.Route.Clear();
        crew.PathCancel?.Cancel();
        crew.PathCancel = null;

        // Without a path the driver heads straight for the goal, which is all a short hop needs.
        if (Distance(here, goal) > DirectRange)
            RequestPath(uid, crew, goal);
    }

    /// <summary>Asks the pathfinder for a route across the vehicle's grid; nothing happens off a grid with a nav graph.</summary>
    private void RequestPath(EntityUid uid, WFVehicleCrewComponent crew, MapCoordinates goal)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !HasComp<GridPathfindingComponent>(grid))
            return;

        var cancel = new CancellationTokenSource();
        crew.PathCancel = cancel;
        _ = FollowPath(uid, cancel, xform.Coordinates, _transform.ToCoordinates(grid, goal));
    }

    /// <summary>Waits for a path and makes it the route, unless it was superseded meanwhile.</summary>
    private async Task FollowPath(EntityUid uid, CancellationTokenSource cancel, EntityCoordinates start, EntityCoordinates end)
    {
        PathResultEvent result;
        try
        {
            result = await _pathfinding.GetPathSafe(uid, start, end, PathRange, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancel.IsCancellationRequested ||
            !TryComp<WFVehicleCrewComponent>(uid, out var crew) ||
            crew.PathCancel != cancel)
        {
            return;
        }

        crew.PathCancel = null;
        if (result.Result is not (PathResult.Path or PathResult.PartialPath) || result.Path.Count == 0)
            return;

        crew.Route.Clear();
        for (var i = PathStride - 1; i < result.Path.Count; i += PathStride)
        {
            crew.Route.Add(_transform.ToMapCoordinates(result.Path[i].Coordinates));
        }

        if (result.Path.Count % PathStride != 0)
            crew.Route.Add(_transform.ToMapCoordinates(result.Path[^1].Coordinates));
    }
}
