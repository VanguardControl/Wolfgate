using System.Numerics;
using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private static readonly Color WfBeamColor = Color.FromHex("#c060ff");
    private static readonly Color WfBrokenBeamColor = Color.FromHex("#d03040");
    private static readonly Color WfSealedTint = Color.FromHex("#a040e0").WithAlpha(0.07f);
    private static readonly Color WfWardColor = Color.FromHex("#b080e0").WithAlpha(0.45f);

    /// <summary>Length in pixels of each dash and gap on a broken beam.</summary>
    private const float WfDash = 10f;

    private Vector2[] _wfHexFan = Array.Empty<Vector2>();

    /// <summary>
    /// Shows where the Lattice still shelters: the hex field's beams (dashed red where broken, so a gap shows), a faint tint
    /// inside it while it is sealed, and a ring round each anchor that keeps corruption off.
    /// </summary>
    private void DrawWfLatticeHex(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (map is not { } mapUid || !EntManager.TryGetComponent<WFLatticeFieldComponent>(mapUid, out var field))
            return;

        var scale = (Vector2.Transform(Vector2.UnitX, worldToView) - Vector2.Transform(Vector2.Zero, worldToView)).Length();
        for (var i = 0; i < field.Wards.Count && i < field.WardRadii.Count; i++)
        {
            handle.DrawCircle(Vector2.Transform(field.Wards[i], worldToView), field.WardRadii[i] * scale, WfWardColor, false);
        }

        var corners = field.Hex;
        if (field.Unstrung || corners.Count < 3 || field.HexUp.Count != corners.Count)
            return;

        if (WFLatticeHex.Sealed(corners, field.HexUp))
        {
            if (_wfHexFan.Length != corners.Count + 2)
                _wfHexFan = new Vector2[corners.Count + 2];

            var centre = Vector2.Zero;
            foreach (var corner in corners)
            {
                centre += corner;
            }

            _wfHexFan[0] = Vector2.Transform(centre / corners.Count, worldToView);
            for (var i = 0; i <= corners.Count; i++)
            {
                _wfHexFan[i + 1] = Vector2.Transform(corners[i % corners.Count], worldToView);
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _wfHexFan, WfSealedTint);
        }

        for (var i = 0; i < corners.Count; i++)
        {
            var a = Vector2.Transform(corners[i], worldToView);
            var b = Vector2.Transform(corners[(i + 1) % corners.Count], worldToView);
            if (field.HexUp[i])
            {
                handle.DrawLine(a, b, WfBeamColor);
                continue;
            }

            var length = (b - a).Length();
            if (length < 1f)
                continue;

            var step = (b - a) / length;
            var dash = WfDash * UIScale;
            for (var along = 0f; along < length; along += dash * 2f)
            {
                handle.DrawLine(a + step * along, a + step * MathF.Min(along + dash, length), WfBrokenBeamColor);
            }
        }
    }
}
