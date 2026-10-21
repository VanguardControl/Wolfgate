using System.Numerics;
using Content.Server._WF.Vehicles.Npc;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.NpcVehicleCrew;

/// <summary>The commander: Hold, Patrol, Hunt, Escort, Flee and Retreat each set where the driver goes.</summary>
public sealed partial class WFVehicleCrewSystem
{
    /// <summary>How far a holding vehicle may be pushed from home before it drives back.</summary>
    private const float HomeSlack = 3f;

    private const int PatrolPointsMin = 4;
    private const int PatrolPointsMax = 6;

    /// <summary>Patrol points are at least this share of the patrol radius from home.</summary>
    private const float PatrolInnerShare = 0.3f;

    private const int PatrolPointTries = 20;

    private static readonly TimeSpan PatrolPauseMin = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PatrolPauseMax = TimeSpan.FromSeconds(20);

    /// <summary>How long a patrol that stops to fight waits after losing sight of the target.</summary>
    private static readonly TimeSpan ContactLoss = TimeSpan.FromSeconds(30);

    /// <summary>Out of sight this long, a hunter drives to where the target was last seen.</summary>
    private static readonly TimeSpan HuntChaseDelay = TimeSpan.FromSeconds(10);

    /// <summary>Out of sight this long, a hunter gives up and goes back to its orders.</summary>
    private static readonly TimeSpan HuntGiveUp = TimeSpan.FromSeconds(60);

    /// <summary>A hunter closes in beyond this multiple of its standoff and backs off inside the second.</summary>
    private const float AdvanceFactor = 1.3f;

    private const float BackOffFactor = 0.6f;

    /// <summary>How far one back-off goal lies behind the vehicle; inside the driver's reverse range.</summary>
    private const float BackOffStep = 2.5f;

    /// <summary>An escort keeps this far behind its lead, with a tile of slack either way.</summary>
    private const float EscortDistance = 5f;

    private const float EscortSlack = 1f;

    /// <summary>How far a fleeing vehicle runs when home lies toward the attacker.</summary>
    private const float FleeDistance = 40f;

    /// <summary>Unhit this long, a fleeing vehicle stops running.</summary>
    private static readonly TimeSpan FleeCalm = TimeSpan.FromSeconds(30);

    private readonly HashSet<EntityUid> _strangers = new();
    private readonly List<EntityUid> _gone = new();

    /// <summary>Retreats a damaged or dry vehicle home, and sends it back to its orders once repaired and rearmed.</summary>
    private void UpdateDamageState(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto)
    {
        var fraction = IntegrityFraction(uid);
        if (crew.Behaviour == WFVehicleBehaviour.Retreat)
        {
            if (fraction >= proto.RetreatIntegrity + RetreatRecoverMargin && !crew.OutOfAmmo)
                ChangeBehaviour(crew, crew.PreviousBehaviour);
            return;
        }

        if (crew.Behaviour is WFVehicleBehaviour.Flee or WFVehicleBehaviour.Bail)
            return;

        if (fraction < proto.RetreatIntegrity || crew.Gunner != null && crew.OutOfAmmo)
            ChangeBehaviour(crew, WFVehicleBehaviour.Retreat);
    }

    /// <summary>
    /// A holding vehicle with a guard radius sounds its horn at each stranger who comes inside it, and takes him for an
    /// attacker once he has stayed the warning time.
    /// </summary>
    private void UpdateGuard(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, TimeSpan now)
    {
        if (crew.Behaviour != WFVehicleBehaviour.Hold || proto.GuardRadius <= 0f)
        {
            crew.Warned.Clear();
            return;
        }

        var here = _transform.GetMapCoordinates(uid);
        _strangers.Clear();
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(here, proto.GuardRadius))
        {
            if (IsStranger(uid, crew, mob.Owner))
                _strangers.Add(mob.Owner);
        }

        foreach (var vehicle in _lookup.GetEntitiesInRange<WFOccupiedVehicleComponent>(here, proto.GuardRadius))
        {
            if (IsStranger(uid, crew, vehicle.Owner))
                _strangers.Add(vehicle.Owner);
        }

        _gone.Clear();
        foreach (var warned in crew.Warned.Keys)
        {
            if (!_strangers.Contains(warned))
                _gone.Add(warned);
        }

        foreach (var gone in _gone)
            crew.Warned.Remove(gone);

        var warnTime = TimeSpan.FromSeconds(proto.GuardWarnTime);
        foreach (var stranger in _strangers)
        {
            if (!crew.Warned.TryGetValue(stranger, out var warnedAt))
            {
                crew.Warned[stranger] = now;
                _horn.TrySoundHorn(uid);
                continue;
            }

            if (now - warnedAt >= warnTime)
                crew.Attackers[stranger] = now;
        }
    }

    /// <summary>Someone a guard warns off: up and about, not the crew, not of a friendly faction.</summary>
    private bool IsStranger(EntityUid uid, WFVehicleCrewComponent crew, EntityUid other)
    {
        if (other == uid || IsOwnCrew(crew, other) || TerminatingOrDeleted(other))
            return false;

        if (TryComp<MobStateComponent>(other, out var mobState) && _mobState.IsIncapacitated(other, mobState))
            return false;

        return !_factions.IsEntityFriendly(uid, other);
    }

    /// <summary>Sets the driver's goal from the crew's current orders; a target sends a pursuing crew hunting.</summary>
    private void UpdateBehaviour(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, TimeSpan now)
    {
        var here = _transform.GetMapCoordinates(uid);
        if (crew.Target != null && crew.TargetVisible && crew.Pursues &&
            crew.Behaviour is WFVehicleBehaviour.Hold or WFVehicleBehaviour.Patrol)
        {
            ChangeBehaviour(crew, WFVehicleBehaviour.Hunt);
        }

        switch (crew.Behaviour)
        {
            case WFVehicleBehaviour.Hold:
                GoHome(crew, here, HomeSlack);
                break;
            case WFVehicleBehaviour.Patrol:
                UpdatePatrol(uid, crew, proto, here, now);
                break;
            case WFVehicleBehaviour.Hunt:
                UpdateHunt(crew, proto, here, now);
                break;
            case WFVehicleBehaviour.Escort:
                UpdateEscort(crew, here);
                break;
            case WFVehicleBehaviour.Flee:
                UpdateFlee(crew, here, now);
                break;
            case WFVehicleBehaviour.Retreat:
                GoHome(crew, here, ArriveDistance);
                break;
            default:
                crew.Goal = null;
                break;
        }
    }

    /// <summary>Drives home once further than <paramref name="slack"/> from it, and keeps going until there.</summary>
    private static void GoHome(WFVehicleCrewComponent crew, MapCoordinates here, float slack)
    {
        var away = Distance(here, crew.Home);
        crew.Goal = away > slack || crew.Goal != null && away > ArriveDistance ? crew.Home : null;
    }

    /// <summary>Loops the patrol points, pausing at each; a crew that does not fire on the move stops while in contact.</summary>
    private void UpdatePatrol(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, MapCoordinates here, TimeSpan now)
    {
        EnsurePatrolPoints(uid, crew, proto);
        if (crew.Target != null && !proto.FireOnTheMove && now - crew.LastSeenAt < ContactLoss)
        {
            crew.Goal = null;
            return;
        }

        if (now < crew.PauseUntil || crew.Waypoints.Count == 0)
        {
            crew.Goal = null;
            return;
        }

        crew.WaypointIndex %= crew.Waypoints.Count;
        var point = crew.Waypoints[crew.WaypointIndex];
        if (Distance(here, point) > ArriveDistance)
        {
            crew.Goal = point;
            return;
        }

        crew.WaypointIndex = (crew.WaypointIndex + 1) % crew.Waypoints.Count;
        crew.PauseUntil = now + _random.Next(PatrolPauseMin, PatrolPauseMax);
        crew.Goal = null;
    }

    /// <summary>Rolls patrol points round home on floor of the vehicle's grid, when none were given.</summary>
    private void EnsurePatrolPoints(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto)
    {
        if (crew.Waypoints.Count > 0)
            return;

        var gridUid = Transform(uid).GridUid;
        TryComp<MapGridComponent>(gridUid, out var grid);
        var count = _random.Next(PatrolPointsMin, PatrolPointsMax + 1);
        for (var i = 0; i < count; i++)
        {
            for (var attempt = 0; attempt < PatrolPointTries; attempt++)
            {
                var angle = new Angle(_random.NextFloat(0f, MathF.Tau));
                var distance = _random.NextFloat(PatrolInnerShare, 1f) * proto.PatrolRadius;
                var point = new MapCoordinates(crew.Home.Position + angle.ToVec() * distance, crew.Home.MapId);
                if (gridUid is { } gridEnt && grid != null &&
                    (!_map.TryGetTileRef(gridEnt, grid, _map.WorldToTile(gridEnt, grid, point.Position), out var tile) ||
                     tile.Tile.IsEmpty))
                {
                    continue;
                }

                crew.Waypoints.Add(point);
                break;
            }
        }

        if (crew.Waypoints.Count == 0)
            crew.Waypoints.Add(crew.Home);
    }

    /// <summary>
    /// Closes on the target to the standoff, backs off inside it and holds between; out of sight, drives to where it was
    /// last seen and in the end gives up.
    /// </summary>
    private void UpdateHunt(WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, MapCoordinates here, TimeSpan now)
    {
        if (crew.Target != null && crew.TargetVisible && crew.LastSeenTarget is { } seen)
        {
            var distance = Distance(here, seen);
            if (distance > proto.Standoff * AdvanceFactor)
            {
                crew.Goal = seen;
            }
            else if (distance < proto.Standoff * BackOffFactor)
            {
                var away = here.Position - seen.Position;
                away = away.LengthSquared() > 0.0001f ? Vector2.Normalize(away) : Vector2.UnitX;
                crew.Goal = new MapCoordinates(here.Position + away * BackOffStep, here.MapId);
            }
            else
            {
                crew.Goal = null;
            }

            return;
        }

        var unseen = now - crew.LastSeenAt;
        if (crew.LastSeenTarget is not { } last || unseen > HuntGiveUp)
        {
            crew.Target = null;
            crew.LastSeenTarget = null;
            ChangeBehaviour(crew, crew.PreviousBehaviour);
            return;
        }

        crew.Goal = unseen > HuntChaseDelay ? last : null;
    }

    /// <summary>Keeps station behind the lead; without one, holds where it is.</summary>
    private void UpdateEscort(WFVehicleCrewComponent crew, MapCoordinates here)
    {
        if (crew.Lead is not { } lead || TerminatingOrDeleted(lead))
        {
            crew.Lead = null;
            crew.Home = here;
            crew.Behaviour = WFVehicleBehaviour.Hold;
            crew.Goal = null;
            return;
        }

        var leadPosition = _transform.GetMapCoordinates(lead);
        if (leadPosition.MapId != here.MapId)
        {
            crew.Goal = null;
            return;
        }

        var gap = Distance(here, leadPosition);
        if (gap >= EscortDistance - EscortSlack && gap <= EscortDistance + EscortSlack)
        {
            crew.Goal = null;
            return;
        }

        var behind = leadPosition.Position - _transform.GetWorldRotation(lead).ToWorldVec() * EscortDistance;
        crew.Goal = new MapCoordinates(behind, leadPosition.MapId);
    }

    /// <summary>Runs from the latest attacker, toward home when home lies away from him, until left alone a while.</summary>
    private void UpdateFlee(WFVehicleCrewComponent crew, MapCoordinates here, TimeSpan now)
    {
        if (now - crew.LastHit > FleeCalm)
        {
            ChangeBehaviour(crew, crew.PreviousBehaviour);
            return;
        }

        EntityUid? threat = null;
        var latest = TimeSpan.MinValue;
        foreach (var (attacker, at) in crew.Attackers)
        {
            if (at <= latest || TerminatingOrDeleted(attacker))
                continue;

            threat = attacker;
            latest = at;
        }

        var threatPosition = threat is { } found ? _transform.GetMapCoordinates(found) : MapCoordinates.Nullspace;
        if (threatPosition.MapId != here.MapId)
        {
            GoHome(crew, here, ArriveDistance);
            return;
        }

        var away = here.Position - threatPosition.Position;
        away = away.LengthSquared() > 0.0001f ? Vector2.Normalize(away) : Vector2.UnitX;
        var toHome = crew.Home.Position - here.Position;
        if (crew.Home.MapId == here.MapId && toHome.Length() > ArriveDistance && Vector2.Dot(toHome, away) > 0f)
        {
            crew.Goal = crew.Home;
            return;
        }

        if (crew.Goal is not { } goal || Distance(here, goal) <= ArriveDistance || Distance(goal, crew.Home) < 0.01f)
            crew.Goal = new MapCoordinates(here.Position + away * FleeDistance, here.MapId);
    }
}
