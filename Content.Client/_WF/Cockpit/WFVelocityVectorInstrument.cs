using System.Numerics;
using Content.Client._WF.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

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
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var reading = Reading;
        var skin = WFInstrumentTheme.Skin;
        var tint = reading == null ? skin.TextMuted : skin.Accent;
        var radius = MathF.Max(1, MathF.Min(PixelWidth / 2 - 3 * UIScale, (PixelHeight - 34 * UIScale) / 2));
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
        Text(handle, _labels, _forward, center + new Vector2(0, -extent), face * 0.9f, skin.TextMuted);
        Text(handle, _labels, _aft, center + new Vector2(0, extent), face * 0.9f, skin.TextMuted);
        Text(handle, _labels, _port, center + new Vector2(-extent, 0), face * 0.35f, skin.TextMuted);
        Text(handle, _labels, _starboard, center + new Vector2(extent, 0), face * 0.35f, skin.TextMuted);
        var bow = new[] { center + new Vector2(0, -6 * UIScale), center + new Vector2(-3, 4) * UIScale,
            center + new Vector2(3, 4) * UIScale };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, bow, skin.TextMuted.WithAlpha(0.65f));
        if (reading?.ScreenDirection is { } motion)
        {
            var tip = center + motion * face * 0.57f;
            var side = new Vector2(-motion.Y, motion.X);
            var head = MathF.Min(5 * UIScale, face * 0.18f);
            handle.DrawLine(center, tip, tint);
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
            WFConsoleDigital.Dot(handle, center, 2 * UIScale, tint, ref _vertices);
        if (!WFInstrumentTheme.Digital)
            WFInstrumentGlass.Round(handle, center, face + 2 * UIScale, UIScale);
        var speed = WFGaugeReading.Number(reading?.Speed, 0, 1, "wf-gauge-unit-speed", 1).Text;
        Text(handle, _digits, speed, new Vector2(PixelWidth / 2, PixelHeight - 21 * UIScale), PixelWidth - 6 * UIScale, tint);
        Text(handle, _labels, _caption, new Vector2(PixelWidth / 2, PixelHeight - 7 * UIScale), PixelWidth - 6 * UIScale, skin.Text);
    }

    private void Text(DrawingHandleScreen handle, Font font, string text, Vector2 center, float width, Color color)
    {
        var measured = handle.GetDimensions(font, text, UIScale).X;
        var scale = UIScale * MathF.Min(1, width / MathF.Max(1, measured));
        var dimensions = handle.GetDimensions(font, text, scale);
        handle.DrawString(font, center - new Vector2(dimensions.X / 2, font.GetAscent(scale) / 2), text, scale, color);
    }
}
