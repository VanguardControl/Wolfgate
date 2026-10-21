using System.Linq;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.Vehicles.Npc;

/// <summary>
/// An NPC that climbs into vehicles to get at whoever is inside: a hostile one near an occupied vehicle walks to its
/// nearest door and, unlocked, climbs in and fights in the cabin. A locked vehicle it just attacks from outside.
/// </summary>
[RegisterComponent]
public sealed partial class WFVehicleBoarderComponent : Component
{
    /// <summary>How far off he notices a vehicle worth boarding.</summary>
    [DataField]
    public float Range = 12f;

    /// <summary>The vehicle he's making for, if any.</summary>
    [ViewVariables]
    public EntityUid? Vehicle;

    /// <summary>When he gives up getting in.</summary>
    [ViewVariables]
    public TimeSpan GiveUpAt;

    /// <summary>When he next looks for a vehicle to board.</summary>
    [ViewVariables]
    public TimeSpan NextLook;
}

public sealed class WFNpcBoardingSystem : EntitySystem
{
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VehicleInteriorSystem _interiors = default!;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LookInterval = TimeSpan.FromSeconds(1);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFVehicleBoarderComponent, ActiveNPCComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var boarder, out _, out var htn))
        {
            if (now < boarder.NextLook)
                continue;
            boarder.NextLook = now + TimeSpan.FromSeconds(0.5);

            if (!_mobs.IsAlive(uid) || HasComp<VehicleInteriorOccupantComponent>(uid))
            {
                Stop((uid, boarder), htn);
                continue;
            }

            if (boarder.Vehicle is { } vehicle)
            {
                Board((uid, boarder), htn, vehicle, now);
                continue;
            }

            // Only one already out for blood goes looking for a way in.
            if (!htn.Enabled || FindVehicle((uid, boarder)) is not { } target)
            {
                boarder.NextLook = now + LookInterval;
                continue;
            }

            boarder.Vehicle = target;
            boarder.GiveUpAt = now + Patience;
            _htn.SetHTNEnabled((uid, htn), false);
        }
    }

    private void Board(Entity<WFVehicleBoarderComponent> ent, HTNComponent htn, EntityUid vehicle, TimeSpan now)
    {
        if (TerminatingOrDeleted(vehicle) || now >= ent.Comp.GiveUpAt || _interiors.IsLockedAgainst(vehicle, ent)
            || !HasComp<WFOccupiedVehicleComponent>(vehicle)
            || !_interiors.TryGetNearestEntry(vehicle, ent, out var door) || door.MapId != Transform(ent).MapID)
        {
            Stop(ent, htn);
            ent.Comp.NextLook = now + Patience;
            return;
        }

        var here = _transform.GetMapCoordinates(ent);
        if ((here.Position - door.Position).Length() > 1f)
        {
            if (Transform(ent).GridUid is { } grid)
                _steering.Register(ent, _transform.ToCoordinates(grid, door));
            else if (Transform(ent).MapUid is { } map)
                _steering.Register(ent, _transform.ToCoordinates(map, door));
            return;
        }

        _steering.Unregister(ent);
        _interiors.TryNpcEnter(vehicle, ent);
    }

    /// <summary>Back to fighting on his own, inside or out.</summary>
    private void Stop(Entity<WFVehicleBoarderComponent> ent, HTNComponent htn)
    {
        if (ent.Comp.Vehicle == null)
            return;
        ent.Comp.Vehicle = null;
        _steering.Unregister(ent);
        _htn.SetHTNEnabled((ent, htn), true);
    }

    /// <summary>The nearest unlocked vehicle with someone inside he's hostile to, or the vehicle itself is.</summary>
    private EntityUid? FindVehicle(Entity<WFVehicleBoarderComponent> ent)
    {
        var hostiles = _factions.GetNearbyHostiles(ent.Owner, ent.Comp.Range).ToHashSet();
        if (hostiles.Count == 0 && !HasComp<FactionExceptionComponent>(ent))
            return null;

        var here = _transform.GetMapCoordinates(ent);
        EntityUid? best = null;
        var bestDistance = ent.Comp.Range;
        var query = EntityQueryEnumerator<WFOccupiedVehicleComponent, VehicleInteriorComponent, TransformComponent>();
        while (query.MoveNext(out var vehicle, out _, out var interior, out var xform))
        {
            if (xform.MapID != here.MapId)
                continue;
            var distance = (_transform.GetWorldPosition(xform) - here.Position).Length();
            if (distance >= bestDistance || _interiors.IsLockedAgainst(vehicle, ent))
                continue;
            if (!hostiles.Contains(vehicle) && !interior.Passengers.Any(hostiles.Contains))
                continue;
            best = vehicle;
            bestDistance = distance;
        }
        return best;
    }
}
