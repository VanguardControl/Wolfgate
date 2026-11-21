using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared.Mind;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.ColdStorage;

/// <summary>
/// Who attends which grid: viewers (bodies, ghosts, camera eyes and announced arrivals) found through each map's grid
/// tree, bodies with a mind aboard, and the docked grids of an attended one.
/// </summary>
public sealed partial class WGGridStasisSystem
{
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>Metres from each attended grid's bounds to its nearest attendee; 0 for someone aboard.</summary>
    private readonly Dictionary<EntityUid, float> _distances = new();

    /// <summary>Docked neighbours of each grid, stations left out: a station never attends what is docked to it.</summary>
    private readonly Dictionary<EntityUid, List<EntityUid>> _docks = new();

    private readonly List<(MapId Map, Vector2 Position, TimeSpan Until)> _arrivals = new();
    private readonly List<MapCoordinates> _viewers = new();
    private readonly HashSet<EntityUid> _visited = new();
    private readonly List<EntityUid> _group = new();
    private List<Entity<MapGridComponent>> _found = new();

    private EntityQuery<VehicleInteriorOccupantComponent> _occupantQuery;
    private EntityQuery<EyeComponent> _eyeQuery;

    private void InitializeAttendance()
    {
        _occupantQuery = GetEntityQuery<VehicleInteriorOccupantComponent>();
        _eyeQuery = GetEntityQuery<EyeComponent>();
    }

    /// <summary>Someone will arrive at a spot, such as a jump's exit: a viewer there for the duration.</summary>
    public void NoteArrival(MapCoordinates at, TimeSpan duration)
    {
        if (at.MapId == MapId.Nullspace)
            return;

        _arrivals.Add((at.MapId, at.Position, _timing.CurTime + duration));
        _nextEvaluate = TimeSpan.Zero;
    }

    /// <summary>Where every viewer and announced arrival was at the last look, four times a second while stasis is on.</summary>
    public IReadOnlyList<MapCoordinates> Viewers => _viewers;

    /// <summary>Works out every attended grid's distance to its nearest attendee.</summary>
    private void ComputeAttendance()
    {
        _distances.Clear();
        _viewers.Clear();
        var now = _timing.CurTime;

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            if (session.AttachedEntity is { } body)
            {
                AddViewer(body);
                if (_eyeQuery.TryComp(body, out var eyeComp) && eyeComp.Target is { } target && target != body)
                    AddViewer(target);
            }

            foreach (var eye in session.ViewSubscriptions)
            {
                AddViewer(eye);
            }
        }

        for (var i = _arrivals.Count - 1; i >= 0; i--)
        {
            var (map, position, until) = _arrivals[i];
            if (until <= now)
                _arrivals.RemoveAt(i);
            else
                AddViewerAt(map, position);
        }

        // A body with a mind, a player's or a sleeping one's, keeps the grid it is on awake wherever its player is.
        var minds = AllEntityQuery<MindComponent>();
        while (minds.MoveNext(out _, out var mind))
        {
            if (mind.OwnedEntity is not { } owned)
                continue;

            if (_occupantQuery.TryComp(owned, out var occupant) && Exists(occupant.Vehicle))
                owned = occupant.Vehicle;

            if (_xformQuery.TryComp(owned, out var xform) && xform.GridUid is { } grid)
                _distances[grid] = 0f;
        }

        SpreadOverDocks();
    }

    /// <summary>Notes a viewer where an entity is; one in a vehicle's cabin is where the vehicle is.</summary>
    private void AddViewer(EntityUid uid)
    {
        if (_occupantQuery.TryComp(uid, out var occupant) && Exists(occupant.Vehicle))
            uid = occupant.Vehicle;

        if (!_xformQuery.TryComp(uid, out var xform) || xform.MapUid == null)
            return;

        if (xform.GridUid is { } grid && grid != xform.MapUid)
            _distances[grid] = 0f;

        AddViewerAt(xform.MapID, _transform.GetWorldPosition(xform));
    }

    /// <summary>Marks every grid within stash range of a spot with its distance from it, through the map's grid tree.</summary>
    private void AddViewerAt(MapId map, Vector2 position)
    {
        _viewers.Add(new MapCoordinates(position, map));
        var range = StashRange;
        _found.Clear();
        _map.FindGridsIntersecting(map, Box2.CenteredAround(position, new Vector2(range * 2f)), ref _found, approx: true, includeMap: false);
        foreach (var grid in _found)
        {
            var bounds = _transform.GetWorldMatrix(grid.Owner).TransformBox(grid.Comp.LocalAABB);
            var dx = MathF.Max(0f, MathF.Max(bounds.Left - position.X, position.X - bounds.Right));
            var dy = MathF.Max(0f, MathF.Max(bounds.Bottom - position.Y, position.Y - bounds.Top));
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance > range)
                continue;

            if (!_distances.TryGetValue(grid.Owner, out var known) || distance < known)
                _distances[grid.Owner] = distance;
        }
    }

    /// <summary>Lists which grids are docked to which, leaving stations out.</summary>
    private void RebuildDocks()
    {
        _docks.Clear();

        var query = AllEntityQuery<DockingComponent, TransformComponent>();
        while (query.MoveNext(out _, out var dock, out var xform))
        {
            if (dock.DockedWith is not { } other
                || xform.GridUid is not { } a
                || !_xformQuery.TryComp(other, out var otherXform)
                || otherXform.GridUid is not { } b
                || a == b
                || IsStation(a)
                || IsStation(b))
                continue;

            Link(a, b);
            Link(b, a);
        }
    }

    private void Link(EntityUid from, EntityUid to)
    {
        if (!_docks.TryGetValue(from, out var list))
            _docks[from] = list = new List<EntityUid>();

        if (!list.Contains(to))
            list.Add(to);
    }

    /// <summary>Gives every grid in a docked group the nearest attendee's distance of any of them.</summary>
    private void SpreadOverDocks()
    {
        _visited.Clear();
        foreach (var (start, links) in _docks)
        {
            if (links.Count == 0 || _visited.Contains(start))
                continue;

            _group.Clear();
            _group.Add(start);
            _visited.Add(start);
            var nearest = float.MaxValue;
            for (var i = 0; i < _group.Count; i++)
            {
                var grid = _group[i];
                if (_distances.TryGetValue(grid, out var distance))
                    nearest = MathF.Min(nearest, distance);

                if (!_docks.TryGetValue(grid, out var next))
                    continue;

                foreach (var neighbour in next)
                {
                    if (_visited.Add(neighbour))
                        _group.Add(neighbour);
                }
            }

            if (nearest == float.MaxValue)
                continue;

            foreach (var grid in _group)
            {
                _distances[grid] = nearest;
            }
        }
    }
}
