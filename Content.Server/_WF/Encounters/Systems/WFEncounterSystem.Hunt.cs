using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Ghost;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterSystem
{
    /// <summary>How far clear of its prey a raider flies before it jumps out.</summary>
    private const float RaidExit = 2500f;

    /// <summary>How long a raider stays docked at the least, and how long its boarding party is aboard the prey.</summary>
    private static readonly TimeSpan RaidTime = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan RecallLead = TimeSpan.FromSeconds(20);

    /// <summary>
    /// A hunting ship runs down the nearest ship with a player aboard and docks with it, trying again each time
    /// the prey shakes it off. Docked, its hands go aboard, fight whoever they see, carry a few things back,
    /// and the ship casts off and leaves.
    /// </summary>
    private void Hunt(WFEncounterShipState ship)
    {
        if (ship.Raided)
            return;

        // Docked: the guards go aboard to hold the prey while the hands loot, and are called back before casting off.
        if (ship.Prey is { } docked && !TerminatingOrDeleted(docked) && _docking.AreGridsDocked(ship.Grid, docked))
        {
            if (ship.RaidEnds == null)
            {
                ship.RaidEnds = _timing.CurTime + RaidTime;
                SendBoardingParty(ship, docked);
                // Nobody is left behind lightly: the pilot waits a good while for people still aboard the prey.
                var pilots = EntityQueryEnumerator<WFPilotDutyComponent, WFCrewComponent, TransformComponent>();
                while (pilots.MoveNext(out _, out var pilot, out var member, out var xform))
                {
                    if (member.Group == ship.Group && xform.GridUid == ship.Grid)
                        pilot.AbsentCrewWait = 180f;
                }
            }
            else if (_timing.CurTime >= ship.RaidEnds - RecallLead)
                RecallBoardingParty(ship);
        }
        else if (ship.RaidEnds != null)
        {
            RecallBoardingParty(ship);
            ship.RaidEnds = null;
        }

        var status = _objectives.QueueStatus(ship.Grid, ship.Group);
        if (status == "complete")
        {
            // The raid's orders are flown; the encounter completes and the ship jumps out.
            ship.Raided = true;
            ship.HasOrders = true;
            return;
        }

        if (ship.Prey is not { } prey || TerminatingOrDeleted(prey) || Transform(prey).MapID != Transform(ship.Grid).MapID)
        {
            ship.Prey = NearestCrewedShip(ship.Grid);
            if (ship.Prey is not { } found)
                return;

            prey = found;
            status = null;
        }

        // No orders yet, or the last run at the prey failed or lost it: go again.
        if (status is not (null or "dock-failed" or "target-lost"))
            return;

        var away = _transform.GetMapCoordinates(prey).Position + _random.NextAngle().ToVec() * RaidExit;
        _objectives.SetQueue(ship.Grid, ship.Group, new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.Loot, Target = GetNetEntity(prey) },
            // Even with nothing worth taking they stay a while: the boarding party is aboard looking for a fight.
            new() { Kind = WFCrewObjectiveKind.Hold, Duration = (float) RaidTime.TotalSeconds },
            new() { Kind = WFCrewObjectiveKind.Undock },
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = away, Range = 200f },
        });
    }

    /// <summary>
    /// Posts the ship's guards aboard the prey. A crewman's post decides which ship is his to hold, so aboard the
    /// prey they treat its people as intruders and fight them on sight.
    /// </summary>
    private void SendBoardingParty(WFEncounterShipState ship, EntityUid prey)
    {
        var guards = new List<Entity<WFCrewComponent>>();
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group == ship.Group && xform.GridUid == ship.Grid && member.Role == WFCrewRoles.Marine && _mobs.IsAlive(uid))
                guards.Add((uid, member));
        }

        var posts = _planner.Plan(prey, guards.Count);
        if (posts.Count == 0)
            return;

        for (var i = 0; i < guards.Count; i++)
        {
            ship.BoardingParty[guards[i]] = guards[i].Comp.Post;
            _crew.SetPost((guards[i], guards[i].Comp), posts[i % posts.Count].Coordinates);
        }

        Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} sends {guards.Count} aboard {ToPrettyString(prey)}.");
    }

    /// <summary>Gives the boarding party their own posts back, so they return and the ship waits for them.</summary>
    private void RecallBoardingParty(WFEncounterShipState ship)
    {
        foreach (var (uid, post) in ship.BoardingParty)
        {
            if (TryComp<WFCrewComponent>(uid, out var member) && post is { } home && !TerminatingOrDeleted(home.EntityId))
                _crew.SetPost((uid, member), home);
        }

        ship.BoardingParty.Clear();
    }

    /// <summary>The nearest ship on the same map with a living player aboard that is not part of an encounter.</summary>
    private EntityUid? NearestCrewedShip(EntityUid hunter)
    {
        var here = _transform.GetMapCoordinates(hunter);
        EntityUid? nearest = null;
        var best = float.MaxValue;
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || grid == hunter || xform.MapID != here.MapId
                || HasComp<GhostComponent>(uid) || _mobs.IsDead(uid) || HasComp<WFEncounterGridComponent>(grid))
                continue;

            var distance = (_transform.GetWorldPosition(grid) - here.Position).LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            nearest = grid;
        }

        return nearest;
    }
}
