using System.Linq;
using System.Numerics;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Database;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    private readonly WFMappingThrottle<NetUserId> _gridThrottle = new(2, 4);

    /// <summary>How far off whole tiles, or radians off a right angle, a grid may sit and still count as aligned.</summary>
    private const float SnapTolerance = 0.001f;

    /// <summary>The largest selection MappingTools accepts, in cells; it ignores bigger ones.</summary>
    private const long MaxDeleteCells = 256 * 256;

    private void InitializeGrids()
    {
        SubscribeNetworkEvent<WFMappingGridListRequest>(OnGridListRequest);
        SubscribeNetworkEvent<WFMappingGoToGridRequest>(OnGoToGridRequest);
        SubscribeNetworkEvent<WFMappingDeleteGridRequest>(OnDeleteGridRequest);
        SubscribeNetworkEvent<WFMappingSnapGridRequest>(OnSnapGridRequest);
    }

    /// <summary>The grids directly on a map, most tiles first.</summary>
    public List<WFMappingGridInfo> GridsOn(EntityUid map)
    {
        var grids = new List<WFMappingGridInfo>();
        var children = Transform(map).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<MapGridComponent>(child, out var grid) || TerminatingOrDeleted(child))
                continue;

            grids.Add(new WFMappingGridInfo(GetNetEntity(child), MetaData(child).EntityName, CountTiles(child, grid),
                CountGridEntities(child), IsAligned(child)));
        }

        grids.Sort((a, b) => b.Tiles.CompareTo(a.Tiles));
        return grids;
    }

    /// <summary>Whether a grid sits on whole map tiles at a right angle.</summary>
    public bool IsAligned(EntityUid grid)
    {
        var (position, rotation) = SnappedPose(grid);
        var xform = Transform(grid);
        return Vector2.DistanceSquared(xform.LocalPosition, position) < SnapTolerance * SnapTolerance &&
               Math.Abs(Angle.ShortestDistance(xform.LocalRotation, rotation).Theta) < SnapTolerance;
    }

    /// <summary>A grid's position rounded to whole tiles and its rotation to the nearest right angle.</summary>
    private (Vector2 Position, Angle Rotation) SnappedPose(EntityUid grid)
    {
        var xform = Transform(grid);
        var position = new Vector2(MathF.Round(xform.LocalPosition.X), MathF.Round(xform.LocalPosition.Y));
        var rotation = new Angle(Math.Round(xform.LocalRotation.Theta / (Math.PI / 2)) * (Math.PI / 2));
        return (position, rotation);
    }

    /// <summary>Everything under a grid, contents included, but not the players standing on it.</summary>
    private int CountGridEntities(EntityUid grid)
    {
        var count = 0;
        var stack = new Stack<EntityUid>();
        stack.Push(grid);
        while (stack.TryPop(out var uid))
        {
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<ActorComponent>(child) || HasComp<WFMappingObserverComponent>(child))
                    continue;

                count++;
                stack.Push(child);
            }
        }

        return count;
    }

    private int CountTiles(EntityUid uid, MapGridComponent grid)
    {
        var count = 0;
        var tiles = _map.GetAllTiles(uid, grid);
        while (tiles.MoveNext(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>The centre of a grid's bounds in world space.</summary>
    public Vector2 GridCentre(EntityUid grid)
    {
        return _transform.GetWorldMatrix(grid).TransformBox(Comp<MapGridComponent>(grid).LocalAABB).Center;
    }

    /// <summary>The centre of the bounds of the grid with the most tiles, or null when there is no grid.</summary>
    public Vector2? LargestGridCentre(IEnumerable<EntityUid> grids)
    {
        EntityUid? largest = null;
        var most = -1;
        foreach (var uid in grids)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<MapGridComponent>(uid, out var grid))
                continue;

            var tiles = CountTiles(uid, grid);
            if (tiles <= most)
                continue;

            most = tiles;
            largest = uid;
        }

        return largest is { } found ? GridCentre(found) : null;
    }

    /// <summary>The centre of the largest grid directly on a map, or null when it has none.</summary>
    public Vector2? LargestGridCentre(EntityUid map)
    {
        var grids = new List<EntityUid>();
        var children = Transform(map).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            grids.Add(child);
        }

        return LargestGridCentre(grids);
    }

    private void OnGridListRequest(WFMappingGridListRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetPresent(player, out var session) ||
            !TryThrottle(_gridThrottle, player, "grid list") ||
            player.AttachedEntity is not { } observer ||
            Transform(observer).MapUid is not { } map ||
            !IsSessionMap(map, session))
        {
            return;
        }

        RaiseNetworkEvent(new WFMappingGridListEvent(GridsOn(map)), player);
    }

    private void OnGoToGridRequest(WFMappingGoToGridRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetPresent(player, out var session) ||
            !TryThrottle(_gridThrottle, player, "go to grid") ||
            player.AttachedEntity is not { } observer)
        {
            return;
        }

        if (!TryGetEntity(ev.Grid, out var grid) ||
            TerminatingOrDeleted(grid) ||
            !HasComp<MapGridComponent>(grid) ||
            Transform(grid.Value).MapUid is not { } map ||
            !IsSessionMap(map, session))
        {
            Refuse(player, "wf-mapping-ship-goto-not-yours");
            return;
        }

        MoveObserver(observer, map, GridCentre(grid.Value));
    }

    /// <summary>Deletes a whole grid through MappingTools, so undo brings it back with everything on it.</summary>
    private void OnDeleteGridRequest(WFMappingDeleteGridRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetPresent(player, out var session) ||
            !TryThrottle(_gridThrottle, player, "delete grid"))
        {
            return;
        }

        if (!TryGetEntity(ev.Grid, out var gridUid) ||
            TerminatingOrDeleted(gridUid) ||
            !TryComp<MapGridComponent>(gridUid, out var grid) ||
            Transform(gridUid.Value).MapUid is not { } map ||
            !IsSessionMap(map, session))
        {
            Refuse(player, "wf-mapping-ship-goto-not-yours");
            return;
        }

        var aabb = grid.LocalAABB;
        var area = new Box2i((int) MathF.Floor(aabb.Left), (int) MathF.Floor(aabb.Bottom),
            (int) MathF.Ceiling(aabb.Right), (int) MathF.Ceiling(aabb.Top));
        if (area.Width <= 0 || area.Height <= 0 || (long) area.Width * area.Height > MaxDeleteCells)
        {
            Refuse(player, "wf-mapping-mode-menu-delete-grid-too-large");
            return;
        }

        _tools.Delete(player, new MappingSelection { Grid = ev.Grid, Area = area });
        RaiseNetworkEvent(new WFMappingGridListEvent(GridsOn(map)), player);
    }

    /// <summary>Moves a grid onto whole tiles and the nearest right angle, so it lines up with other levels.</summary>
    private void OnSnapGridRequest(WFMappingSnapGridRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetPresent(player, out var session) ||
            !TryThrottle(_gridThrottle, player, "snap grid"))
        {
            return;
        }

        if (!TryGetEntity(ev.Grid, out var grid) ||
            TerminatingOrDeleted(grid) ||
            !HasComp<MapGridComponent>(grid) ||
            Transform(grid.Value).ParentUid is not { Valid: true } map ||
            !HasComp<MapComponent>(map) ||
            !IsSessionMap(map, session))
        {
            Refuse(player, "wf-mapping-ship-goto-not-yours");
            return;
        }

        if (IsAligned(grid.Value))
        {
            Notify(player, true, "wf-mapping-mode-menu-snap-grid-already");
            return;
        }

        var (position, rotation) = SnappedPose(grid.Value);
        _transform.SetLocalPositionRotation(grid.Value, position, rotation);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} snapped mapping grid {ToPrettyString(grid.Value):grid} to {position}, {rotation}");
        Notify(player, true, "wf-mapping-mode-menu-snap-grid-done");
        RaiseNetworkEvent(new WFMappingGridListEvent(GridsOn(map)), player);
    }

    /// <summary>Puts an observer at a world position on a map.</summary>
    private void MoveObserver(EntityUid observer, EntityUid map, Vector2 position)
    {
        _transform.SetMapCoordinates(observer, new MapCoordinates(position, Comp<MapComponent>(map).MapId));
    }

    /// <summary>Puts a player's observer at the centre of the largest of some grids, if they are present.</summary>
    private void MoveToLargest(ICommonSession player, EntityUid map, IEnumerable<EntityUid> grids)
    {
        if (player.AttachedEntity is { } observer && LargestGridCentre(grids.ToList()) is { } centre)
            MoveObserver(observer, map, centre);
    }
}
