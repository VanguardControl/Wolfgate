using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;

namespace Content.Client._WF.MappingMode.Modes;

/// <summary>
/// Erase: a click removes the entity, tile or decal under the cursor, as the sub-choice says; a drag removes them
/// over a rectangle.
/// </summary>
public sealed partial class WFMappingEraseMode(WFMappingBuildModeSystem modes) : WFMappingDragModeBase(modes)
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>The rectangle erase is sent slightly inside its cells, so neighbours touching the edge stay.</summary>
    private const float RectInset = 0.02f;

    public override WFMappingBuildMode Mode => WFMappingBuildMode.Erase;

    protected override Color PreviewColor => Color.FromHex("#ef5350");

    protected override Box2i? Preview(Vector2i start, Vector2i end, List<Vector2i> cells, ref Color color)
    {
        var box = Corners(start, end);
        if ((long) box.Width * box.Height > WFMappingModeLimits.MaxDrawCells)
            color = Color.FromHex("#ff1744");

        return box;
    }

    protected override void Release(EntityUid? grid, Vector2i start, Vector2i end)
    {
        if (grid is not { } gridUid || start == end)
        {
            Click(grid, start);
            return;
        }

        var box = Corners(start, end);
        if ((long) box.Width * box.Height > WFMappingModeLimits.MaxDrawCells)
        {
            Modes.Popup("wf-mapping-screen-too-many", ("max", WFMappingModeLimits.MaxDrawCells));
            return;
        }

        switch (Modes.EraseTarget)
        {
            case WFMappingEraseTarget.Entity:
            {
                var local = new Box2(box.Left, box.Bottom, box.Right, box.Top);
                _net.ClientSendMessage(new MsgPlacement
                {
                    PlaceType = PlacementManagerMessage.RequestRectRemove,
                    NetCoordinates = _entities.GetNetCoordinates(new EntityCoordinates(gridUid, local.Center)),
                    RectSize = local.Size - new System.Numerics.Vector2(RectInset * 2f),
                    RectRotation = (float) Modes.WorldBox(gridUid, local).Rotation.Theta,
                });
                break;
            }
            case WFMappingEraseTarget.Tile:
                Modes.SendDraw(new MappingToolsDrawEvent(_entities.GetNetEntity(gridUid), MappingDrawOp.SetTiles,
                    MappingToolsShapes.Rect(start, end, false), ContentTileDefinition.SpaceID, 0, null, Angle.Zero));
                break;
            case WFMappingEraseTarget.Decal:
                Modes.SendDraw(new MappingToolsDrawEvent(_entities.GetNetEntity(gridUid), MappingDrawOp.EraseDecals,
                    MappingToolsShapes.Rect(start, end, false), null, 0, null, Angle.Zero));
                break;
        }
    }

    private void Click(EntityUid? grid, Vector2i cell)
    {
        switch (Modes.EraseTarget)
        {
            case WFMappingEraseTarget.Entity:
                var target = PressClicked;
                if (target.IsValid() && _entities.EntityExists(target) && target != _player.LocalEntity)
                {
                    _net.ClientSendMessage(new MsgPlacement
                    {
                        PlaceType = PlacementManagerMessage.RequestEntRemove,
                        EntityUid = _entities.GetNetEntity(target),
                    });
                }

                break;
            case WFMappingEraseTarget.Tile:
                if (grid is { } gridUid &&
                    _entities.TryGetComponent(gridUid, out Robust.Shared.Map.Components.MapGridComponent? comp) &&
                    Modes.TileTypeAt((gridUid, comp), cell) != 0)
                {
                    Modes.SendDraw(new MappingToolsDrawEvent(_entities.GetNetEntity(gridUid), MappingDrawOp.SetTiles,
                        new List<Vector2i> { cell }, ContentTileDefinition.SpaceID, 0, null, Angle.Zero));
                }

                break;
            case WFMappingEraseTarget.Decal:
                if (PressCoordinates.IsValid(_entities))
                    Modes.Send(new RequestDecalRemovalEvent(_entities.GetNetCoordinates(PressCoordinates)));

                break;
        }
    }
}
