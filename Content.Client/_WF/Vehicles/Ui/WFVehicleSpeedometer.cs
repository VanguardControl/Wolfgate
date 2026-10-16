using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>A compact speed dial using the cockpit instrument palette.</summary>
public sealed class WFVehicleSpeedometer : Control
{
    public float Fraction;

    public WFVehicleSpeedometer()
    {
        MinSize = new Vector2(120, 92);
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var center = new Vector2(PixelSize.X * 0.5f, 48f * UIScale);
        var radius = 37f * UIScale;
        var ink = Color.FromHex("#74BFA6");
        handle.DrawCircle(center, radius, ink, false);
        for (var i = 0; i <= 10; i++)
        {
            var angle = (135f + i * 27f) * MathF.PI / 180f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            handle.DrawLine(center + direction * (radius - 5f * UIScale), center + direction * radius, ink);
        }
        var needleAngle = (135f + Math.Clamp(Fraction, 0f, 1f) * 270f) * MathF.PI / 180f;
        var needle = new Vector2(MathF.Cos(needleAngle), MathF.Sin(needleAngle));
        handle.DrawLine(center, center + needle * (radius - 8f * UIScale), Color.FromHex("#D8FFF2"));
        handle.DrawCircle(center, 3f * UIScale, ink);
    }
}
