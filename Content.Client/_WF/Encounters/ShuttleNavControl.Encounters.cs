using System.Numerics;
using Content.Client._WF.Encounters;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>The labels placed so far this frame, so ships of one encounter do not draw over each other.</summary>
    private readonly List<Box2> _encounterLabels = new();

    /// <summary>
    /// Marks every ship of the visible encounters in the sector: a named diamond where it is on the scope, or an arrow
    /// at the rim with its name and distance when it is beyond it, each in its own colour, with its zones around it.
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
        _encounterLabels.Clear();
        for (var i = 0; i < system.Markers.Count; i++)
        {
            var marker = system.Markers[i];
            if (marker.Map != consoleXform.MapID)
                continue;

            var world = system.GetPosition(marker);
            var point = Vector2.Transform(world, worldToView);
            var offset = point - centre;
            var color = marker.Color ?? WFEncounterColors.Category(marker.Category);
            var name = system.Labels[i];
            DrawEncounterZone(handle, point, marker.WarnRange, Color.FromHex("#ffd23f"), system.WarnLabel);
            DrawEncounterZone(handle, point, marker.AttackRange, Color.FromHex("#ff4b4b"), system.AttackLabel);
            if (offset.Length() <= rim)
            {
                handle.DrawLine(point + new Vector2(0f, -size), point + new Vector2(size, 0f), color);
                handle.DrawLine(point + new Vector2(size, 0f), point + new Vector2(0f, size), color);
                handle.DrawLine(point + new Vector2(0f, size), point + new Vector2(-size, 0f), color);
                handle.DrawLine(point + new Vector2(-size, 0f), point + new Vector2(0f, -size), color);
                // Above the ship: its own IFF label hangs below and to the right, and the admin crew tag sits between.
                var at = PlaceEncounterLabel(point + new Vector2(10f, -54f) * UIScale, handle.GetDimensions(Font, name, UIScale * 0.8f));
                handle.DrawString(Font, at, name, UIScale * 0.8f, color);
                continue;
            }

            var direction = Vector2.Normalize(offset);
            var side = new Vector2(-direction.Y, direction.X);
            var tip = centre + direction * rim;
            var back = tip - direction * size * 2f;
            handle.DrawLine(tip, back + side * size, color);
            handle.DrawLine(tip, back - side * size, color);
            handle.DrawLine(back + side * size, back - side * size, color);

            var label = Loc.GetString("wf-encounter-marker-distance", ("name", name),
                ("distance", ((world - here).Length() / 1000f).ToString("0.0")));
            var dimensions = handle.GetDimensions(Font, label, UIScale * 0.8f);
            var rimAt = PlaceEncounterLabel(back - direction * (dimensions.X / 2f + 6f * UIScale) - dimensions / 2f, dimensions);
            handle.DrawString(Font, rimAt, label, UIScale * 0.8f, color);
        }
    }

    /// <summary>Moves a label down until it clears the ones already placed, and records where it ends up.</summary>
    private Vector2 PlaceEncounterLabel(Vector2 position, Vector2 size)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var box = Box2.FromDimensions(position, size);
            var clear = true;
            foreach (var placed in _encounterLabels)
            {
                if (!placed.Intersects(box))
                    continue;

                clear = false;
                break;
            }

            if (clear)
                break;

            position.Y += size.Y + 2f * UIScale;
        }

        _encounterLabels.Add(Box2.FromDimensions(position, size));
        return position;
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
}
