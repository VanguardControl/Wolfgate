using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.CombatConsole;

/// <summary>A telemetry sample with explicit scale limits, exact text and its current warning tint.</summary>
public readonly record struct WFGaugeReading(double? Value, double Minimum, double Maximum, string Text, Color? Tint = null)
{
    /// <summary>Formats a measured quantity without inventing a reading when its source is unavailable.</summary>
    public static WFGaugeReading Number(double? value, double minimum, double maximum, string unit, int decimals = 0, Color? tint = null) =>
        new(value, minimum, maximum, value is { } number && double.IsFinite(number)
            ? Loc.GetString("wf-gauge-value", ("value", (Math.Round(number, decimals) == 0 ? 0d : number).ToString(decimals == 0 ? "0" : decimals == 1 ? "0.0" : "0.00")),
                ("unit", Loc.GetString(unit)))
            : Loc.GetString("wf-gauge-offline"), tint);
}

/// <summary>A damped needle, engraved scale and exact readout beneath a reflective glass lens.</summary>
public sealed class WFGlassGauge : Control
{
    private readonly Func<WFGaugeReading> _read;
    private readonly string _caption;
    private DrawVertexUV2DColor[] _digitalVertices = Array.Empty<DrawVertexUV2DColor>();
    private readonly Font _small;
    private readonly Font _digits;
    private readonly Font _digitalDigits;
    private WFGaugeReading _reading;
    private float? _needle;

    /// <summary>Uses a horizontal scale with a marked midpoint for linear telemetry.</summary>
    public bool Strip { get; }

    /// <summary>Exposes the actual reading independently of visual needle damping.</summary>
    public WFGaugeReading Reading => _read();

    /// <summary>Creates a passive instrument bound to a typed telemetry source.</summary>
    public WFGlassGauge(string caption, Func<WFGaugeReading> read, bool strip = false)
    {
        _caption = Loc.GetString(caption);
        _read = read;
        Strip = strip;
        MinWidth = 104;
        SetHeight = strip ? 64 : 142;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
        var font = IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf");
        _small = new VectorFont(font, 9);
        _digits = new VectorFont(font, 12);
        _digitalDigits = new VectorFont(font, 18);
        _reading = _read();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _reading = _read();
        var target = WFGaugeScale.Fraction(_reading.Value, _reading.Minimum, _reading.Maximum);
        _needle = target == null ? null : _needle == null ? target :
            _needle + (target - _needle) * MathF.Min(1, args.DeltaSeconds * 12);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var color = _reading.Tint ?? WFInstrumentTheme.Accent;
        if (WFGaugeScale.Fraction(_reading.Value, _reading.Minimum, _reading.Maximum) == null)
            color = WFInstrumentTheme.Muted;
        if (WFInstrumentTheme.Digital)
        {
            DrawDigital(handle, color);
            return;
        }
        if (Strip)
        {
            DrawStrip(handle, color);
            return;
        }
        var radius = MathF.Max(1, MathF.Min(PixelWidth / 2f - 3 * UIScale, (PixelHeight - 25 * UIScale) / 2f));
        var center = new Vector2(PixelWidth / 2f, radius + 2 * UIScale);
        handle.DrawCircle(center + new Vector2(0, 2 * UIScale), radius, Color.Black.WithAlpha(0.8f));
        handle.DrawCircle(center, radius, WFInstrumentTheme.Skin.EdgeLight);
        handle.DrawCircle(center, radius - 2 * UIScale, WFInstrumentTheme.Skin.EdgeSoft);
        handle.DrawCircle(center, radius - 4 * UIScale, WFInstrumentTheme.Skin.TextMuted);
        handle.DrawCircle(center, radius - 5 * UIScale, WFInstrumentTheme.Skin.Ink);
        var face = MathF.Max(1, radius - 8 * UIScale);
        handle.DrawCircle(center, face, WFInstrumentTheme.Skin.Glass);
        for (var i = 0; i <= 30; i++)
        {
            var angle = (135 + i * 9) * MathF.PI / 180;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            handle.DrawLine(center + direction * (face - (i % 5 == 0 ? 8 : 4) * UIScale),
                center + direction * face, i % 5 == 0 ? WFInstrumentTheme.Cream : WFInstrumentTheme.Muted);
        }
        for (var i = 0; i <= 2; i++)
        {
            var fraction = i / 2f;
            var value = _reading.Minimum + fraction * (_reading.Maximum - _reading.Minimum);
            var point = i == 1
                ? center + new Vector2(0, -face * 0.65f)
                : center + new Vector2((i == 0 ? -1 : 1) * face * 0.55f, face * 0.90f);
            Text(handle, _small, value.ToString("0.#"), point + new Vector2(0, 3 * UIScale), face * 0.65f, WFInstrumentTheme.Muted);
        }
        if (_needle is { } needle)
        {
            var angle = (135 + needle * 270) * MathF.PI / 180;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var side = new Vector2(-direction.Y, direction.X);
            handle.DrawLine(center + new Vector2(2 * UIScale) - direction * face * 0.14f,
                center + new Vector2(2 * UIScale) + direction * face * 0.84f, Color.Black);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
                new[] { center - direction * face * 0.16f + side * 1.8f * UIScale,
                    center + direction * face * 0.88f, center - direction * face * 0.16f - side * 1.8f * UIScale }, color);
        }
        handle.DrawCircle(center, 4 * UIScale, WFInstrumentTheme.Skin.TextMuted);
        handle.DrawCircle(center, 2 * UIScale, WFInstrumentTheme.Skin.Edge);
        var readout = UIBox2.FromDimensions(center + new Vector2(-face * 0.73f, face * 0.17f), new Vector2(face * 1.46f, 18 * UIScale));
        handle.DrawRect(readout, WFInstrumentTheme.Skin.Ink);
        Text(handle, _digits, _reading.Text, new Vector2(center.X, readout.Bottom - 3 * UIScale), readout.Width - 4 * UIScale, color);
        WFInstrumentGlass.Round(handle, center, face + 2 * UIScale, UIScale);
        Text(handle, _small, _caption, new Vector2(center.X, PixelHeight - 4 * UIScale), PixelWidth - 4 * UIScale, WFInstrumentTheme.Cream);
    }

    /// <summary>Fits a linear needle scale beneath an exact readout in narrow instrument banks.</summary>
    private void DrawStrip(DrawingHandleScreen handle, Color color)
    {
        var box = new UIBox2(Vector2.Zero, PixelSize);
        handle.DrawRect(box, WFInstrumentTheme.Skin.Glass);
        Text(handle, _small, _caption, new Vector2(PixelWidth * 0.27f, 15 * UIScale), PixelWidth * 0.51f, WFInstrumentTheme.Cream);
        Text(handle, _digits, _reading.Text, new Vector2(PixelWidth * 0.76f, 16 * UIScale), PixelWidth * 0.45f, color);
        var left = 10 * UIScale;
        var right = PixelWidth - left;
        var top = 26 * UIScale;
        for (var i = 0; i <= 20; i++)
        {
            var x = left + (right - left) * i / 20f;
            handle.DrawLine(new Vector2(x, top), new Vector2(x, top + (i == 10 ? 13 : i % 5 == 0 ? 9 : 4) * UIScale), WFInstrumentTheme.Muted);
        }
        if (_needle is { } needle)
        {
            var x = left + (right - left) * needle;
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
                new[] { new Vector2(x, top + 2 * UIScale), new Vector2(x - 4 * UIScale, top + 14 * UIScale),
                    new Vector2(x + 4 * UIScale, top + 14 * UIScale) }, color);
        }
        Text(handle, _small, _reading.Minimum.ToString("0.#"), new Vector2(left + 12 * UIScale, PixelHeight - 4 * UIScale), 45 * UIScale, WFInstrumentTheme.Muted);
        Text(handle, _small, _reading.Maximum.ToString("0.#"), new Vector2(right - 15 * UIScale, PixelHeight - 4 * UIScale), 55 * UIScale, WFInstrumentTheme.Muted);
        Text(handle, _small, ((_reading.Minimum + _reading.Maximum) / 2).ToString("0.#"),
            new Vector2(PixelWidth / 2, PixelHeight - 4 * UIScale), 55 * UIScale, WFInstrumentTheme.Cream);
        WFInstrumentGlass.Window(handle, box, UIScale);
    }

    private void DrawDigital(DrawingHandleScreen handle, Color color)
    {
        var skin = WFInstrumentTheme.Skin;
        var bounds = new UIBox2(Vector2.Zero, PixelSize);
        WFConsoleDigital.Panel(handle, bounds, UIScale, skin.Glass, skin.EdgeSoft);
        if (Strip)
        {
            Text(handle, _small, _caption, new Vector2(PixelWidth * 0.27f, 15 * UIScale), PixelWidth * 0.51f, skin.TextMuted);
            Text(handle, _digits, _reading.Text, new Vector2(PixelWidth * 0.76f, 16 * UIScale), PixelWidth * 0.45f, color);
            var left = 10 * UIScale;
            var width = PixelWidth - 20 * UIScale;
            var zero = WFGaugeScale.Fraction(0, _reading.Minimum, _reading.Maximum) ?? 0;
            for (var i = 0; i < 40; i++)
            {
                var fraction = (i + 0.5f) / 40;
                var active = _needle is { } needle && fraction >= MathF.Min(zero, needle) && fraction <= MathF.Max(zero, needle);
                var x = left + width * i / 40;
                handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, 28 * UIScale),
                    new Vector2(MathF.Max(1, width / 40 - 2 * UIScale), 9 * UIScale)), active ? color : skin.GlassLight);
            }
            if (_needle is { } position)
            {
                var x = left + width * position;
                handle.DrawLine(new Vector2(x, 24 * UIScale), new Vector2(x, 40 * UIScale), color);
            }
            if (_reading.Minimum < 0 && _reading.Maximum > 0)
            {
                var x = left + width * zero;
                handle.DrawLine(new Vector2(x, 26 * UIScale), new Vector2(x, 39 * UIScale), skin.Text);
            }
            Text(handle, _small, _reading.Minimum.ToString("0.#"), new Vector2(left + 12 * UIScale, PixelHeight - 4 * UIScale), 45 * UIScale, skin.TextMuted);
            Text(handle, _small, _reading.Maximum.ToString("0.#"), new Vector2(PixelWidth - left - 15 * UIScale, PixelHeight - 4 * UIScale), 55 * UIScale, skin.TextMuted);
            Text(handle, _small, ((_reading.Minimum + _reading.Maximum) / 2).ToString("0.#"),
                new Vector2(PixelWidth / 2, PixelHeight - 4 * UIScale), 55 * UIScale, skin.TextMuted);
            return;
        }
        var radius = MathF.Max(1, MathF.Min(PixelWidth / 2 - 9 * UIScale, (PixelHeight - 30 * UIScale) / 2));
        var center = new Vector2(PixelWidth / 2, radius + 6 * UIScale);
        var start = MathF.PI * 0.75f;
        var sweep = MathF.PI * 1.5f;
        WFConsoleDigital.Arc(handle, center, radius, UIScale, start, start + sweep, skin.EdgeLight, ref _digitalVertices);
        WFConsoleDigital.Arc(handle, center, radius - 5 * UIScale, 4 * UIScale, start, start + sweep, skin.GlassLight, ref _digitalVertices);
        if (_needle is { } value)
        {
            var zero = WFGaugeScale.Fraction(0, _reading.Minimum, _reading.Maximum) ?? 0;
            WFConsoleDigital.Arc(handle, center, radius - 5 * UIScale, 4 * UIScale,
                start + MathF.Min(zero, value) * sweep, start + MathF.Max(zero, value) * sweep, color, ref _digitalVertices);
            var direction = new Vector2(MathF.Cos(start + value * sweep), MathF.Sin(start + value * sweep));
            WFConsoleDigital.Dot(handle, center + direction * (radius - 7 * UIScale), 3 * UIScale, color, ref _digitalVertices);
        }
        Text(handle, _digitalDigits, _reading.Text, center + new Vector2(0, 7 * UIScale), (radius - 10 * UIScale) * 2, color);
        Text(handle, _small, _reading.Minimum.ToString("0.#"), center + new Vector2(-radius * 0.57f, radius * 0.86f), radius * 0.8f, skin.TextMuted);
        Text(handle, _small, _reading.Maximum.ToString("0.#"), center + new Vector2(radius * 0.57f, radius * 0.86f), radius * 0.8f, skin.TextMuted);
        Text(handle, _small, _caption, new Vector2(center.X, PixelHeight - 5 * UIScale), PixelWidth - 10 * UIScale, skin.Text);
    }

    private void Text(DrawingHandleScreen handle, Font font, string? text, Vector2 position, float width, Color color)
    {
        text ??= Loc.GetString("wf-gauge-offline");
        var measured = handle.GetDimensions(font, text, UIScale).X;
        var scale = UIScale * MathF.Min(1, width / MathF.Max(1, measured));
        var drawn = handle.GetDimensions(font, text, scale).X;
        handle.DrawString(font, position - new Vector2(drawn / 2, font.GetAscent(scale)), text, scale, color);
    }
}
