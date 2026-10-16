using System.Numerics;
using Content.Server.Decals;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.Vehicles.Core;

/// <summary>Distance-spaced, cleanable tread marks with a bounded lifetime and global count.</summary>
public sealed class WFVehicleTrackSystem : EntitySystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly Dictionary<EntityUid, (EntityUid Grid, Vector2 Position)> _last = new();
    private readonly Queue<(EntityUid Grid, uint Id, TimeSpan Expires)> _marks = new();
    private float _elapsed;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFVehicleEngineComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<WFVehicleEngineComponent> ent, ref ComponentShutdown args)
    {
        _last.Remove(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 0.1f)
            return;
        _elapsed = 0f;

        while (_marks.TryPeek(out var mark) && (mark.Expires <= _timing.CurTime || _marks.Count > 2048))
        {
            _marks.Dequeue();
            if (!Deleted(mark.Grid))
                _decals.RemoveDecal(mark.Grid, mark.Id);
        }

        var query = EntityQueryEnumerator<WFVehicleEngineComponent, GridVehicleMoverComponent>();
        while (query.MoveNext(out var uid, out _, out var mover))
        {
            if (Transform(uid).GridUid is not { } grid)
            {
                _last.Remove(uid);
                continue;
            }
            if (!_last.TryGetValue(uid, out var last) || last.Grid != grid)
            {
                _last[uid] = (grid, mover.Position);
                continue;
            }
            var distance = Vector2.Distance(last.Position, mover.Position);
            if (distance < 0.6f)
                continue;
            _last[uid] = (grid, mover.Position);
            if (distance > 5f)
                continue;
            var heading = mover.Heading;
            var side = heading.RotateVec(new Vector2(0.7f, 0f));
            foreach (var offset in new[] { side, -side })
            {
                var coordinates = new EntityCoordinates(grid, mover.Position + offset);
                if (_decals.TryAddDecal("WFVehicleTrack", coordinates, out var id,
                        Color.FromHex("#35302688"), mover.Heading, cleanable: true))
                    _marks.Enqueue((grid, id, _timing.CurTime + TimeSpan.FromMinutes(3)));
            }
        }
    }
}
