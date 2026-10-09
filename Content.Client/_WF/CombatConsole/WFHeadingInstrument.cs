using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._WF.CombatConsole;

/// <summary>Displays the ship's real heading on an engraved compass instrument.</summary>
public sealed class WFHeadingInstrument : Control
{
    private static readonly UIBox2 DialRegion = new(0, 12, 374, 350);
    private readonly Func<double?> _heading;
    private DrawVertexUV2DColor[] _digitalVertices = Array.Empty<DrawVertexUV2DColor>();
    private readonly Font _font;
    private readonly Texture _dial = WFInstrumentTheme.Texture("heading_dial");

    public WFHeadingInstrument(Func<double?> heading)
    {
        _heading = heading;
        MinHeight = 170;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
        _font = new VectorFont(IoCManager.Resolve<IResourceCache>()
            .GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 12);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (WFInstrumentTheme.Digital)
        {
            DrawDigital(handle);
            return;
        }
        // The source sprite includes a black header strip above the metal housing.
        var scale = Math.Min(PixelWidth / DialRegion.Width, (PixelHeight - 24 * UIScale) / DialRegion.Height);
        var origin = new Vector2((PixelWidth - DialRegion.Width * scale) / 2, 0);
        handle.DrawTextureRectRegion(_dial, UIBox2.FromDimensions(origin, DialRegion.Size * scale), DialRegion, WFInstrumentTheme.Skin.Text);
        var center = origin + (new Vector2(190, 190) - DialRegion.TopLeft) * scale;
        var radius = 118 * scale;
        var heading = _heading();
        if (heading is { } degrees)
        {
            var angle = (float) MathHelper.DegreesToRadians(degrees - 90);
            var tip = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            handle.DrawLine(center, tip, WFInstrumentTheme.Accent);
            handle.DrawCircle(center, 4 * UIScale, WFInstrumentTheme.Accent);
        }
        WFInstrumentGlass.Round(handle, center, 142 * scale, UIScale);
        var text = heading is { } value ? Loc.GetString("wf-console-bearing-value", ("heading", $"{(Math.Round((value % 360 + 360) % 360, 1) % 360):000.0}")) :
            Loc.GetString("wf-console-bearing-offline");
        var width = handle.GetDimensions(_font, text, UIScale).X;
        handle.DrawString(_font, new Vector2((PixelWidth - width) / 2, PixelHeight - 4 * UIScale - _font.GetAscent(UIScale)),
            text, UIScale, WFInstrumentTheme.Accent);
    }
    private void DrawDigital(DrawingHandleScreen handle)
    {
        var skin = WFInstrumentTheme.Skin;
        WFConsoleDigital.Panel(handle, new UIBox2(Vector2.Zero, PixelSize), UIScale, skin.Glass, skin.EdgeSoft);
        var radius = MathF.Min(PixelWidth / 2 - 14 * UIScale, PixelHeight / 2 - 12 * UIScale);
        var center = new Vector2(PixelWidth / 2, PixelHeight / 2);
        WFConsoleDigital.Arc(handle, center, radius, UIScale, 0, MathF.Tau, skin.EdgeLight, ref _digitalVertices);
        for (var i = 0; i < 36; i++)
        {
            var angle = i * MathF.Tau / 36 - MathF.PI / 2;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            handle.DrawLine(center + direction * (radius - (i % 9 == 0 ? 8 : 4) * UIScale),
                center + direction * radius, i % 9 == 0 ? skin.Accent : skin.EdgeLight);
        }
        var heading = _heading();
        if (heading is { } degrees)
        {
            var angle = (float) MathHelper.DegreesToRadians(degrees - 90);
            WFConsoleDigital.Arc(handle, center, radius - 12 * UIScale, 4 * UIScale, angle - 0.2f, angle + 0.2f, skin.Accent, ref _digitalVertices);
            var tip = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius - 5 * UIScale);
            WFConsoleDigital.Dot(handle, tip, 3 * UIScale, skin.Accent, ref _digitalVertices);
        }
        var text = heading is { } value
            ? Loc.GetString("wf-console-bearing-value", ("heading", $"{(Math.Round((value % 360 + 360) % 360, 1) % 360):000.0}"))
            : Loc.GetString("wf-console-bearing-offline");
        var size = handle.GetDimensions(_font, text, UIScale);
        handle.DrawString(_font, center - size / 2, text, UIScale, heading == null ? skin.TextMuted : skin.Accent);
        handle.DrawLine(center + new Vector2(-18, 17) * UIScale, center + new Vector2(18, 17) * UIScale, skin.AccentDim);
    }

}
