using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.HTN;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Puts a crew pilot at the helm and flies its orders with Mono's ship steering. The HTN only walks the pilot to the
/// helm and holds it; orders advance here, so a ship keeps flying while its pilot's HTN sleeps. Docking by hand is in
/// <c>WFPilotDutySystem.Docking.cs</c>.
/// </summary>
public sealed partial class WFPilotDutySystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipSteeringSystem _steering = default!;
    [Dependency] private ShuttleConsoleSystem _console = default!;

    /// <summary>Blackboard key holding the helm the pilot works.</summary>
    public const string HelmKey = "WFCrewHelm";

    /// <summary>Blackboard key holding the helm's coordinates, walked to before taking it.</summary>
    public const string HelmCoordinatesKey = "WFCrewHelmCoords";

    /// <summary>How far a holding ship may drift from where it stopped.</summary>
    private const float HoldRange = 5f;

    /// <summary>Speed under which a holding ship counts as stopped, in m/s.</summary>
    private const float HoldSpeed = 0.5f;

    private readonly List<EntityUid> _toRelease = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFPilotDutyComponent, PilotedShuttleRelayedEvent<StartCollideEvent>>(OnShuttleCollide);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _toRelease.Clear();
        var query = EntityQueryEnumerator<WFPilotDutyComponent>();
        while (query.MoveNext(out var uid, out var duty))
        {
            if (!duty.AtHelm)
                continue;

            if (!CanHoldHelm((uid, duty)) || !HelmInPlan(uid))
            {
                _toRelease.Add(uid);
                continue;
            }

            if (!TryComp<ShipSteererComponent>(uid, out var steerer))
            {
                // Something else stopped the steering; pick it back up.
                if (Steer((uid, duty)) == null)
                    _toRelease.Add(uid);

                continue;
            }

            Advance((uid, duty), steerer, frameTime);
        }

        foreach (var uid in _toRelease)
        {
            ReleaseHelm(uid);
        }
    }

    /// <summary>Stop the ship and keep station.</summary>
    public void Hold(Entity<WFPilotDutyComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        SetOrders((ent, ent.Comp), WFPilotOrder.Hold);
    }

    /// <summary>Fly the waypoints in turn, then hold. No waypoints is Hold.</summary>
    public void GoTo(Entity<WFPilotDutyComponent?> ent, List<EntityCoordinates> waypoints)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        if (waypoints.Count == 0)
        {
            Hold(ent);
            return;
        }

        ent.Comp.Waypoints = new List<EntityCoordinates>(waypoints);
        ent.Comp.WaypointIndex = 0;
        SetOrders((ent, ent.Comp), WFPilotOrder.GoTo);
    }

    /// <summary>Circle a point at a radius.</summary>
    public void Loiter(Entity<WFPilotDutyComponent?> ent, EntityCoordinates center, float radius)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.LoiterCenter = center;
        ent.Comp.LoiterRadius = radius;
        SetOrders((ent, ent.Comp), WFPilotOrder.Loiter);
    }

    /// <summary>Keep within range of another grid.</summary>
    public void Follow(Entity<WFPilotDutyComponent?> ent, EntityUid grid, float range)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.FollowTarget = grid;
        ent.Comp.FollowRange = range;
        SetOrders((ent, ent.Comp), WFPilotOrder.Follow);
    }

    /// <summary>Fly to another grid, dock with it by hand, then hold.</summary>
    public void Dock(Entity<WFPilotDutyComponent?> ent, EntityUid targetGrid)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.DockTarget = targetGrid;
        SetOrders((ent, ent.Comp), WFPilotOrder.Dock);
    }

    /// <summary>Undock from everything, back off away from it, then hold. Done once the pilot is at the helm.</summary>
    public void Undock(Entity<WFPilotDutyComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        // The back-off point is filled in when the docks are released.
        ent.Comp.Waypoints = new List<EntityCoordinates>();
        ent.Comp.WaypointIndex = 0;
        SetOrders((ent, ent.Comp), WFPilotOrder.Undock);
    }

    private void SetOrders(Entity<WFPilotDutyComponent> ent, WFPilotOrder orders)
    {
        ent.Comp.Orders = orders;
        ent.Comp.OrdersCompleted = false;
        ResetDock(ent.Comp);

        var ev = new WFPilotOrdersChangedEvent(ent, orders);
        RaiseLocalEvent(ent, ref ev, true);

        if (ent.Comp.AtHelm)
            Steer(ent);
    }

    /// <summary>
    /// The assigned helm if it can be flown from, else the nearest anchored, powered shuttle console on the
    /// crewman's grid. A helm the crewman already holds wins.
    /// </summary>
    public bool TryFindHelm(EntityUid mob, out EntityUid console)
    {
        console = default;
        if (Transform(mob).GridUid is not { } grid)
            return false;

        if (TryComp<PilotComponent>(mob, out var pilot) && pilot.Console is { } held && IsUsableHelm(held, grid))
        {
            console = held;
            return true;
        }

        if (TryComp<WFPilotDutyComponent>(mob, out var duty)
            && duty.Console is { } assigned
            && IsUsableHelm(assigned, grid))
        {
            console = assigned;
            return true;
        }

        var position = _transform.GetWorldPosition(mob);
        var best = float.MaxValue;
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid || !IsUsableHelm(uid, grid))
                continue;

            var distance = (_transform.GetWorldPosition(xform) - position).LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            console = uid;
        }

        return best < float.MaxValue;
    }

    /// <summary>
    /// Attaches the crewman to the helm and steers toward its current orders. True when it holds the helm
    /// afterwards, including when it already did.
    /// </summary>
    public bool TryTakeHelm(EntityUid mob, EntityUid console)
    {
        if (!TryComp<WFPilotDutyComponent>(mob, out var duty)
            || Transform(mob).GridUid is not { } grid
            || !HasComp<ShuttleComponent>(grid)
            || !IsUsableHelm(console, grid)
            || !TryComp<ShuttleConsoleComponent>(console, out var consoleComp))
        {
            return false;
        }

        // Upstream's AddPilot doesn't let go of another console first.
        if (TryComp<PilotComponent>(mob, out var existing) && existing.Console is { } current && current != console)
            _console.RemovePilot(mob, existing);

        var pilot = EnsureComp<PilotComponent>(mob);
        _console.AddPilot(console, mob, consoleComp);
        if (pilot.Console != console)
        {
            // A pilot component with no console still blocks movement.
            RemComp<PilotComponent>(mob);
            return false;
        }

        if (Steer((mob, duty)) == null)
        {
            _console.RemovePilot(mob);
            return false;
        }

        if (duty.AtHelm)
            return true;

        duty.AtHelm = true;
        var facing = _transform.GetWorldPosition(console) - _transform.GetWorldPosition(mob);
        if (facing.LengthSquared() > 0.001f)
            _transform.SetWorldRotation(mob, facing.ToWorldAngle());
        var ev = new WFHelmTakenEvent(mob, console, grid);
        RaiseLocalEvent(mob, ref ev, true);
        return true;
    }

    /// <summary>Stops steering and detaches the crewman from the helm. Safe to call at any time.</summary>
    public void ReleaseHelm(EntityUid mob)
    {
        _steering.Stop(mob);
        _console.RemovePilot(mob);

        if (!TryComp<WFPilotDutyComponent>(mob, out var duty) || !duty.AtHelm)
            return;

        duty.AtHelm = false;
        var ev = new WFHelmReleasedEvent(mob);
        RaiseLocalEvent(mob, ref ev, true);
    }

    public bool IsAtHelm(EntityUid mob)
    {
        return TryComp<WFPilotDutyComponent>(mob, out var duty) && duty.AtHelm;
    }

    /// <summary>
    /// Whether the crewman holds a helm it can keep flying from: attached to a usable console, up, and not taken over
    /// by a player.
    /// </summary>
    public bool CanHoldHelm(Entity<WFPilotDutyComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !ent.Comp.AtHelm)
            return false;

        return TryComp<PilotComponent>(ent, out var pilot)
               && pilot.Console is { } console
               && Transform(ent).GridUid is { } grid
               && IsUsableHelm(console, grid)
               && !_mobState.IsIncapacitated(ent)
               && !HasComp<ActorComponent>(ent);
    }

    /// <summary>
    /// False when the crewman's HTN is running a plan that doesn't hold the helm, such as a fight it woke into. No
    /// plan (asleep, or between plans) keeps the helm.
    /// </summary>
    private bool HelmInPlan(EntityUid mob)
    {
        return !TryComp<HTNComponent>(mob, out var htn)
               || htn.Plan is not { } plan
               || plan.Tasks.Any(task => task.Operator is WFTakeHelmOperator);
    }

    private bool IsUsableHelm(EntityUid console, EntityUid grid)
    {
        return !TerminatingOrDeleted(console)
               && HasComp<ShuttleConsoleComponent>(console)
               && Transform(console) is { Anchored: true } xform
               && xform.GridUid == grid
               && _power.IsPowered(console);
    }

    /// <summary>
    /// GoTo: on to the next waypoint once in range, and hold after the last. Follow: hold once the grid is gone.
    /// Undock: release the docks, then back off like a GoTo. Dock: fly the docking phases.
    /// </summary>
    private void Advance(Entity<WFPilotDutyComponent> ent, ShipSteererComponent steerer, float frameTime)
    {
        var duty = ent.Comp;
        switch (duty.Orders)
        {
            case WFPilotOrder.Dock:
                AdvanceDock(ent, steerer, frameTime);
                break;
            case WFPilotOrder.Undock when duty.Waypoints.Count == 0:
                ReleaseDocks(ent);
                break;
            case WFPilotOrder.GoTo or WFPilotOrder.Undock when steerer.Status == ShipSteeringStatus.InRange:
                duty.WaypointIndex++;
                if (duty.WaypointIndex < duty.Waypoints.Count)
                {
                    Steer(ent);
                    return;
                }

                CompleteOrders(ent);
                break;
            case WFPilotOrder.Follow when duty.FollowTarget is not { } target || TerminatingOrDeleted(target):
                SetOrders(ent, WFPilotOrder.Hold);
                break;
        }
    }

    /// <summary>Switches to Hold and reports the orders flown to their end.</summary>
    private void CompleteOrders(Entity<WFPilotDutyComponent> ent)
    {
        SetOrders(ent, WFPilotOrder.Hold);
        ent.Comp.OrdersCompleted = true;
        var ev = new WFPilotOrdersCompletedEvent(ent);
        RaiseLocalEvent(ent, ref ev, true);
    }

    /// <summary>Points the steering at the current orders. Null when the crewman's grid can't be steered.</summary>
    private ShipSteererComponent? Steer(Entity<WFPilotDutyComponent> ent)
    {
        var duty = ent.Comp;
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || xform.MapUid is not { } map)
            return null;

        // Hold where the ship is now; also the fallback for orders missing their target.
        var target = new EntityCoordinates(map, _transform.GetWorldPosition(grid));
        var mode = ShipSteeringMode.GoToRange;
        var range = HoldRange;
        var speed = HoldSpeed;
        var avoid = true;
        var finishOnCollide = true;
        var faceTarget = false;
        Angle? heading = null;
        float? maxTurnRate = null;

        switch (duty.Orders)
        {
            case WFPilotOrder.GoTo when duty.WaypointIndex >= 0 && duty.WaypointIndex < duty.Waypoints.Count:
                target = duty.Waypoints[duty.WaypointIndex];
                range = duty.ArrivalRange;
                speed = duty.CruiseSpeed;
                faceTarget = true;
                break;
            case WFPilotOrder.Loiter when duty.LoiterCenter is { } center:
                target = center;
                mode = ShipSteeringMode.Orbit;
                range = duty.LoiterRadius;
                speed = duty.CruiseSpeed;
                break;
            case WFPilotOrder.Follow when duty.FollowTarget is { } followed && !TerminatingOrDeleted(followed):
                // Grid-relative coordinates move with the grid.
                target = new EntityCoordinates(followed, Vector2.Zero);
                range = duty.FollowRange;
                speed = duty.CruiseSpeed;
                faceTarget = true;
                break;
            case WFPilotOrder.Undock when duty.Waypoints.Count > 0:
                target = duty.Waypoints[0];
                speed = duty.DockApproachSpeed;
                break;
            case WFPilotOrder.Dock when duty.DockPlan is { } plan
                                        && duty.DockTarget is { } dockTarget
                                        && !TerminatingOrDeleted(dockTarget):
                heading = DockHeading(plan, dockTarget);
                if (duty.DockPhase == WFDockPhase.Creep)
                {
                    // On the target grid, so the final pose moves with it; avoidance would refuse to touch it.
                    target = plan.Final;
                    range = CreepRange;
                    speed = duty.DockCreepSpeed;
                    avoid = false;
                    finishOnCollide = false;
                    faceTarget = true;
                    break;
                }

                target = StandoffOnMap(plan, map);
                range = StandoffRange;
                speed = duty.DockPhase == WFDockPhase.Settle ? SettleSpeed : duty.DockApproachSpeed;
                maxTurnRate = duty.DockPhase == WFDockPhase.Settle ? SettleTurnRate : null;
                break;
        }

        if (_steering.Steer(ent.Owner, target) is not { } steerer)
            return null;

        steerer.Mode = mode;
        steerer.Range = range;
        steerer.InRangeMaxSpeed = speed;
        steerer.AvoidCollisions = avoid;
        steerer.AvoidProjectiles = duty.Orders != WFPilotOrder.Dock;
        steerer.FinishOnCollide = finishOnCollide;
        steerer.InRangeRotation = heading;
        steerer.MaxRotateRate = maxTurnRate;
        steerer.AlwaysFaceTarget = faceTarget;
        steerer.TargetRotation = faceTarget && heading is { } held ? CreepHeadingOffset(steerer, held, grid) : 0f;
        // Status is only refreshed when the ship next asks for input; don't let the last target's arrival count.
        steerer.Status = ShipSteeringStatus.Moving;
        return steerer;
    }
}
