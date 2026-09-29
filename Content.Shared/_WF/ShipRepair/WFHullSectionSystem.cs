using System.Numerics;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;

namespace Content.Shared._WF.ShipRepair;

/// <summary>SRD rules for broken hulls: which grid a click works on and where the hull may be rebuilt.</summary>
public sealed partial class WFHullSectionSystem : EntitySystem
{
    [Dependency] private IMapManager _mapMan = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How far a section may sit from its place on the hull, in tiles, and still be reattached.</summary>
    public const float ReattachRange = 32f;

    /// <summary>Half the side of the box tested for another grid on a tile; it stays inside the tile at any rotation.</summary>
    private const float OccupancyHalfExtent = 0.35f;

    /// <summary>
    /// The grid an SRD click works on: a section, hull or other grid with a tile under the click, then the user's own
    /// grid unless it is planet ground, then a hull beside the click, then planet ground.
    /// </summary>
    public Entity<MapGridComponent>? PickTarget(TransformComponent toolXform, Vector2 clickWorld)
    {
        var grids = new List<Entity<MapGridComponent>>();
        _mapMan.FindGridsIntersecting(toolXform.MapID, Box2.CenteredAround(clickWorld, new Vector2(0.1f, 0.1f)), ref grids, true, false);

        Entity<MapGridComponent>? section = null, hull = null, other = null;
        foreach (var grid in grids)
        {
            if (IsGround(grid) || _map.GetTileRef(grid, _map.WorldToTile(grid, grid.Comp, clickWorld)).Tile.IsEmpty)
                continue;

            if (IsSection(grid))
                section ??= grid;
            else if (HasComp<ShipRepairDataComponent>(grid))
                hull ??= grid;
            else
                other ??= grid;
        }

        if ((section ?? hull ?? other) is { } hit)
            return hit;

        // Standing on the ship and clicking into a hole in it.
        if (toolXform.GridUid is { } own && !IsGround(own) && TryComp<MapGridComponent>(own, out var ownGrid))
            return (own, ownGrid);

        grids.Clear();
        _mapMan.FindGridsIntersecting(toolXform.MapID, Box2.CenteredAround(clickWorld, new Vector2(1.5f, 1.5f)), ref grids, false, false);

        Entity<MapGridComponent>? near = null;
        foreach (var grid in grids)
        {
            if (IsGround(grid))
                continue;

            if (HasComp<ShipRepairDataComponent>(grid))
                return grid;

            near ??= grid;
        }

        if (near != null)
            return near;

        if (toolXform.GridUid is { } ground && TryComp<MapGridComponent>(ground, out var groundGrid))
            return (ground, groundGrid);

        return null;
    }

    /// <summary>Whether a grid is a section still linked to a hull; one given a snapshot of its own is a hull.</summary>
    public bool IsSection(EntityUid grid)
    {
        return TryComp<WFHullSectionComponent>(grid, out var section)
               && section.Hull != null
               && !HasComp<ShipRepairDataComponent>(grid);
    }

    /// <summary>The tile index a hull-local position falls in.</summary>
    public Vector2i TileOf(EntityUid grid, Vector2 local)
    {
        var size = CompOrNull<MapGridComponent>(grid)?.TileSize ?? 1;
        return new Vector2i((int) MathF.Floor(local.X / size), (int) MathF.Floor(local.Y / size));
    }

    /// <summary>Whether the SRD may rebuild this hull tile; tells the user why not.</summary>
    public bool CanRebuildAt(EntityUid tool, EntityUid user, EntityUid hull, Vector2i index)
    {
        if (!TryComp<MapGridComponent>(hull, out var grid))
            return false;

        string? reason = null;
        if (IsOccupied((hull, grid), index, null))
            reason = "wf-ship-repair-blocked";
        else if (IsReserved(hull, index))
            reason = "wf-ship-repair-reserved";

        if (reason == null)
            return true;

        // The client may not see every nearby grid, so it only skips predicting the repair.
        if (_net.IsServer)
            _popup.PopupEntity(Loc.GetString(reason), tool, user, PopupType.MediumCaution);

        return false;
    }

    /// <summary>Whether a grid other than the hull, planet ground or the ignored grid covers a hull tile in the world.</summary>
    public bool IsOccupied(Entity<MapGridComponent> hull, Vector2i index, EntityUid? ignore)
    {
        var xform = Transform(hull);
        var centre = Vector2.Transform(_map.TileCenterToVector(hull, index), _transform.GetWorldMatrix(xform));
        var grids = new List<Entity<MapGridComponent>>();
        _mapMan.FindGridsIntersecting(xform.MapID, Box2.CenteredAround(centre, new Vector2(OccupancyHalfExtent * 2)), ref grids, false, false);

        foreach (var grid in grids)
        {
            if (grid.Owner != hull.Owner && grid.Owner != ignore && !IsGround(grid))
                return true;
        }

        return false;
    }

    /// <summary>Whether a hull tile is the place of a linked section on the same map and within reattach range.</summary>
    public bool IsReserved(EntityUid hull, Vector2i index)
    {
        var query = EntityQueryEnumerator<WFHullSectionComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var section, out var grid))
        {
            if (section.Hull == hull
                && !_map.GetTileRef(uid, grid, index).Tile.IsEmpty
                && InReattachRange(hull, (uid, grid)))
                return true;
        }

        return false;
    }

    /// <summary>Whether a section is on its hull's map and within reattach range of its place on the hull.</summary>
    public bool InReattachRange(EntityUid hull, Entity<MapGridComponent> section)
    {
        var mapId = Transform(hull).MapID;
        if (mapId == MapId.Nullspace || Transform(section).MapID != mapId)
            return false;

        return HomeOffset(hull, section) <= ReattachRange * section.Comp.TileSize;
    }

    /// <summary>How far a section's centre sits from where it belongs on the hull.</summary>
    public float HomeOffset(EntityUid hull, Entity<MapGridComponent> section)
    {
        var centre = section.Comp.LocalAABB.Center;
        var home = Vector2.Transform(centre, _transform.GetWorldMatrix(hull));
        var actual = Vector2.Transform(centre, _transform.GetWorldMatrix(section));
        return Vector2.Distance(home, actual);
    }

    private bool IsGround(EntityUid grid)
    {
        return HasComp<MapComponent>(grid);
    }
}
