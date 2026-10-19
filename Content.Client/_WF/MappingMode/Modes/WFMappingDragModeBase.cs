using Robust.Shared.Input.Binding;
using Robust.Shared.Map;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// A mode that works on the cells between a press and a release of the left button, with a live preview. Right
/// click and Escape drop the drag; when nothing is held the hovered cell is previewed.
/// </summary>
public abstract partial class WFMappingDragModeBase(WFMappingBuildModeSystem modes)
    : WFMappingModeHandlerBase(modes), IWFMappingPreviewSource
{
    protected bool Pressing { get; private set; }

    /// <summary>The grid the press landed on, or null off every grid.</summary>
    protected EntityUid? PressGrid { get; private set; }

    protected Vector2i PressCell { get; private set; }

    protected EntityCoordinates PressCoordinates { get; private set; }

    protected EntityUid PressClicked { get; private set; }

    protected virtual Color PreviewColor => Color.FromHex("#4fc3f7");

    public override void Exit()
    {
        Pressing = false;
    }

    public override bool Primary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (IsDown(args))
        {
            Pressing = false;
            var position = Modes.ToMap(args.Coordinates);
            EntityUid? grid = Modes.TryGetGridCell(position, out var found, out var cell) ? found.Owner : null;
            if (!CanStart(grid))
                return true;

            Pressing = true;
            PressGrid = grid;
            PressCell = cell;
            PressCoordinates = args.Coordinates;
            PressClicked = args.EntityUid;
            return true;
        }

        if (!Pressing)
            return true;

        Pressing = false;
        var end = PressCell;
        if (PressGrid is { } pressGrid && Modes.TryGetCellOn(pressGrid, Modes.ToMap(args.Coordinates), out var released))
            end = released;

        Release(PressGrid, PressCell, end);
        return true;
    }

    public override bool Secondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        Pressing = false;
        return true;
    }

    public override bool Cancel()
    {
        if (!Pressing)
            return false;

        Pressing = false;
        return true;
    }

    public bool TryGetPreview(List<Vector2i> cells, out EntityUid grid, out Box2i? box, out Color color)
    {
        cells.Clear();
        box = null;
        color = PreviewColor;

        if (Pressing && PressGrid is { } pressGrid)
        {
            grid = pressGrid;
            if (!Modes.TryGetCellOn(pressGrid, Modes.MouseMapPosition, out var end))
                end = PressCell;

            box = Preview(PressCell, end, cells, ref color);
            return true;
        }

        if (!Modes.TryGetGridCell(Modes.MouseMapPosition, out var hovered, out var cell))
        {
            grid = default;
            return false;
        }

        grid = hovered.Owner;
        cells.Add(cell);
        return true;
    }

    /// <summary>Whether a press may start a drag here; refusals tell the player why.</summary>
    protected virtual bool CanStart(EntityUid? grid)
    {
        return true;
    }

    /// <summary>Fills the cells the drag would change, or returns a box to shade instead.</summary>
    protected abstract Box2i? Preview(Vector2i start, Vector2i end, List<Vector2i> cells, ref Color color);

    /// <summary>The button came up; <paramref name="start"/> equals <paramref name="end"/> for a plain click.</summary>
    protected abstract void Release(EntityUid? grid, Vector2i start, Vector2i end);

    /// <summary>The cells from one corner to the other, both included, as a box with right and top exclusive.</summary>
    protected static Box2i Corners(Vector2i a, Vector2i b)
    {
        return new Box2i(Vector2i.ComponentMin(a, b), Vector2i.ComponentMax(a, b) + Vector2i.One);
    }
}
