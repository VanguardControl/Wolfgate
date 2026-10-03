using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

public sealed partial class WFPilotDutySystem
{
    [Dependency] private MoverController _navigationMover = default!;

    /// <summary>Applies independent scenario limits without restarting an objective or losing its hold anchor.</summary>
    public bool SetNavigation(Entity<WFPilotDutyComponent?> ent, WFCrewNavigationSettings settings)
    {
        if (!settings.IsValid() || !Resolve(ent, ref ent.Comp))
            return false;
        var replanDock = ent.Comp.Navigation.DockStandoff != settings.DockStandoff
            && ent.Comp.DockPhase is WFDockPhase.Approach or WFDockPhase.Settle;
        ent.Comp.Navigation = settings.Clone();
        if (TryComp<WFCrewComponent>(ent, out var crew))
            crew.Navigation = settings.Clone();
        UpdateOrbitLimits(ent.Comp);
        if (replanDock)
        {
            ent.Comp.DockPlan = null;
            ent.Comp.DockPhase = WFDockPhase.None;
            ent.Comp.DockPhaseTime = 0f;
        }
        if (ent.Comp.EscortOffset != null && ent.Comp.FollowTarget is { } target && !TerminatingOrDeleted(target)
            && Transform(ent).GridUid is { } grid)
        {
            ent.Comp.FollowRange = settings.EscortRange;
            UpdateEscortOffset(ent.Comp, grid, target);
        }
        if (ent.Comp.AtHelm)
            Steer((ent, ent.Comp));
        return true;
    }

    private static void UpdateOrbitLimits(WFPilotDutyComponent duty)
    {
        duty.LoiterRadius = duty.OrbitKind == WFCrewObjectiveKind.Attack
            ? MathF.Max(duty.RequestedLoiterRadius, duty.Navigation.AttackRange) : duty.RequestedLoiterRadius;
        duty.LoiterSpeed = duty.LoiterSpeedOverride ?? (duty.OrbitKind switch
        {
            WFCrewObjectiveKind.Circle => duty.Navigation.CircleSpeed,
            WFCrewObjectiveKind.Attack => duty.Navigation.AttackSpeed,
            _ => duty.Navigation.LoiterSpeed,
        });
    }

    private void UpdateEscortOffset(WFPilotDutyComponent duty, EntityUid grid, EntityUid target)
    {
        var spacing = MathF.Max(duty.EscortSpacing, GridRadius(grid) + GridRadius(target) + duty.Navigation.NavigationClearance);
        var row = duty.EscortSlot / 2 + 1;
        var center = TryComp<MapGridComponent>(target, out var targetGrid) ? targetGrid.LocalAABB.Center : Vector2.Zero;
        duty.EscortOffset = center + GridForwardAngle(target).RotateVec(new Vector2((duty.EscortSlot % 2 == 0 ? -1 : 1) * spacing, -row * spacing));
    }

    private void CaptureHold(Entity<WFPilotDutyComponent> ent)
    {
        if (Transform(ent).GridUid is not { } grid || Transform(grid).MapUid is not { } map)
            return;
        ent.Comp.HoldPosition = new EntityCoordinates(map, _transform.GetWorldPosition(grid));
        ent.Comp.HoldHeading = _transform.GetWorldRotation(grid) + new Angle(Math.PI);
        ent.Comp.CorrectingHold = false;
    }

    private float SafeOrbitRange(EntityUid grid, EntityCoordinates center, float requested, float clearance)
    {
        var radius = GridRadius(grid) + clearance;
        if (TryComp<MapGridComponent>(center.EntityId, out var target))
            radius += target.LocalAABB.Size.Length() / 2f + (center.Position - target.LocalAABB.Center).Length();
        return MathF.Max(requested, radius);
    }

    /// <summary>Limits real flight speed and turning while keeping full braking authority.</summary>
    private void OnCrewGetInputs(Entity<WFPilotDutyComponent> ent, ref GetShuttleInputsEvent args)
    {
        if (!ent.Comp.AtHelm || HasComp<ActorComponent>(ent) || !args.GotInput
            || !TryComp<ShipSteererComponent>(ent, out var steerer)
            || !TryComp<ShuttleComponent>(args.ShuttleUid, out var shuttle)
            || !TryComp<PhysicsComponent>(args.ShuttleUid, out var body))
            return;

        var duty = ent.Comp;
        var limits = duty.Navigation;
        var grid = args.ShuttleUid;
        var position = _transform.GetWorldPosition(grid);
        var input = args.Input ?? new ShuttleInput(Vector2.Zero, 0f, 0f);
        if (duty.Orders == WFPilotOrder.Hold && duty.HoldPosition is { } anchor && anchor.IsValid(EntityManager))
        {
            var distance = (_transform.ToMapCoordinates(anchor).Position - position).Length();
            if (_docking.GetDocks(grid).Any(dock => dock.Comp.Docked))
            {
                args.Input = new ShuttleInput(Vector2.Zero, 0f, 0f);
                steerer.Status = ShipSteeringStatus.InRange;
                return;
            }
            if (distance > limits.HoldRange)
                duty.CorrectingHold = true;
            else if (distance <= limits.HoldReturnRange)
                duty.CorrectingHold = false;
            steerer.Range = duty.CorrectingHold ? limits.HoldReturnRange : limits.HoldRange;
            steerer.TargetRotation = CreepHeadingOffset(steerer, duty.HoldHeading, grid);
            if (!duty.CorrectingHold)
            {
                // Brake tiny residual movement without steering toward a changing zero-length direction.
                args.Input = new ShuttleInput(Vector2.Zero, 0f, 1f);
                steerer.Status = body.LinearVelocity.Length() <= limits.ArrivalSpeed && MathF.Abs(body.AngularVelocity) <= limits.HoldAngularSpeed
                    ? ShipSteeringStatus.InRange : ShipSteeringStatus.Moving;
                return;
            }
        }

        var speed = duty.Orders switch
        {
            WFPilotOrder.Hold => limits.HoldCorrectionSpeed,
            WFPilotOrder.Loiter => duty.LoiterSpeed,
            WFPilotOrder.Dock => duty.DockPhase == WFDockPhase.Creep ? duty.DockCreepSpeed : duty.DockApproachSpeed,
            WFPilotOrder.Undock => limits.UndockSpeed,
            WFPilotOrder.Follow => limits.FollowSpeed,
            _ => duty.CruiseSpeed,
        };
        var target = duty.Orders switch
        {
            WFPilotOrder.Follow => duty.FollowTarget,
            WFPilotOrder.Loiter => duty.LoiterCenter?.EntityId,
            _ => null,
        };
        var targetVelocity = Vector2.Zero;
        if (target is { } leader && !TerminatingOrDeleted(leader) && TryComp<MapGridComponent>(leader, out var targetGrid))
        {
            if (TryComp<PhysicsComponent>(leader, out var targetBody))
                targetVelocity = targetBody.LinearVelocity;
            var center = Vector2.Transform(targetGrid.LocalAABB.Center, _transform.GetWorldMatrix(leader));
            var toTarget = center - position;
            var gap = toTarget.Length() - GridRadius(grid) - targetGrid.LocalAABB.Size.Length() / 2f;
            var relativeVelocity = body.LinearVelocity - targetVelocity;
            var closing = Vector2.Dot(relativeVelocity, ShipSteeringSystem.NormalizedOrZero(toTarget));
            var brakeAcceleration = _navigationMover.GetWorldDirectionAccel(-toTarget, shuttle, body, Transform(grid)).Length();
            if (shuttle.AccelerationMultiplier > 0f)
                brakeAcceleration /= shuttle.AccelerationMultiplier;
            brakeAcceleration *= ShuttleComponent.BrakeCoefficient;
            var stoppingDistance = brakeAcceleration > 0f ? closing * closing / (2f * brakeAcceleration) : float.PositiveInfinity;
            if (gap < limits.NavigationClearance + stoppingDistance + MathF.Max(closing, 0f) * limits.BrakingLookahead && closing > limits.SpeedTolerance)
                input = new ShuttleInput(Vector2.Zero, 0f, 1f);
            if (gap < limits.NavigationClearance * 2f)
                speed = MathF.Min(speed, limits.NearTargetSpeed);
        }

        // Native arrival thresholds do not limit transit speed. Feed the mover its actual cruise ceiling.
        var maximumSpeed = speed + targetVelocity.Length();
        if (body.LinearVelocity.Length() > maximumSpeed + limits.SpeedTolerance)
            input = new ShuttleInput(Vector2.Zero, input.Rotation, 1f);
        args.SetMaxVelocity = maximumSpeed;
        args.AccelMul *= input.Brakes > 0f ? 1f : limits.ThrustMultiplier;
        args.AngularMul *= input.Brakes > 0f ? 1f : limits.AngularThrustMultiplier;

        var acceleration = _navigationMover.GetAngularAcceleration(shuttle, body);
        if (shuttle.AngularMultiplier > 0f)
            acceleration = acceleration / shuttle.AngularMultiplier * args.AngularMul;
        if (acceleration > 0f && args.FrameTime > 0f)
        {
            var delta = acceleration * args.FrameTime;
            var desiredRate = Math.Clamp(body.AngularVelocity - input.Rotation * delta, -limits.MaximumTurnRate, limits.MaximumTurnRate);
            input.Rotation = Math.Clamp((body.AngularVelocity - desiredRate) / delta, -1f, 1f);
        }
        args.Input = input;
    }
}
