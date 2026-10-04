using System.Numerics;
using Content.Client._WF.Encounters;
using Content.Shared._WF.Encounters;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>
    /// Marks every visible encounter in the sector: a named diamond where it is on the scope, or an arrow at the rim
    /// with its name and distance when it is beyond it.
    /// </summary>
    private void DrawEncounterMarkers(DrawingHandleScreen handle, TransformComponent consoleXform, Matrix3x2 worldToView)
    {
        var system = EntManager.System<WFEncounterMarkerClientSystem>();
        if (system.Markers.Count == 0 || !Matrix3x2.Invert(worldToView, out var viewToWorld))
            return;

        var centre = MidPointVector;
        var here = Vector2.Transform(centre, viewToWorld);
        var rim = MathF.Min(PixelSize.X, PixelSize.Y) / 2f - 20f * UIScale;
        var size = 6f * UIScale;
        var elapsed = MathF.Min(system.Elapsed, 10f);
        foreach (var marker in system.Markers)
        {
            if (marker.Map != consoleXform.MapID)
                continue;

            var world = marker.Position + marker.Velocity * elapsed;
            var point = Vector2.Transform(world, worldToView);
            var offset = point - centre;
            var color = EncounterColor(marker.Category);
            DrawEncounterZone(handle, point, marker.WarnRange, Color.FromHex("#ffd23f"), system.WarnLabel);
            DrawEncounterZone(handle, point, marker.AttackRange, Color.FromHex("#ff4b4b"), system.AttackLabel);
            if (offset.Length() <= rim)
            {
                handle.DrawLine(point + new Vector2(0f, -size), point + new Vector2(size, 0f), color);
                handle.DrawLine(point + new Vector2(size, 0f), point + new Vector2(0f, size), color);
                handle.DrawLine(point + new Vector2(0f, size), point + new Vector2(-size, 0f), color);
                handle.DrawLine(point + new Vector2(-size, 0f), point + new Vector2(0f, -size), color);
                handle.DrawString(Font, point + new Vector2(size + 4f * UIScale, -size), marker.Name, UIScale * 0.8f, color);
                continue;
            }

            var direction = Vector2.Normalize(offset);
            var side = new Vector2(-direction.Y, direction.X);
            var tip = centre + direction * rim;
            var back = tip - direction * size * 2f;
            handle.DrawLine(tip, back + side * size, color);
            handle.DrawLine(tip, back - side * size, color);
            handle.DrawLine(back + side * size, back - side * size, color);

            var label = Loc.GetString("wf-encounter-marker-distance", ("name", marker.Name),
                ("distance", ((world - here).Length() / 1000f).ToString("0.0")));
            var dimensions = handle.GetDimensions(Font, label, UIScale * 0.8f);
            handle.DrawString(Font, back - direction * (dimensions.X / 2f + 6f * UIScale) - dimensions / 2f, label, UIScale * 0.8f, color);
        }
    }

    /// <summary>A ring at a zone's radius around the ship, named at its top, when any of it is on the scope.</summary>
    private void DrawEncounterZone(DrawingHandleScreen handle, Vector2 centre, float range, Color color, string label)
    {
        if (range <= 0f)
            return;

        var radius = range * MinimapScale;
        if (centre.X + radius < 0f || centre.Y + radius < 0f || centre.X - radius > PixelSize.X || centre.Y - radius > PixelSize.Y)
            return;

        handle.DrawCircle(centre, radius, color.WithAlpha(0.8f), false);
        var dimensions = handle.GetDimensions(Font, label, UIScale * 0.7f);
        handle.DrawString(Font, centre + new Vector2(-dimensions.X / 2f, -radius - dimensions.Y - 2f * UIScale), label, UIScale * 0.7f, color);
    }

    private static Color EncounterColor(WFEncounterCategory category)
    {
        return category switch
        {
            WFEncounterCategory.Patrol => Color.FromHex("#6fb6ff"),
            WFEncounterCategory.Threat => Color.FromHex("#ff5c5c"),
            WFEncounterCategory.Distress => Color.FromHex("#ffae3d"),
            _ => Color.FromHex("#7fe0c8"),
        };
    }
}
