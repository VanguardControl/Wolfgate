using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.Encounters;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>Tells every player where the visible, unresolved encounters are, for the sector markers on radars.</summary>
public sealed partial class WFEncounterMarkerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private TimeSpan _next;
    private bool _sentAny;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        var ev = new WFEncounterMarkersEvent();
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            if (encounter.Hidden || encounter.Resolution != null)
                continue;

            // The marker sits on the first ship that still exists.
            foreach (var ship in encounter.Ships.Values)
            {
                if (TerminatingOrDeleted(ship.Grid))
                    continue;

                var position = _transform.GetMapCoordinates(ship.Grid);
                ev.Markers.Add(new WFEncounterMarker
                {
                    Name = encounter.Name,
                    Category = encounter.Category,
                    Map = position.MapId,
                    Position = position.Position,
                    Velocity = TryComp<PhysicsComponent>(ship.Grid, out var body) ? body.LinearVelocity : Vector2.Zero,
                    WarnRange = ship.WarnRange,
                    AttackRange = ship.AttackRange,
                });
                break;
            }
        }

        // One empty update after the last marker goes, then silence.
        if (ev.Markers.Count == 0 && !_sentAny)
            return;

        _sentAny = ev.Markers.Count > 0;
        RaiseNetworkEvent(ev, Filter.Broadcast());
    }
}
