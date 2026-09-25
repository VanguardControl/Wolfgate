using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Cracker;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    // Filled by MapGridControl's constructor, which injects the whole instance.
    [Dependency] private IConfigurationManager _wfCfg = default!;

    /// <summary>Berth corners in half-extent units, wound so consecutive pairs close the rectangle.</summary>
    private static readonly Vector2[] WfBerthCorners =
    [
        new(-1f, -1f),
        new(1f, -1f),
        new(1f, 1f),
        new(-1f, 1f),
    ];

    private readonly Vector2[] _wfBerthVerts = new Vector2[4];

    /// <summary>Dashed outline of this hull's chunk berth, posed from the grid since the marker is often outside PVS.</summary>
    private void DrawWfBerth(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? gridUid)
    {
        if (gridUid is not { } grid || !EntManager.TryGetComponent<WFPlanetCrackerComponent>(grid, out var cracker))
            return;

        // Zero size means the berth never resolved on this hull.
        var half = (Vector2)cracker.BerthSize / 2f;

        if (half.X <= 0f || half.Y <= 0f)
            return;

        var gridToView = _transform.GetWorldMatrix(grid) * worldToView;

        // DrawDottedLine writes linear Modulate directly, so convert the sRGB skin colour.
        var colour = Color.FromSrgb(WolfgateSkins.Get(_wfCfg.GetCVar(WolfgateCVars.UiStyle)).AccentDim);

        for (var i = 0; i < WfBerthCorners.Length; i++)
        {
            var local = cracker.BerthLocalPos + cracker.BerthLocalRot.RotateVec(WfBerthCorners[i] * half);
            _wfBerthVerts[i] = Vector2.Transform(local, gridToView);
        }

        for (var i = 0; i < _wfBerthVerts.Length; i++)
        {
            handle.DrawDottedLine(_wfBerthVerts[i], _wfBerthVerts[(i + 1) % _wfBerthVerts.Length], colour);
        }
    }
}
