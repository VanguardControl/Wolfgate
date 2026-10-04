using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Ghost;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterSystem
{
    /// <summary>How far clear of its prey a raider flies before it jumps out.</summary>
    private const float RaidExit = 2500f;

    /// <summary>
    /// A hunting ship runs down the nearest ship with a player aboard and docks with it, trying again each time
    /// the prey shakes it off. Docked, its hands go aboard, fight whoever they see, carry a few things back,
    /// and the ship casts off and leaves.
    /// </summary>
    private void Hunt(WFEncounterShipState ship)
    {
        if (ship.Raided)
            return;

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
            new() { Kind = WFCrewObjectiveKind.Undock },
            new() { Kind = WFCrewObjectiveKind.GoTo, Position = away, Range = 200f },
        });
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
