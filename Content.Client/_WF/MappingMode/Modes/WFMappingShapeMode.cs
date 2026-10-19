using Content.Client._WF.MappingMode.Palette;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// Shape: drags a line, a filled rectangle or a hollow one of the selected tile or entity, sent as one request when
/// the button comes up.
/// </summary>
public sealed partial class WFMappingShapeMode(WFMappingBuildModeSystem modes) : WFMappingDragModeBase(modes)
{
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Color TooMany = Color.FromHex("#ff1744");

    public override WFMappingBuildMode Mode => WFMappingBuildMode.Shape;

    protected override Color PreviewColor => Color.FromHex("#66bb6a");

    protected override bool CanStart(EntityUid? grid)
    {
        if (Modes.Entry is not { Kind: WFMappingPaletteKind.Tile or WFMappingPaletteKind.Entity })
        {
            Modes.Popup("wf-mapping-screen-shape-needs-entry");
            return false;
        }

        if (grid == null)
        {
            Modes.Popup("wf-mapping-screen-needs-grid");
            return false;
        }

        return true;
    }

    protected override Box2i? Preview(Vector2i start, Vector2i end, List<Vector2i> cells, ref Color color)
    {
        if (Modes.ShapeKind == WFMappingShapeKind.Line)
        {
            cells.AddRange(MappingToolsShapes.Line(start, end));
            if (cells.Count > WFMappingModeLimits.MaxDrawCells)
                color = TooMany;

            return null;
        }

        var hollow = Modes.ShapeKind == WFMappingShapeKind.HollowRect;
        if (MappingToolsShapes.RectCount(start, end, hollow) > WFMappingModeLimits.MaxDrawCells)
            color = TooMany;

        // A filled rectangle is shaded as one box; a hollow one shows its border.
        if (!hollow)
            return Corners(start, end);

        cells.AddRange(MappingToolsShapes.Rect(start, end, true));
        return null;
    }

    protected override void Release(EntityUid? grid, Vector2i start, Vector2i end)
    {
        if (grid is not { } gridUid || Modes.Entry is not { } entry)
            return;

        var hollow = Modes.ShapeKind == WFMappingShapeKind.HollowRect;
        var count = Modes.ShapeKind == WFMappingShapeKind.Line
            ? Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y)) + 1
            : MappingToolsShapes.RectCount(start, end, hollow);
        if (count > WFMappingModeLimits.MaxDrawCells)
        {
            Modes.Popup("wf-mapping-screen-too-many", ("max", WFMappingModeLimits.MaxDrawCells));
            return;
        }

        var cells = Modes.ShapeKind == WFMappingShapeKind.Line
            ? MappingToolsShapes.Line(start, end)
            : MappingToolsShapes.Rect(start, end, hollow);
        var net = _entities.GetNetEntity(gridUid);

        if (Modes.TryGetTile(entry, out var tile))
        {
            Modes.SendDraw(new MappingToolsDrawEvent(net, MappingDrawOp.SetTiles, cells, tile.ID,
                Modes.TileRotation(tile), null, Angle.Zero));
            return;
        }

        Modes.SendDraw(new MappingToolsDrawEvent(net, MappingDrawOp.SpawnEntities, cells, null, 0, entry.Id,
            Modes.PlacementRotation));
    }

    public override bool Wheel(float delta)
    {
        return Modes.RotatePlacement(delta > 0 ? 1 : -1);
    }
}
