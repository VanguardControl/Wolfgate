using System.Numerics;
using Content.Client._WF.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>Shows drift direction against the fixed ship bow, alongside exact speed under glass.</summary>
public sealed class WFVelocityVectorInstrument : Control
{
    private readonly Func<WFCockpitVelocityReading?> _read;
    private readonly Font _labels;
    private readonly Font _digits;
    private readonly string _caption = Loc.GetString("wf-cockpit-velocity");
    private readonly string _forward = Loc.GetString("wf-cockpit-velocity-forward");
    private readonly string _aft = Loc.GetString("wf-cockpit-velocity-aft");
    private readonly string _port = Loc.GetString("wf-cockpit-velocity-port");
    private readonly string _starboard = Loc.GetString("wf-cockpit-velocity-starboard");
    private DrawVertexUV2DColor[] _vertices = Array.Empty<DrawVertexUV2DColor>();
    private WFCockpitVelocityReading? _reading;
    private string _speedText = string.Empty;
    private float _sampleElapsed;

    /// <summary>Exposes the live physical sample independently of drawing.</summary>
    public WFCockpitVelocityReading? Reading => _read();

    public WFVelocityVectorInstrument(Func<WFCockpitVelocityReading?> read)
    {
        _read = read;
        MinWidth = 88;
        SetHeight = 100;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
        ToolTip = Loc.GetString("wf-cockpit-velocity-help");
        var font = IoCManager.Resolve<IResourceCache>()
            .GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf");
        _labels = new VectorFont(font, 8);
        _digits = new VectorFont(font, 12);
        _reading = _read();
        Sample();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _reading = _read();
        _sampleElapsed += args.DeltaSeconds;
        if (_sampleElapsed < 0.1f)
            return;
        _sampleElapsed %= 0.1f;
        Sample();
    }

    private void Sample()
    {
        _speedText = WFGaugeReading.Number(_reading?.Speed, 0, 1, "wf-gauge-unit-speed", 1).Text;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var reading = _reading;
        var skin = WFInstrumentTheme.Skin;
        var tint = reading == null ? skin.TextMuted : skin.Accent;
        var radius = MathF.Max(1, MathF.Min(PixelWidth / 2 - 3 * UIScale, (PixelHeight - 20 * UIScale) / 2));
        var center = new Vector2(PixelWidth / 2, radius + 2 * UIScale);
        var face = MathF.Max(1, radius - 7 * UIScale);
        if (WFInstrumentTheme.Digital)
        {
            WFConsoleDigital.Panel(handle, new UIBox2(Vector2.Zero, PixelSize), UIScale, skin.Glass, skin.EdgeSoft);
            WFConsoleDigital.Arc(handle, center, radius, UIScale, 0, MathF.Tau, skin.EdgeLight, ref _vertices);
            WFConsoleDigital.Arc(handle, center, face, UIScale, 0, MathF.Tau, skin.AccentDim, ref _vertices);
        }
        else
        {
            handle.DrawCircle(center + new Vector2(0, 2 * UIScale), radius, Color.Black.WithAlpha(0.8f));
            handle.DrawCircle(center, radius, skin.EdgeLight);
            handle.DrawCircle(center, radius - 2 * UIScale, skin.EdgeSoft);
            handle.DrawCircle(center, radius - 4 * UIScale, skin.TextMuted);
            handle.DrawCircle(center, radius - 5 * UIScale, skin.Ink);
            handle.DrawCircle(center, face, skin.Glass);
        }
        for (var tick = 0; tick < 24; tick++)
        {
            var angle = tick * MathF.Tau / 24;
            var direction = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
            handle.DrawLine(center + direction * (face - (tick % 6 == 0 ? 5 : 2) * UIScale),
                center + direction * face, tick % 6 == 0 ? skin.Text : skin.EdgeLight);
        }
        var extent = face * 0.72f;
        Text(handle, _labels, _forward, center + new Vector2(0, -face * 0.86f), face * 0.9f, skin.TextMuted);
        Text(handle, _labels, _aft, center + new Vector2(0, extent), face * 0.9f, skin.TextMuted);
        Text(handle, _labels, _port, center + new Vector2(-extent, 0), face * 0.35f, skin.TextMuted);
        Text(handle, _labels, _starboard, center + new Vector2(extent, 0), face * 0.35f, skin.TextMuted);
        // Keep full aft travel above the speed inset while giving slow drift more visible space.
        var origin = center - new Vector2(0, face * 0.26f);
        var bow = new[] { origin + new Vector2(0, -6 * UIScale), origin + new Vector2(-3, 4) * UIScale,
            origin + new Vector2(3, 4) * UIScale };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, bow, skin.TextMuted.WithAlpha(0.65f));
        if (reading?.ScreenDirection is { } motion)
        {
            var length = face * 0.44f * reading.Value.SpeedFraction;
            var tip = origin + motion * length;
            var side = new Vector2(-motion.Y, motion.X);
            var head = MathF.Min(5 * UIScale, length * 0.45f);
            handle.DrawLine(origin, tip, tint);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
                new[] { tip, tip - motion * head + side * head * 0.55f, tip - motion * head - side * head * 0.55f }, tint);
            if (WFInstrumentTheme.Digital)
            {
                var angle = MathF.Atan2(motion.Y, motion.X);
                WFConsoleDigital.Arc(handle, center, radius - 3 * UIScale, 2 * UIScale,
                    angle - 0.12f, angle + 0.12f, tint, ref _vertices);
            }
        }
        else if (reading != null)
            WFConsoleDigital.Dot(handle, origin, 2 * UIScale, tint, ref _vertices);
        var readout = UIBox2.FromDimensions(center + new Vector2(-face * 0.725f, face * 0.21f),
            new Vector2(face * 1.45f, MathF.Min(18 * UIScale, face * 0.32f)));
        handle.DrawRect(readout, skin.Ink);
        TextInBox(handle, _digits, _speedText, readout, tint);
        if (!WFInstrumentTheme.Digital)
            WFInstrumentGlass.Round(handle, center, face + 2 * UIScale, UIScale);
        var captionScale = WFInstrumentText.FitScale(handle, _labels, _caption, UIScale, PixelWidth - 16 * UIScale, 18 * UIScale);
        if (captionScale > 0)
        {
            var width = handle.GetDimensions(_labels, _caption, captionScale).X;
            handle.DrawString(_labels, new Vector2((PixelWidth - width) / 2,
                PixelHeight - 6 * UIScale - _labels.GetAscent(captionScale)), _caption, captionScale, skin.Text);
        }
    }

    /// <summary>Fits the speed's full text inside the lower glass inset.</summary>
    private void TextInBox(DrawingHandleScreen handle, Font font, string text, UIBox2 bounds, Color color)
    {
        var scale = WFInstrumentText.FitScale(handle, font, text, UIScale, bounds.Width, bounds.Height);
        if (scale == 0)
            return;
        var drawn = handle.GetDimensions(font, text, scale).X;
        handle.DrawString(font, bounds.Center - new Vector2(drawn / 2, font.GetHeight(scale) / 2f), text, scale, color);
    }

    private void Text(DrawingHandleScreen handle, Font font, string text, Vector2 center, float width, Color color)
    {
        var scale = WFInstrumentText.FitScale(handle, font, text, UIScale, width);
        if (scale == 0)
            return;
        var dimensions = handle.GetDimensions(font, text, scale);
        handle.DrawString(font, center - new Vector2(dimensions.X / 2, font.GetAscent(scale) / 2), text, scale, color);
    }
}
