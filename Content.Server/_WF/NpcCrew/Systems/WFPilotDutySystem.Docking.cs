using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// A hand-flown docking: the dock pair, our grid's pose with the docks mated and the standoff the creep starts from.
/// Positions are on the target grid and the angle is relative to it, so the plan moves with the target.
/// </summary>
public readonly record struct WFDockPlan(
    EntityUid OwnDock,
    EntityUid TargetDock,
    EntityCoordinates Final,
    Angle FinalAngle,
    EntityCoordinates Standoff);

public sealed partial class WFPilotDutySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    /// <summary>How close to the standoff counts as there.</summary>
    private const float StandoffRange = 2f;

    /// <summary>How close to the final pose the creep aims to get.</summary>
    private const float CreepRange = 0.3f;

    /// <summary>Speed relative to the target under which the ship has settled at the standoff, in m/s.</summary>
    private const float SettleSpeed = 0.3f;

    /// <summary>Turn rate under which the ship has settled at the standoff, in rad/s.</summary>
    private const float SettleTurnRate = 0.05f;

    /// <summary>Seconds before planning again after an attempt ended without a usable pair.</summary>
    private const float PlanRetryDelay = 5f;

    /// <summary>Touching the target's hull with the docks this close is the docks mating, not a collision.</summary>
    private const float PortContactRange = 2.5f;

    private List<Entity<MapGridComponent>> _laneGrids = new();

    /// <summary>
    /// Picks a free dock pair whose approach lane touches no grid but the two, nearest standoff first. The lane is the
    /// box our grid sweeps from its final pose out <paramref name="standoff"/> metres along the target dock's normal.
    /// </summary>
    public bool TryPlanDock(EntityUid grid, EntityUid target, float standoff, out WFDockPlan plan)
    {
        plan = default;
        var gridXform = Transform(grid);
        if (grid == target
            || !TryComp<MapGridComponent>(grid, out var gridComp)
            || !HasComp<MapGridComponent>(target)
            || gridXform.MapUid == null
            || gridXform.MapID != Transform(target).MapID)
        {
            return false;
        }

        var position = _transform.GetWorldPosition(gridXform);
        var targetMatrix = _transform.GetWorldMatrix(target);
        var targetRotation = _transform.GetWorldRotation(target);
        var targetDocks = _docking.GetDocks(target);
        var best = float.MaxValue;
        var found = false;

        foreach (var own in _docking.GetDocks(grid))
        {
            if (own.Comp.Docked || own.Comp.ReceiveOnly)
                continue;

            foreach (var theirs in targetDocks)
            {
                if (theirs.Comp.Docked
                    || _docking.GetDockingConfig(grid, target, own, own.Comp, theirs, theirs.Comp) is not { } config)
                {
                    continue;
                }

                // The target dock's outward normal, on the target grid.
                var normal = Transform(theirs).LocalRotation.RotateVec(new Vector2(0f, -1f));
                var final = config.Coordinates.Position;
                var standoffPoint = final + normal * standoff;
                var distance = (Vector2.Transform(standoffPoint, targetMatrix) - position).LengthSquared();
                if (distance >= best
                    || !LaneClear(grid,
                        target,
                        gridXform.MapID,
                        gridComp.LocalAABB,
                        Vector2.Transform(final, targetMatrix),
                        targetRotation + config.Angle,
                        targetRotation.RotateVec(normal),
                        standoff))
                {
                    continue;
                }

                best = distance;
                found = true;
                plan = new WFDockPlan(own,
                    theirs,
                    new EntityCoordinates(target, final),
                    config.Angle,
                    new EntityCoordinates(target, standoffPoint));
            }
        }

        return found;
    }

    /// <summary>
    /// Whether the box our grid's bounds sweep from the final pose out along the normal touches no grid but ours and
    /// the target. World space.
    /// </summary>
    private bool LaneClear(EntityUid grid,
        EntityUid target,
        MapId map,
        Box2 localBounds,
        Vector2 finalPosition,
        Angle finalRotation,
        Vector2 normal,
        float standoff)
    {
        var across = new Vector2(-normal.Y, normal.X);
        var xAxis = finalRotation.RotateVec(Vector2.UnitX);
        var yAxis = finalRotation.RotateVec(Vector2.UnitY);
        var half = localBounds.Size / 2f;
        var halfAlong = MathF.Abs(Vector2.Dot(normal, xAxis)) * half.X
                        + MathF.Abs(Vector2.Dot(normal, yAxis)) * half.Y;
        var halfAcross = MathF.Abs(Vector2.Dot(across, xAxis)) * half.X
                         + MathF.Abs(Vector2.Dot(across, yAxis)) * half.Y;
        var centre = finalPosition + finalRotation.RotateVec(localBounds.Center);

        // Laid out along +Y from the centre, then turned so +Y is the normal.
        var box = new Box2(-halfAcross, -halfAlong, halfAcross, standoff + halfAlong).Translated(centre);
        var lane = new Box2Rotated(box, new Angle(normal) - new Angle(Vector2.UnitY), centre);

        _laneGrids.Clear();
        _mapManager.FindGridsIntersecting(map, lane, ref _laneGrids, includeMap: false);
        return _laneGrids.All(other => other.Owner == grid || other.Owner == target);
    }

    /// <summary>One update of the Dock order: plan, approach the standoff, settle there, creep in and dock.</summary>
    private void AdvanceDock(Entity<WFPilotDutyComponent> ent, ShipSteererComponent steerer, float frameTime)
    {
        var duty = ent.Comp;
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || xform.MapUid is not { } map)
            return;

        if (duty.DockTarget is not { } target
            || TerminatingOrDeleted(target)
            || target == grid
            || Transform(target).MapUid != map)
        {
            FailDock(ent, grid, duty.DockTarget ?? EntityUid.Invalid);
            return;
        }

        duty.DockPhaseTime += frameTime;
        var collision = duty.DockCollision;
        duty.DockCollision = null;

        if (duty.DockPhase == WFDockPhase.None || duty.DockPlan is not { } plan)
        {
            PlanDock(ent, grid, target);
            return;
        }

        // Docked at the chosen pair by someone else.
        if (TryComp<DockingComponent>(plan.OwnDock, out var docked) && docked.DockedWith == plan.TargetDock)
        {
            Docked(ent, grid, target);
            return;
        }

        if (!TryFreeDock(plan.OwnDock, out var ownDock) || !TryFreeDock(plan.TargetDock, out var targetDock))
        {
            duty.DockPlan = null;
            AbortDock(ent, WFDockPhase.None);
            return;
        }

        steerer.InRangeRotation = DockHeading(plan, target);
        switch (duty.DockPhase)
        {
            case WFDockPhase.Approach:
                // Map coordinates, so the target's hull is avoided on the way; kept up with a moving target.
                steerer.Coordinates = StandoffOnMap(plan, map);
                if (steerer.Status != ShipSteeringStatus.InRange)
                    break;

                // Out of attempts: give up here, at the standoff.
                if (duty.DockAttempts >= duty.DockMaxAttempts)
                {
                    GiveUpDock(ent, grid, target);
                    return;
                }

                SetDockPhase(ent, WFDockPhase.Settle);
                break;
            case WFDockPhase.Settle:
                steerer.Coordinates = StandoffOnMap(plan, map);
                if (Settled(grid, target))
                    SetDockPhase(ent, WFDockPhase.Creep);
                break;
            case WFDockPhase.Creep:
                steerer.TargetRotation = CreepHeadingOffset(steerer, DockHeading(plan, target), grid);
                if (_docking.CanDock((plan.OwnDock, ownDock), (plan.TargetDock, targetDock)))
                {
                    _docking.Dock((plan.OwnDock, ownDock), (plan.TargetDock, targetDock));
                    Docked(ent, grid, target);
                    return;
                }

                var hitElsewhere = collision is { } hit
                                   && (hit != target || PortDistance(plan) > PortContactRange);
                if (hitElsewhere || duty.DockPhaseTime >= duty.DockCreepTimeout)
                    AbortDock(ent, WFDockPhase.Approach);
                break;
        }
    }

    /// <summary>
    /// Plans an attempt, or gives up once the attempts are spent. A plan that finds no pair is a failed attempt.
    /// </summary>
    private void PlanDock(Entity<WFPilotDutyComponent> ent, EntityUid grid, EntityUid target)
    {
        var duty = ent.Comp;
        if (duty.DockAttempts >= duty.DockMaxAttempts)
        {
            GiveUpDock(ent, grid, target);
            return;
        }

        if (duty.DockAttempts > 0 && duty.DockPhaseTime < PlanRetryDelay)
            return;

        if (!TryPlanDock(grid, target, duty.DockStandoff, out var plan))
        {
            duty.DockAttempts++;
            duty.DockPhaseTime = 0f;
            return;
        }

        duty.DockPlan = plan;
        SetDockPhase(ent, WFDockPhase.Approach);
    }

    /// <summary>Counts a failed attempt and goes back: Approach retries the pair, None plans another.</summary>
    private void AbortDock(Entity<WFPilotDutyComponent> ent, WFDockPhase phase)
    {
        ent.Comp.DockAttempts++;
        SetDockPhase(ent, phase);
    }

    private void SetDockPhase(Entity<WFPilotDutyComponent> ent, WFDockPhase phase)
    {
        ent.Comp.DockPhase = phase;
        ent.Comp.DockPhaseTime = 0f;
        Steer(ent);
    }

    /// <summary>Out of attempts: jump onto a port if the server allows it, else hold where the ship is.</summary>
    private void GiveUpDock(Entity<WFPilotDutyComponent> ent, EntityUid grid, EntityUid target)
    {
        // Not TryFTLDock: with no pair that fits it hops the ship next to the target, which would hide the failure.
        if (_cfg.GetCVar(NpcCrewCVars.DockFtlFallback) && _docking.GetDockingConfig(grid, target) is { } config)
        {
            _shuttle.FTLDock((grid, Transform(grid)), config);
            Docked(ent, grid, target);
            return;
        }

        FailDock(ent, grid, target);
    }

    private void FailDock(Entity<WFPilotDutyComponent> ent, EntityUid grid, EntityUid target)
    {
        SetOrders(ent, WFPilotOrder.Hold);
        var ev = new WFPilotDockFailedEvent(ent, grid, target);
        RaiseLocalEvent(ent, ref ev, true);
    }

    private void Docked(Entity<WFPilotDutyComponent> ent, EntityUid grid, EntityUid target)
    {
        // A fresh steerer for Hold, without the creep's settings.
        _steering.Stop(ent.Owner);
        SetOrders(ent, WFPilotOrder.Hold);
        ent.Comp.OrdersCompleted = true;
        var ev = new WFPilotDockedEvent(ent, grid, target);
        RaiseLocalEvent(ent, ref ev, true);
    }

    private static void ResetDock(WFPilotDutyComponent duty)
    {
        duty.DockPhase = WFDockPhase.None;
        duty.DockAttempts = 0;
        duty.DockPhaseTime = 0f;
        duty.DockPlan = null;
        duty.DockCollision = null;
    }

    /// <summary>A dock that still exists, is anchored and is docked with nothing.</summary>
    private bool TryFreeDock(EntityUid uid, [NotNullWhen(true)] out DockingComponent? dock)
    {
        dock = null;
        return !TerminatingOrDeleted(uid)
               && Transform(uid).Anchored
               && TryComp(uid, out dock)
               && dock.DockedWith == null;
    }

    /// <summary>Whether the ship has stopped moving relative to the target and stopped turning.</summary>
    private bool Settled(EntityUid grid, EntityUid target)
    {
        if (!TryComp<PhysicsComponent>(grid, out var body))
            return true;

        var targetVelocity = TryComp<PhysicsComponent>(target, out var targetBody)
            ? targetBody.LinearVelocity
            : Vector2.Zero;

        return (body.LinearVelocity - targetVelocity).Length() < SettleSpeed
               && MathF.Abs(body.AngularVelocity) < SettleTurnRate;
    }

    /// <summary>
    /// The steerer's heading for our grid at the final pose. The steerer measures heading as world rotation plus a
    /// half turn.
    /// </summary>
    private Angle DockHeading(WFDockPlan plan, EntityUid target)
    {
        return _transform.GetWorldRotation(target) + plan.FinalAngle + new Angle(Math.PI);
    }

    /// <summary>
    /// The creep's <see cref="ShipSteererComponent.TargetRotation"/>, in degrees. The steerer only turns to
    /// <c>InRangeRotation</c> once in range; on the way in it faces the target plus this offset, so the offset that
    /// keeps the final heading is worked out every update. It is added in range too, so there it is zero.
    /// </summary>
    private float CreepHeadingOffset(ShipSteererComponent steerer, Angle heading, EntityUid grid)
    {
        var toTarget = _transform.ToMapCoordinates(steerer.Coordinates).Position - _transform.GetWorldPosition(grid);
        if (toTarget.Length() <= steerer.Range)
            return 0f;

        return (float) ShipSteeringSystem.ShortestAngleDistance(toTarget.ToWorldAngle(), heading).Degrees;
    }

    private EntityCoordinates StandoffOnMap(WFDockPlan plan, EntityUid map)
    {
        return new EntityCoordinates(map, _transform.ToMapCoordinates(plan.Standoff).Position);
    }

    private float PortDistance(WFDockPlan plan)
    {
        return (_transform.GetWorldPosition(plan.OwnDock) - _transform.GetWorldPosition(plan.TargetDock)).Length();
    }

    /// <summary>Undock: lets go of every docked port and backs off away from them. Nothing docked: hold.</summary>
    private void ReleaseDocks(Entity<WFPilotDutyComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || xform.MapUid is not { } map)
            return;

        var away = Vector2.Zero;
        foreach (var dock in _docking.GetDocks(grid))
        {
            if (!_docking.CanUndock((dock.Owner, dock.Comp)))
                continue;

            // Our dock faces what we leave; back off the other way.
            away -= _transform.GetWorldRotation(dock).RotateVec(new Vector2(0f, -1f));
            _docking.Undock(dock);
        }

        // Nothing was docked, or ports on opposite sides cancel out.
        if (away.LengthSquared() < 0.01f)
        {
            CompleteOrders(ent);
            return;
        }

        var point = _transform.GetWorldPosition(grid) + away.Normalized() * ent.Comp.DockStandoff;
        ent.Comp.Waypoints = new List<EntityCoordinates> { new(map, point) };
        ent.Comp.WaypointIndex = 0;
        Steer(ent);
    }

    /// <summary>Remembers a grid hit during the creep; the next update decides whether it ends the attempt.</summary>
    private void OnShuttleCollide(Entity<WFPilotDutyComponent> ent,
        ref PilotedShuttleRelayedEvent<StartCollideEvent> args)
    {
        var other = args.Args.OtherEntity;
        if (ent.Comp.Orders != WFPilotOrder.Dock
            || ent.Comp.DockPhase != WFDockPhase.Creep
            || !HasComp<MapGridComponent>(other))
        {
            return;
        }

        // A hit on anything but the target outweighs touching the target.
        if (ent.Comp.DockCollision == null || ent.Comp.DockCollision == ent.Comp.DockTarget)
            ent.Comp.DockCollision = other;
    }
}
