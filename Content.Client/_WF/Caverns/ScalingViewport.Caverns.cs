using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Client._WF.Caverns;
using Content.Shared._CE.ZLevels.Core.EntitySystems;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    private WFCavernViewSystem? _wfCavernView;

    /// <summary>
    /// Opens a ground layer onto the cavern under it: adds the cavern as a pass one level down, and moves the floor of
    /// the view down to it, when <see cref="WFCavernViewSystem.CavernPassDepth"/> says it shows.
    /// </summary>
    // Lower passes draw first and the ground's empty tiles draw nothing, so the cavern shows only through its holes.
    // A mouth, not any empty tile, opens it: unloaded ground at the edge of a far view keeps the sky it always had.
    private void WfAddCavernPass(EntityUid ground, float groundDepth, float ownDepth, ref float? occludeBelowDepth)
    {
        _wfCavernView ??= _entityManager.System<WFCavernViewSystem>();

        var known = _wfCavernView.TryGetCavernBelow(ground, out var cavern);
        var mouth = known && _wfCavernView.AnyHoleWithin(ground, WfLevelView(groundDepth - ownDepth));
        if (WFCavernViewSystem.CavernPassDepth(groundDepth, known, mouth) is not { } depth)
            return;

        _zPasses.Add((cavern!.Value, depth, false, false));
        occludeBelowDepth = depth;
    }

    /// <summary>
    /// The world box a level this far below the observer shows: the observer's own view, widened by the perspective
    /// shrink and shifted by the level offset as its pass is, plus a tile.
    /// </summary>
    private Box2 WfLevelView(float below)
    {
        var drawBox = GetDrawBox();
        var bottomLeft = _eyeManager.ScreenToMap(drawBox.BottomLeft).Position;
        var bottomRight = _eyeManager.ScreenToMap(drawBox.BottomRight).Position;
        var topLeft = _eyeManager.ScreenToMap(drawBox.TopLeft).Position;
        var topRight = _eyeManager.ScreenToMap(drawBox.TopRight).Position;

        // The screen's corners on the map; a turned eye turns them, so take the box round all four.
        var min = Vector2.Min(Vector2.Min(bottomLeft, bottomRight), Vector2.Min(topLeft, topRight));
        var max = Vector2.Max(Vector2.Max(bottomLeft, bottomRight), Vector2.Max(topLeft, topRight));
        var centre = (min + max) / 2f;
        var widen = MathF.Pow(CESharedZLevelsSystem.ZLevelViewShrink, below);
        Angle rotation = (_fallbackEye?.Rotation ?? Angle.Zero) * -1;
        var shift = rotation.ToWorldVec() * CEClientZLevelsSystem.ZLevelOffset * below;

        return new Box2(centre + (min - centre) * widen + shift, centre + (max - centre) * widen + shift).Enlarged(1f);
    }
}
