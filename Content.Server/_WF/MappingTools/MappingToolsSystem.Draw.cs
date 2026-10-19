using System.Numerics;
using Content.Shared._WF.MappingTools;
using Content.Shared.Database;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingTools;

public sealed partial class MappingToolsSystem
{
    [Dependency] private TileSystem _tileSystem = default!;

    /// <summary>Most cells one draw request may hold for anyone; mapping mode checks its own lower limit.</summary>
    private const int MaxDrawCells = 16384;

    private void OnDraw(MappingToolsDrawEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            Draw(args.SenderSession, ev);
    }

    /// <summary>Lays tiles, spawns entities or removes decals over a set of cells, as one undoable edit.</summary>
    public void Draw(ICommonSession session, MappingToolsDrawEvent ev)
    {
        var cells = ev.Cells;
        if (cells.Count == 0)
            return;

        if (cells.Count > MaxDrawCells)
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-tools-draw-too-many", ("max", MaxDrawCells)), session);
            return;
        }

        var gridUid = GetEntity(ev.Grid);
        if (new HashSet<Vector2i>(cells).Count != cells.Count ||
            !TryComp(gridUid, out MapGridComponent? gridComp) ||
            TerminatingOrDeleted(gridUid) ||
            Transform(gridUid).MapUid is not { } map)
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-tools-draw-bad"), session);
            return;
        }

        var grid = new Entity<MapGridComponent>(gridUid, gridComp);
        var box = Bounds(cells);
        switch (ev.Op)
        {
            case MappingDrawOp.SetTiles:
                DrawTiles(session, grid, map, ev, box);
                break;
            case MappingDrawOp.SpawnEntities:
                DrawEntities(session, grid, map, ev, box);
                break;
            case MappingDrawOp.EraseDecals:
                EraseDecals(session, grid, map, ev, box);
                break;
        }
    }

    private void DrawTiles(ICommonSession session, Entity<MapGridComponent> grid, EntityUid map,
        MappingToolsDrawEvent ev, Box2i box)
    {
        if (ev.TileId == null ||
            !_tileDefs.TryGetDefinition(ev.TileId, out var definition) ||
            definition is not ContentTileDefinition tileDef)
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-tools-draw-bad-tile"), session);
            return;
        }

        var clearing = tileDef.TileId == Tile.Empty.TypeId;
        var rotation = tileDef.AllowRotationMirror ? (byte) (ev.TileRotationMirroring & 7) : (byte) 0;
        var changes = new List<MappingTileChange>();
        foreach (var cell in ev.Cells)
        {
            var old = TileAt(grid, cell);
            if (clearing ? old.IsEmpty : old.TypeId == tileDef.TileId && old.RotationMirroring == rotation)
                continue;

            var tile = clearing
                ? Tile.Empty
                : new Tile(tileDef.TileId, variant: _tileSystem.PickVariant(tileDef), rotationMirroring: rotation);
            changes.Add(new MappingTileChange(grid.Owner, cell, old, tile));
        }

        if (changes.Count == 0)
            return;

        // Clearing every tile deletes the grid, so the grid goes as one root that undo brings back whole.
        var wholeGrid = clearing && ClearsGrid(grid, changes.Count);
        var check = new WFMappingCanEditEvent(session, map, clearing ? WFMappingEditKind.Edit : WFMappingEditKind.Paste)
        {
            Grid = grid.Owner,
            Cells = box,
            Tiles = ev.Cells.Count,
            SelectionCells = ev.Cells.Count,
            TouchedEntities = wholeGrid ? Tree(grid.Owner).Count : 0,
        };

        if (!CanEdit(check))
            return;

        var edit = new MappingEdit("wf-mapping-tools-edit-draw-tiles");
        if (wholeGrid)
        {
            var gridGroup = new MappingEntityGroup();
            gridGroup.Roots.Add(new MappingGroupRoot(GetRef(grid.Owner), -1, PoseOf(grid.Owner)));
            edit.Deleted.Add(gridGroup);
        }
        else
        {
            edit.Tiles.AddRange(changes);
        }

        Commit(session, edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to draw {changes.Count} {tileDef.ID} tiles on {ToPrettyString(grid.Owner):grid}");
    }

    /// <summary>Whether clearing <paramref name="cleared"/> filled tiles empties a grid that isn't also a map.</summary>
    private bool ClearsGrid(Entity<MapGridComponent> grid, int cleared)
    {
        if (HasComp<MapComponent>(grid) || Transform(grid).MapUid is null)
            return false;

        var count = 0;
        var tiles = _map.GetAllTiles(grid, grid.Comp);
        while (tiles.MoveNext(out _))
        {
            if (++count > cleared)
                return false;
        }

        return count == cleared;
    }

    private void DrawEntities(ICommonSession session, Entity<MapGridComponent> grid, EntityUid map,
        MappingToolsDrawEvent ev, Box2i box)
    {
        if (ev.Prototype == null ||
            !_prototypes.TryIndex<EntityPrototype>(ev.Prototype, out var prototype) ||
            prototype.Abstract)
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-tools-draw-bad-prototype"), session);
            return;
        }

        var check = new WFMappingCanEditEvent(session, map, WFMappingEditKind.Paste)
        {
            Grid = grid.Owner,
            Cells = box,
            Entities = ev.Cells.Count,
            EntityCells = new List<Vector2i>(ev.Cells),
            SelectionCells = ev.Cells.Count,
            TouchedEntities = ev.Cells.Count,
            Prototypes = new List<string> { prototype.ID },
        };

        if (!CanEdit(check))
            return;

        var rotation = double.IsFinite(ev.Rotation.Theta) ? ev.Rotation.Reduced() : Angle.Zero;
        var group = new MappingEntityGroup();
        foreach (var cell in ev.Cells)
        {
            var uid = Spawn(prototype.ID, new EntityCoordinates(grid.Owner, new Vector2(cell.X + 0.5f, cell.Y + 0.5f)));
            _transform.SetLocalRotation(uid, rotation);
            group.Roots.Add(new MappingGroupRoot(GetRef(uid), -1, PoseOf(uid)));
        }

        var edit = new MappingEdit("wf-mapping-tools-edit-draw-entities");
        edit.Created.Add(group);
        Push(session, edit);
        RaiseEdited(edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to draw {ev.Cells.Count} {prototype.ID} on {ToPrettyString(grid.Owner):grid}");
    }

    private void EraseDecals(ICommonSession session, Entity<MapGridComponent> grid, EntityUid map,
        MappingToolsDrawEvent ev, Box2i box)
    {
        var cells = new HashSet<Vector2i>(ev.Cells);
        var edit = new MappingEdit("wf-mapping-tools-edit-erase-decals");
        var bounds = new Box2(box.Left - 1, box.Bottom - 1, box.Right + 1, box.Top + 1);
        foreach (var (_, decal) in _decals.GetDecalsIntersecting(grid.Owner, bounds))
        {
            if (cells.Contains(MappingToolsMath.CellOf(decal.Coordinates + new Vector2(0.5f))))
                edit.DecalsRemoved.Add(new MappingDecal(grid.Owner, decal));
        }

        if (edit.IsEmpty)
            return;

        var check = new WFMappingCanEditEvent(session, map, WFMappingEditKind.Edit)
        {
            Grid = grid.Owner,
            Cells = box,
            SelectionCells = ev.Cells.Count,
            Decals = edit.DecalsRemoved.Count,
        };

        if (!CanEdit(check))
            return;

        Commit(session, edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to erase {edit.DecalsRemoved.Count} decals on {ToPrettyString(grid.Owner):grid}");
    }

    /// <summary>The box round a set of cells, right and top exclusive.</summary>
    private static Box2i Bounds(List<Vector2i> cells)
    {
        var min = cells[0];
        var max = cells[0];
        foreach (var cell in cells)
        {
            min = Vector2i.ComponentMin(min, cell);
            max = Vector2i.ComponentMax(max, cell);
        }

        return new Box2i(min, max + Vector2i.One);
    }
}
