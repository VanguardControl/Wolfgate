using System.Numerics;
using Content.Server._WF.Vehicles.Npc;
using Content.Shared.Chat;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.Strangers;

/// <summary>Where strangers go when nobody has their attention: roamers wander the world, the rest keep to home and guard it.</summary>
public sealed partial class WFStrangerSystem
{
    /// <summary>How long between his warnings to the same person.</summary>
    private static readonly TimeSpan WarnCooldown = TimeSpan.FromSeconds(20);

    /// <summary>The home he spawned in, if any.</summary>
    private EntityUid? FindHome(EntityUid uid)
    {
        var here = _transform.GetMapCoordinates(uid);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var home in _lookup.GetEntitiesInRange<WFStrangerHomeComponent>(here, 24f))
        {
            var distance = Vector2.Distance(_transform.GetMapCoordinates(home).Position, here.Position);
            if (distance > home.Comp.Radius + 1f || distance >= bestDistance)
                continue;
            best = home;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>Gives a stranger a home to keep to and guard; null turns him loose to roam.</summary>
    public void SetHome(Entity<WFStrangerComponent> ent, EntityUid? home)
    {
        ent.Comp.Home = home;
        ent.Comp.NextWander = _timing.CurTime;
    }

    /// <summary>
    /// At home: whoever he sees coming gets told to clear off, whoever comes in gets set on by the lot of them. Anyone
    /// he's done business with lately is let be. Otherwise he potters about.
    /// </summary>
    private void Guard(Entity<WFStrangerComponent> ent, Entity<WFStrangerHomeComponent> home, TransformComponent xform, TimeSpan now)
    {
        var stranger = ent.Comp;
        if (stranger.Node != null)
        {
            if (stranger.Target is not { } partner || !Near(ent, partner, ConversationRange))
                EndConversation(ent, leave: false);
            return;
        }

        var centre = _transform.GetMapCoordinates(home);
        var here = _transform.GetMapCoordinates(ent, xform);
        EntityUid? warn = null;
        foreach (var intruder in Intruders(ent, here))
        {
            if (stranger.Done.TryGetValue(intruder, out var until) && now < until)
                continue;
            var there = _transform.GetMapCoordinates(intruder);
            var distance = Vector2.Distance(there.Position, centre.Position);
            if (there.MapId != centre.MapId || distance > home.Comp.WarnRadius)
                continue;
            if (distance <= home.Comp.Radius)
            {
                TurnOn(ent, intruder, rally: true);
                return;
            }
            warn ??= intruder;
        }

        if (warn is { } trespasser)
        {
            // He stops to watch them.
            _steering.Unregister(ent);
            stranger.NextWander = now + TimeSpan.FromSeconds(4);
            if (!stranger.Warned.TryGetValue(trespasser, out var next) || now >= next)
            {
                stranger.Warned[trespasser] = now + WarnCooldown;
                if (home.Comp.Warnings.Count > 0)
                    _chat.TrySendInGameICMessage(ent, Loc.GetString(_random.Pick(home.Comp.Warnings)), InGameICChatType.Speak, false);
            }
            return;
        }

        Wander(ent, xform, now, home, home.Comp.Radius * 0.8f);
    }

    /// <summary>Living players he can see, and any vehicle with someone inside.</summary>
    private IEnumerable<EntityUid> Intruders(Entity<WFStrangerComponent> ent, MapCoordinates here)
    {
        _seen.Clear();
        _lookup.GetEntitiesInRange(here, ent.Comp.SightRange, _seen);
        foreach (var other in _seen)
        {
            if (other.Owner != ent.Owner && _mobs.IsAlive(other) && HasComp<ActorComponent>(other)
                && _examine.InRangeUnOccluded(here, _transform.GetMapCoordinates(other), ent.Comp.SightRange, null))
                yield return other;
        }

        var vehicles = EntityQueryEnumerator<WFOccupiedVehicleComponent, TransformComponent>();
        while (vehicles.MoveNext(out var vehicle, out _, out var vehicleXform))
        {
            var there = _transform.GetMapCoordinates(vehicle, vehicleXform);
            if (there.MapId == here.MapId && Vector2.Distance(there.Position, here.Position) <= ent.Comp.SightRange
                && _examine.InRangeUnOccluded(here, there, ent.Comp.SightRange, null))
                yield return vehicle;
        }
    }

    /// <summary>
    /// Every so often he sets off for somewhere else: anywhere open within <paramref name="range"/> of himself, or of
    /// his home if he has one. Now and then he just stands about.
    /// </summary>
    private void Wander(Entity<WFStrangerComponent> ent, TransformComponent xform, TimeSpan now, EntityUid? home, float range)
    {
        if (now < ent.Comp.NextWander)
            return;
        ent.Comp.NextWander = now + TimeSpan.FromSeconds(_random.NextFloat(8f, 20f));
        _steering.Unregister(ent);
        if (_random.Prob(0.25f) || xform.GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return;

        var origin = home is { } centre ? _transform.GetWorldPosition(centre) : _transform.GetWorldPosition(xform);
        var inverse = _transform.GetInvWorldMatrix(grid);
        var boundsCentre = Vector2.Zero;
        var boundsRadius = 0f;
        var bounded = xform.MapUid is { } map && _bounds.TryGetBounds(map, out boundsCentre, out boundsRadius);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var spot = origin + _random.NextAngle().ToVec() * _random.NextFloat(range * 0.3f, range);
            if (bounded && Vector2.Distance(spot, boundsCentre) > boundsRadius * 0.9f)
                continue;
            var tile = Vector2.Transform(spot, inverse).Floored();
            if (_maps.GetTileRef(grid, gridComp, tile).Tile.IsEmpty || _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, tile).MoveNext(out _))
                continue;
            _steering.Register(ent, new EntityCoordinates(grid, (Vector2) tile + new Vector2(0.5f)));
            return;
        }
    }
}
