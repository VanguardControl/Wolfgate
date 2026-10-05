using System.Numerics;
using Content.Client.Shuttles.UI;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Administration.UI.AdminRadar;

/// <summary>
/// The mass scanner display for admins: it fills whatever its window gives it and zooms out far past a console's range.
/// </summary>
public sealed class AdminRadarControl : ShuttleNavControl
{
    /// <summary>
    /// Furthest zoom, measured from the centre to the edge of the longer side. Grids past it aren't looked up.
    /// </summary>
    public const float AdminMaxRange = 25600f;

    private const float AdminMinRange = 64f;
    private const float StartRange = 256f;

    /// <summary>How far out an off-screen grid still gets a label at the edge, as on a console.</summary>
    private const float EdgeIffRange = 3000f;

    /// <summary>The nav control's own range rings stop here.</summary>
    private const float LastNavRing = 4096f;

    public AdminRadarControl() : base(AdminMinRange, AdminMaxRange, StartRange)
    {
        // The nav control fixes itself to a square; here the window sets the size and the drawing follows it.
        SetSize = new Vector2(float.NaN, float.NaN);
    }

    /// <summary>Drawing centres on the control rather than on the nav control's fixed square.</summary>
    protected override Vector2 MidPointVector => new Vector2(PixelWidth, PixelHeight) / 2f;

    /// <summary>The range spans the longer side, so the nav control's square culling covers the whole view.</summary>
    protected override int ScaledMinimapRadius => Math.Max(1, (int) (MathF.Max(PixelWidth, PixelHeight) / 2f));

    /// <summary>
    /// Takes a scanner state, then lifts the console's range limit again.
    /// </summary>
    public void UpdateAdminState(NavInterfaceState state)
    {
        // The nav control clamps the zoom to the console's range.
        var range = ActualRadarRange;
        UpdateState(state);
        WorldMaxRange = AdminMaxRange;
        WorldMinRange = AdminMinRange;
        ActualRadarRange = Math.Clamp(range, WorldMinRange, WorldMaxRange);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        var offset = Offset;
        base.MouseMove(args);

        // The nav control pans as if it were still its fixed square; redo the step at this control's scale.
        if (_draggin && MinimapScale > 0f)
            Offset = offset - new Vector2(args.Relative.X, -args.Relative.Y) * UIScale / MinimapScale;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        // Label everything in view, and keep the edge pointers to what a console would show.
        MaximumIFFDistance = MathF.Max(EdgeIffRange, CornerRadarRange);

        base.Draw(handle);

        if (_coordinates != null)
            DrawFarCircles(handle);
    }

    /// <summary>
    /// Continues the nav control's range rings out to the furthest zoom.
    /// </summary>
    private void DrawFarCircles(DrawingHandleScreen handle)
    {
        var cornerDistance = MathF.Sqrt(2f) * WorldRange;
        var color = Color.ToSrgb(Color.LightGray).WithAlpha(0.05f);

        for (var radius = LastNavRing * 2f; radius <= cornerDistance; radius *= 2f)
        {
            // A string, so Fluent doesn't group the digits unlike the nav control's own rings.
            var text = Loc.GetString("wf-admin-radar-ring", ("metres", $"{radius:0}"));
            var textDimensions = handle.GetDimensions(Font, text, UIScale);

            handle.DrawCircle(MidPointVector, MinimapScale * radius, color, false);
            handle.DrawString(Font, ScalePosition(new Vector2(0f, -radius)) - new Vector2(0f, textDimensions.Y), text, UIScale, color);
        }
    }
}
