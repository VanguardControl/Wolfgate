using System.Numerics;
using System.Linq;
using Content.Client._WF.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>
/// One compact instrument cluster beside the hand slots, left of them where the screen allows and above them where it
/// doesn't, with the equipment tray opening above it.
/// </summary>
public sealed class WFVehicleDashboard : LayoutContainer
{
    /// <summary>The cluster's width in UI units.</summary>
    public const float Width = 356;

    private const float Padding = 10;
    private const float Gap = 12;

    public BoxContainer Cluster { get; } = WFInstrumentTheme.Column();
    public BoxContainer Details { get; } = WFInstrumentTheme.Column();
    public UIBox2 ClusterBounds { get; private set; }

    public WFVehicleDashboard()
    {
        Name = "WFVehicleHud";
        MouseFilter = MouseFilterMode.Ignore;
        AddChild(Cluster);
        AddChild(Details);
    }

    /// <summary>Sizes the cluster to its contents and keeps it clear of the hand slots.</summary>
    public void Fit(Vector2 viewport, UIBox2 hotbar)
    {
        SetSize = viewport;
        Cluster.Measure(new Vector2(Width - Padding * 2, float.PositiveInfinity));
        var height = Cluster.DesiredSize.Y + Padding * 2;
        var beside = hotbar.Left - Gap - Width >= 8;
        var topLeft = beside
            ? new Vector2(hotbar.Left - Gap - Width, viewport.Y - 8 - height)
            : new Vector2(8, MathF.Max(0, hotbar.Top - 8 - height));
        ClusterBounds = UIBox2.FromDimensions(topLeft, new Vector2(Width, height));
        Place(Cluster, ClusterBounds, Padding);

        var detailHeight = MathF.Min(240, Details.Children.Where(child => child.Visible).Select(child => child.DesiredSize.Y).DefaultIfEmpty(0).Max() + 16);
        var tray = UIBox2.FromDimensions(new Vector2(ClusterBounds.Left, MathF.Max(0, ClusterBounds.Top - detailHeight - 8)),
            new Vector2(Width, detailHeight));
        Place(Details, tray, 8);
    }

    private static void Place(Control control, UIBox2 box, float padding)
    {
        SetPosition(control, box.TopLeft + new Vector2(padding));
        control.SetSize = Vector2.Max(Vector2.Zero, box.Size - new Vector2(padding * 2));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        DrawPlate(handle, ClusterBounds);
        if (Details.Children.Any(child => child.Visible))
        {
            var box = UIBox2.FromDimensions(Details.Position * UIScale - new Vector2(6 * UIScale), Details.Size * UIScale + new Vector2(12 * UIScale));
            handle.DrawRect(box, WFInstrumentTheme.Ink.WithAlpha(0.82f));
            handle.DrawRect(box, WFInstrumentTheme.Accent.WithAlpha(0.5f), false);
        }
    }

    /// <summary>A plate with clipped corners behind the cluster.</summary>
    private void DrawPlate(DrawingHandleScreen handle, UIBox2 bounds)
    {
        const float cut = 12;
        var w = bounds.Width;
        var h = bounds.Height;
        var points = new[] { new Vector2(0, cut), new Vector2(cut, 0), new Vector2(w - cut, 0), new Vector2(w, cut),
            new Vector2(w, h - cut), new Vector2(w - cut, h), new Vector2(cut, h), new Vector2(0, h - cut) };
        for (var i = 0; i < points.Length; i++)
            points[i] = (points[i] + bounds.TopLeft) * UIScale;

        var triangles = new Vector2[(points.Length - 2) * 3];
        for (var i = 1; i < points.Length - 1; i++)
        {
            triangles[(i - 1) * 3] = points[0];
            triangles[(i - 1) * 3 + 1] = points[i];
            triangles[(i - 1) * 3 + 2] = points[i + 1];
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, triangles, WFInstrumentTheme.Ink.WithAlpha(0.72f));
        for (var i = 0; i < points.Length; i++)
            handle.DrawLine(points[i], points[(i + 1) % points.Length], WFInstrumentTheme.Accent.WithAlpha(0.5f));
    }
}
