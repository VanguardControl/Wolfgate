using System.Numerics;
using Content.Server._WF.Caverns;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Caverns;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    [Dependency] private WFCavernEyeSystem _wfCavernEyes = default!;

    /// <summary>Whether the downward eye walk stops under a level: a ground with no hole in view or a cavern.</summary>
    // Only the one cavern under the ground opens, never what lies below it.
    private bool WfEyesStopUnder(EntityUid viewer, EntityUid viewerMap, EntityUid level, Vector2 position, float pvsScale)
    {
        if (HasComp<WFCavernLayerComponent>(level))
            return level != viewerMap;

        return HasComp<CEZGroundLayerComponent>(level)
               && !_wfCavernEyes.SeesCavern(viewer, level, position, GetZEyePvsScale(viewerMap, level, pvsScale));
    }

    /// <summary>The ground layer a viewer's downward eye walk reaches and the view scale of an eye on it, or null.</summary>
    public (EntityUid Ground, float Scale)? WfGroundInView(EntityUid viewer)
    {
        if (Transform(viewer).MapUid is not { } map)
            return null;

        var pvsScale = TryComp<EyeComponent>(viewer, out var eye) ? eye.PvsScale * GetViewerZoom(viewer, eye) : 1f;
        var level = map;

        // The same walk as UpdateViewer's.
        for (var i = 1; i <= MaxZLevelsBelowRendering; i++)
        {
            if (HasComp<CEZGroundLayerComponent>(level))
                return (level, GetZEyePvsScale(map, level, pvsScale));

            if (!TryMapOffset(map, -i, out var below))
                return null;

            level = below;
        }

        return null;
    }

    /// <summary>Rebuilds a viewer's z-level eyes on the next tick.</summary>
    public void WfQueueViewerUpdate(EntityUid viewer)
    {
        _dirtyViewers.Add(viewer);
    }
}
