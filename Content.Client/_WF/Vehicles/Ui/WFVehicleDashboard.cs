using System.Numerics;
using System.Linq;
using Content.Client._WF.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>Fixed dashboard wings surrounding the hand slots, with a recessed equipment tray.</summary>
public sealed class WFVehicleDashboard : LayoutContainer
{
    public BoxContainer LeftBank { get; } = WFInstrumentTheme.Column();
    public BoxContainer RightBank { get; } = WFInstrumentTheme.Column();
    public BoxContainer Details { get; } = WFInstrumentTheme.Column();
    public UIBox2 LeftBounds { get; private set; }
    public UIBox2 RightBounds { get; private set; }
    public const float BankHeight = 310;

    public WFVehicleDashboard()
    {
        Name = "WFVehicleHud";
        MouseFilter = MouseFilterMode.Ignore;
        AddChild(LeftBank);
        AddChild(RightBank);
        AddChild(Details);
    }

    /// <summary>Reserves the live hotbar and corner icons; narrow screens lift the wings above the slots.</summary>
    public void Fit(Vector2 viewport, UIBox2 hotbar)
    {
        SetSize = viewport;
        var available = MathF.Min(hotbar.Left - 112, viewport.X - hotbar.Right - 112);
        var width = MathF.Min(480, MathF.Max(400, available));
        width = MathF.Min(width, (viewport.X - 40) / 2);
        var bottom = available >= 400 ? viewport.Y - 8 : hotbar.Top - 8;
        var outer = available >= 400 ? 104f : 12f;
        LeftBounds = UIBox2.FromDimensions(new Vector2(outer, MathF.Max(0, bottom - BankHeight)), new Vector2(width, BankHeight));
        RightBounds = UIBox2.FromDimensions(new Vector2(viewport.X - outer - width, LeftBounds.Top), new Vector2(width, BankHeight));
        Place(LeftBank, LeftBounds, 18);
        Place(RightBank, RightBounds, 18);
        var detailHeight = MathF.Min(240, Details.Children.Where(child => child.Visible).Select(child => child.DesiredSize.Y).DefaultIfEmpty(0).Max() + 16);
        var tray = UIBox2.FromDimensions(new Vector2(RightBounds.Left, MathF.Max(0, RightBounds.Top - detailHeight - 8)), new Vector2(width, detailHeight));
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
        DrawWing(handle, LeftBounds, false);
        DrawWing(handle, RightBounds, true);
        if (Details.Children.Any(child => child.Visible))
        {
            var box = UIBox2.FromDimensions(Details.Position * UIScale - new Vector2(6 * UIScale), Details.Size * UIScale + new Vector2(12 * UIScale));
            handle.DrawRect(box, WFInstrumentTheme.Ink.WithAlpha(0.82f));
            handle.DrawRect(box, WFInstrumentTheme.Accent.WithAlpha(0.5f), false);
        }
    }

    private void DrawWing(DrawingHandleScreen handle, UIBox2 bounds, bool mirror)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        var points = new[] { new Vector2(0, 20), new Vector2(20, 0), new Vector2(width - 46, 0),
            new Vector2(width, 40), new Vector2(width, height - 76), new Vector2(width - 26, height),
            new Vector2(18, height), new Vector2(0, height - 18) };
        for (var i = 0; i < points.Length; i++)
        {
            if (mirror)
                points[i].X = width - points[i].X;
            points[i] = (points[i] + bounds.TopLeft) * UIScale;
        }
        var triangles = new Vector2[(points.Length - 2) * 3];
        for (var i = 1; i < points.Length - 1; i++)
        {
            triangles[(i - 1) * 3] = points[0];
            triangles[(i - 1) * 3 + 1] = points[i];
            triangles[(i - 1) * 3 + 2] = points[i + 1];
        }
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, triangles, WFInstrumentTheme.Ink.WithAlpha(0.42f));
        for (var i = 0; i < points.Length; i++)
            handle.DrawLine(points[i], points[(i + 1) % points.Length], WFInstrumentTheme.Accent.WithAlpha(0.5f));
        var start = (bounds.TopLeft + new Vector2(22, 8)) * UIScale;
        handle.DrawLine(start, start + new Vector2((width - 80) * UIScale, 0), WFInstrumentTheme.Skin.EdgeLight);
    }
}
