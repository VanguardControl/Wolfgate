using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// A rock's heat on a bar: cold, the green cracking window, hot, then the red overcharge zone. A white needle shows the
/// heat now and a cyan notch where the current power setting will settle it.
/// </summary>
public sealed class WFMiningChargeMeter : Control
{
    /// <summary>Heat at the right-hand end of the bar.</summary>
    public const float Scale = 1.25f;

    public float Charge;
    public float WindowLow = 0.45f;
    public float WindowHigh = 0.65f;
    public float OverchargeLine = 1f;
    public float? Predicted;
    public bool Overcharging;

    private static readonly Color Cold = new(0.16f, 0.2f, 0.24f);
    private static readonly Color WindowColor = new(0.15f, 0.75f, 0.3f);
    private static readonly Color Hot = new(0.85f, 0.5f, 0.1f);
    private static readonly Color Danger = new(0.85f, 0.1f, 0.08f);

    public WFMiningChargeMeter()
    {
        MinSize = new Vector2(240, 30);
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = (Vector2) PixelSize;
        var top = size.Y * 0.2f;
        var bottom = size.Y * 0.8f;

        handle.DrawRect(new UIBox2(X(0f), top, X(WindowLow), bottom), Cold);
        handle.DrawRect(new UIBox2(X(WindowLow), top, X(WindowHigh), bottom), WindowColor);
        handle.DrawRect(new UIBox2(X(WindowHigh), top, X(OverchargeLine), bottom), Hot.WithAlpha(0.8f));
        handle.DrawRect(new UIBox2(X(OverchargeLine), top, X(Scale), bottom), Danger.WithAlpha(Overcharging ? 1f : 0.7f));
        handle.DrawRect(new UIBox2(0f, top, size.X, bottom), Color.White.WithAlpha(0.35f), false);

        if (Predicted is { } predicted)
        {
            var x = X(Math.Clamp(predicted, 0f, Scale));
            var notch = 6f * UIScale;
            handle.DrawLine(new Vector2(x - notch, 0f), new Vector2(x, top), Color.Cyan);
            handle.DrawLine(new Vector2(x + notch, 0f), new Vector2(x, top), Color.Cyan);
            handle.DrawLine(new Vector2(x - notch, 0f), new Vector2(x + notch, 0f), Color.Cyan);
        }

        var needle = X(Math.Clamp(Charge, 0f, Scale));
        handle.DrawRect(new UIBox2(needle - 1.5f * UIScale, 0f, needle + 1.5f * UIScale, size.Y), Color.White);

        float X(float value)
        {
            return size.X * Math.Clamp(value / Scale, 0f, 1f);
        }
    }
}
