using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Cracker;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>
    /// Active skin lookup for the berth ghost. Injected by <c>MapGridControl</c>'s ctor, which calls
    /// IoCManager.InjectDependencies on the whole instance, so a field declared in this partial is filled too.
    /// </summary>
    [Dependency] private IConfigurationManager _wfCfg = default!;

    /// <summary>Berth corners in half-extent units, wound so consecutive pairs close the rectangle.</summary>
    private static readonly Vector2[] WfBerthCorners =
    [
        new(-1f, -1f),
        new(1f, -1f),
        new(1f, 1f),
        new(-1f, 1f),
    ];

    /// <summary>Reused corner buffer, so a per-frame Draw allocates nothing.</summary>
    private readonly Vector2[] _wfBerthVerts = new Vector2[4];

    /// <summary>
    /// Dashed outline of this hull's chunk berth, so a pilot can line the hauler up without the berth marker in view.
    /// The pose comes off the GRID's own component rather than the marker entity: the grid is force-sent to anyone
    /// who sees any chunk of it, while the marker sits tiles out and routinely falls outside net.pvs_range.
    /// Nothing here draws the cut circle - the radar finds grids on its own MapID, so ground-layer geometry never
    /// reaches it.
    /// </summary>
    private void DrawWfBerth(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? gridUid)
    {
        if (gridUid is not { } grid || !EntManager.TryGetComponent<WFPlanetCrackerComponent>(grid, out var cracker))
            return;

        // Zero size means the berth never resolved on this hull; drawing a degenerate rectangle would be a dot.
        var half = (Vector2)cracker.BerthSize / 2f;

        if (half.X <= 0f || half.Y <= 0f)
            return;

        var gridToView = _transform.GetWorldMatrix(grid) * worldToView;

        // DrawDottedLine is DrawLine per dash, which writes straight into the linear Vertex2D.Modulate, so the
        // sRGB skin colour has to be converted here.
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
