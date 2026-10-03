using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Captains suspend and restore their own crew's orders without overwriting later commands.</summary>
public sealed partial class WFCaptainSystem : EntitySystem
{
    [Dependency] private WFPilotDutySystem _pilots = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, SavedCourse> _courses = new();
    private readonly HashSet<(EntityUid Grid, string Group, EntityUid Pilot)> _overridden = new();
    private bool _changingOrders;

    /// <summary>Automatic evasion and restoration preserve the crew's escort assignment.</summary>
    public bool IsChangingOrders => _changingOrders;

    /// <summary>Whether a captain temporarily owns this pilot's flight orders.</summary>
    public bool IsCourseSuspended(EntityUid pilot) => _courses.ContainsKey(pilot);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewAlertEvent>(OnAlert);
        SubscribeLocalEvent<WFCrewAlertClearedEvent>(OnClear);
        SubscribeLocalEvent<WFPilotOrdersChangedEvent>(OnOrdersChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        foreach (var (pilot, course) in _courses.ToArray())
        {
            if (!Eligible(pilot, course.Grid, course.Group) || !Eligible(course.Captain, course.Grid, course.Group))
                _courses.Remove(pilot);
        }
    }

    private bool Eligible(EntityUid uid, EntityUid grid, string group)
    {
        return !TerminatingOrDeleted(uid) && _mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid)
            && TryComp<WFCrewComponent>(uid, out var crew) && crew.Group == group && Transform(uid).GridUid == grid;
    }

    private void OnOrdersChanged(ref WFPilotOrdersChangedEvent args)
    {
        if (!_changingOrders && !args.Continuation && _courses.Remove(args.Mob, out var course))
            _overridden.Add((course.Grid, course.Group, args.Mob));
    }

    private void OnAlert(ref WFCrewAlertEvent args)
    {
        var captains = EntityQueryEnumerator<WFCaptainComponent>();
        while (captains.MoveNext(out var captain, out var component))
        {
            if (!component.HeaveTo || !Eligible(captain, args.Grid, args.Group))
                continue;
            var pilots = EntityQueryEnumerator<WFPilotDutyComponent, WFCrewComponent>();
            while (pilots.MoveNext(out var pilot, out var duty, out var crew))
            {
                if (crew.Duty != WFCrewDuties.Pilot || !Eligible(pilot, args.Grid, args.Group)
                    || _courses.ContainsKey(pilot)
                    || _overridden.Contains((args.Grid, args.Group, pilot)))
                    continue;
                // Preserve the destination beyond automatic undocking, not its temporary back-off course.
                var order = duty.ResumeOrder ?? duty.Orders;
                var waypoints = duty.ResumeOrder != null
                    ? duty.ResumeWaypoints.ToList()
                    : duty.Waypoints.Skip(duty.WaypointIndex).ToList();
                _courses[pilot] = new SavedCourse(captain, args.Grid, args.Group, order,
                    waypoints, duty.LoiterCenter, duty.RequestedLoiterRadius, duty.LoiterSpeedOverride, duty.OrbitKind,
                    duty.FollowTarget, duty.FollowRange, duty.DockTarget, duty.EscortOffset, duty.EscortSlot, duty.EscortSpacing,
                    duty.HoldPosition, duty.HoldHeading);
                _changingOrders = true;
                try
                {
                    var threats = _alerts.GetHostileShips(args.Grid, args.Group);
                    if (threats.FirstOrDefault() is var threat && threat.IsValid())
                    {
                        var center = TryComp<Robust.Shared.Map.Components.MapGridComponent>(threat, out var targetGrid)
                            ? targetGrid.LocalAABB.Center : System.Numerics.Vector2.Zero;
                        _pilots.Loiter(pilot, new EntityCoordinates(threat, center), 0f,
                            objective: WFCrewObjectiveKind.Attack);
                    }
                    else
                        _pilots.Hold(pilot);
                }
                finally { _changingOrders = false; }
            }
        }
    }

    private void OnClear(ref WFCrewAlertClearedEvent args)
    {
        if (_alerts.IsAlerted(args.Grid, args.Group))
            return;
        var grid = args.Grid;
        var group = args.Group;
        _overridden.RemoveWhere(entry => entry.Grid == grid && entry.Group == group);
        foreach (var (pilot, course) in _courses.ToArray())
        {
            if (course.Grid != args.Grid || course.Group != args.Group)
                continue;
            _courses.Remove(pilot);
            if (!Eligible(pilot, course.Grid, course.Group) || !Eligible(course.Captain, course.Grid, course.Group))
                continue;
            _changingOrders = true;
            try
            {
                RestoreCourse(pilot, course);
            }
            finally { _changingOrders = false; }
        }
    }

    private void RestoreCourse(EntityUid pilot, SavedCourse course)
    {
        switch (course.Order)
        {
            case WFPilotOrder.Hold:
                _pilots.Hold(pilot);
                if (course.HoldPosition is { } anchor && anchor.IsValid(EntityManager)
                    && _transform.ToMapCoordinates(anchor).MapId == Transform(pilot).MapID
                    && TryComp<WFPilotDutyComponent>(pilot, out var holding))
                {
                    holding.HoldPosition = anchor;
                    holding.HoldHeading = course.HoldHeading;
                    _pilots.SetNavigation(pilot, holding.Navigation);
                }
                break;
            case WFPilotOrder.GoTo:
                _pilots.GoTo(pilot, course.Waypoints.Where(point => point.IsValid(EntityManager)).ToList());
                break;
            case WFPilotOrder.Loiter when course.Center is { } center && center.IsValid(EntityManager):
                _pilots.Loiter(pilot, center, course.Radius, course.OrbitSpeed, course.OrbitKind);
                break;
            case WFPilotOrder.Follow when course.Follow is { } follow && !TerminatingOrDeleted(follow):
                _pilots.Follow(pilot, follow, course.Range);
                if (TryComp<WFPilotDutyComponent>(pilot, out var formation))
                {
                    formation.EscortOffset = course.EscortOffset;
                    formation.EscortSlot = course.EscortSlot;
                    formation.EscortSpacing = course.EscortSpacing;
                    _pilots.SetNavigation(pilot, formation.Navigation);
                }
                break;
            case WFPilotOrder.Dock when course.Dock is { } dock && !TerminatingOrDeleted(dock):
                _pilots.Dock(pilot, dock);
                break;
            case WFPilotOrder.Undock:
                _pilots.Undock(pilot);
                break;
        }
    }

    private sealed record SavedCourse(EntityUid Captain, EntityUid Grid, string Group, WFPilotOrder Order,
        List<EntityCoordinates> Waypoints, EntityCoordinates? Center, float Radius, float? OrbitSpeed, WFCrewObjectiveKind OrbitKind, EntityUid? Follow,
        float Range, EntityUid? Dock, System.Numerics.Vector2? EscortOffset, int EscortSlot, float EscortSpacing,
        EntityCoordinates? HoldPosition, Angle HoldHeading);
}
