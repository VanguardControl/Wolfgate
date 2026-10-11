#nullable enable

using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Cockpit;

/// <summary>A compact fuel warning lens beside the cockpit's remaining-fuel scale.</summary>
public sealed class WFCockpitFuelLamp : Control
{
    private readonly Func<bool?> _read;
    private readonly string _fuel = Loc.GetString("wf-cockpit-fuel");
    private readonly string _low = Loc.GetString("wf-cockpit-fuel-low");
    private string? _fontSkin;
    private Font _small = default!;
    private Font _large = default!;

    /// <summary>Unavailable fuel telemetry leaves the lamp unlit rather than claiming a low reading.</summary>
    public bool? LowFuel => _read();

    /// <summary>Retains a fixed warning-lamp footprint while the neighbouring fuel gauge expands.</summary>
    public WFCockpitFuelLamp(Func<bool?> read)
    {
        _read = read;
        Name = "CockpitFuelLow";
        SetWidth = MinWidth = 64;
        SetHeight = 44;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Pass;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        if (_fontSkin != skin.Id)
        {
            var cache = IoCManager.Resolve<IResourceCache>();
            _small = cache.GetFont(skin.MenuFonts, 10);
            _large = cache.GetFont(skin.MenuFonts, 14);
            _fontSkin = skin.Id;
        }
        var lit = LowFuel == true;
        var inner = WFConsoleAnnunciator.Face(handle, PixelSizeBox, UIScale, skin.Caution, lit);
        var color = lit ? WFInstrumentTheme.Digital ? skin.Caution : skin.Ink : skin.TextMuted;
        var legend = new UIBox2(inner.TopLeft + new Vector2(3 * UIScale),
            inner.BottomRight - new Vector2(3 * UIScale));
        var split = legend.Top + legend.Height * 0.44f;
        Text(handle, _small, _fuel, new UIBox2(legend.Left, legend.Top, legend.Right, split), color);
        Text(handle, _large, _low, new UIBox2(legend.Left, split, legend.Right, legend.Bottom), color);
    }

    private void Text(DrawingHandleScreen handle, Font font, string text, UIBox2 box, Color color)
    {
        var scale = WFInstrumentText.FitScale(handle, font, text, UIScale, box.Width, box.Height);
        if (scale == 0)
            return;
        var width = handle.GetDimensions(font, text, scale).X;
        handle.DrawString(font, box.Center - new Vector2(width / 2, font.GetHeight(scale) / 2f), text, scale, color);
    }
}
