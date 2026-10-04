using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Ghost;
using Robust.Shared.Map;
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
                ship.Boarded = docked;
                // They are here to steal, not to clear the ship: nobody goes looking for a fight.
                var raiders = EntityQueryEnumerator<WFCrewComponent>();
                while (raiders.MoveNext(out var raider, out var member))
                {
                    if (member.Group != ship.Group)
                        continue;

                    member.Pursues = false;
                    // Nobody is left behind lightly: the pilot waits a good while for people still aboard the prey.
                    if (TryComp<WFPilotDutyComponent>(raider, out var pilot))
                        pilot.AbsentCrewWait = 180f;
                }

                SendBoardingParty(ship, docked);
            }
            else if (_timing.CurTime >= ship.RaidEnds - RecallLead)
                RecallBoardingParty(ship);
        }
        else if (ship.RaidEnds != null)
        {
            // The prey broke away with the raid under way. Whoever is still aboard it fights on there, and the
            // ship gives them up and runs.
            ship.RaidEnds = null;
            if (Strand(ship))
            {
                ship.Raided = true;
                ship.HasOrders = true;
                var clear = _transform.GetMapCoordinates(ship.Grid).Position + _random.NextAngle().ToVec() * RaidExit;
                _objectives.SetQueue(ship.Grid, ship.Group, new List<WFCrewObjective>
                {
                    new() { Kind = WFCrewObjectiveKind.GoTo, Position = clear, Range = 200f },
                });
                return;
            }
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
    /// Leaves the raiders still aboard the prey to it: each holds where he stands, hunts whoever is aboard, and is
    /// no longer waited for. The rest get their own posts back. True if anyone was left.
    /// </summary>
    private bool Strand(WFEncounterShipState ship)
    {
        var left = 0;
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group != ship.Group || !_mobs.IsAlive(uid))
                continue;

            if (xform.GridUid is { } grid && grid != ship.Grid && grid == ship.Boarded)
            {
                ship.BoardingParty.Remove(uid);
                member.Pursues = true;
                _crew.SetPost((uid, member), xform.Coordinates);
                left++;
            }
        }

        RecallBoardingParty(ship);
        return left > 0;
    }

    /// <summary>The prey's side of the docking port the raider came in by.</summary>
    private EntityCoordinates? Breach(EntityUid raider, EntityUid prey)
    {
        var docks = EntityQueryEnumerator<Content.Server.Shuttles.Components.DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid == raider && dock.DockedWith is { } other && Transform(other).GridUid == prey)
                return Transform(other).Coordinates;
        }

        return null;
    }

    /// <summary>
    /// Posts the ship's guards at the breach, on the prey's side. A crewman's post decides which ship is his to
    /// hold, so there they treat its people as intruders and fight the ones they see, while the hands loot.
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

        if (Breach(ship.Grid, prey) is not { } breach)
            return;

        for (var i = 0; i < guards.Count; i++)
        {
            ship.BoardingParty[guards[i]] = guards[i].Comp.Post;
            _crew.SetPost((guards[i], guards[i].Comp), breach);
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
