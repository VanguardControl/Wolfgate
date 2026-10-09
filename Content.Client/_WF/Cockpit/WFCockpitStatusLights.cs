using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>A compact bank of live status lamps with full descriptions on hover.</summary>
public sealed class WFCockpitStatusLights : GridContainer
{
    /// <summary>Creates two columns of lamps bound to the existing flight telemetry.</summary>
    public WFCockpitStatusLights(params (string Caption, Func<WFCockpitStatusReading> Read)[] readings)
    {
        Name = "CockpitStatus";
        Columns = 2;
        HSeparationOverride = 4;
        VSeparationOverride = 3;
        HorizontalExpand = true;
        foreach (var (caption, read) in readings)
            AddChild(new WFCockpitStatusLamp(caption, read));
    }
}

/// <summary>A theme-aware indicator whose state comes directly from a live telemetry reader.</summary>
public sealed class WFCockpitStatusLamp : Control
{
    private readonly string _caption;
    private readonly Func<WFCockpitStatusReading> _read;
    private string? _detail;
    private string? _fontSkin;
    private Font? _font;

    /// <summary>Exposes the current source reading independently of drawing.</summary>
    public WFCockpitStatusReading Reading => _read();

    /// <summary>Creates a small indicator without copying or predicting its authoritative source.</summary>
    public WFCockpitStatusLamp(string caption, Func<WFCockpitStatusReading> read)
    {
        _caption = Loc.GetString(caption);
        _read = read;
        Name = caption;
        HorizontalExpand = true;
        SetHeight = 21;
        MinWidth = 64;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Pass;
        UpdateTooltip();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        var detail = Reading.Detail;
        if (_detail == detail)
            return;
        _detail = detail;
        ToolTip = Loc.GetString(detail);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        if (_fontSkin != skin.Id)
        {
            _font = IoCManager.Resolve<IResourceCache>().GetFont(skin.MenuFonts, 10);
            _fontSkin = skin.Id;
        }
        var reading = Reading;
        var lit = reading.State is WFCockpitLampState.Good or WFCockpitLampState.Active or WFCockpitLampState.Caution;
        var color = reading.State switch
        {
            WFCockpitLampState.Good => skin.Good,
            WFCockpitLampState.Active => skin.Accent,
            WFCockpitLampState.Caution => skin.Caution,
            _ => skin.EdgeLight,
        };
        handle.DrawRect(PixelSizeBox, skin.Ink);
        var center = new Vector2(9 * UIScale, PixelHeight / 2f);
        if (WFInstrumentTheme.Digital)
        {
            handle.DrawRect(UIBox2.FromDimensions(center - new Vector2(4 * UIScale), new Vector2(8 * UIScale)), color, false);
            if (lit)
                handle.DrawRect(UIBox2.FromDimensions(center - new Vector2(2 * UIScale), new Vector2(4 * UIScale)), color);
        }
        else
        {
            handle.DrawCircle(center, 6 * UIScale, skin.EdgeSoft);
            handle.DrawCircle(center, 4 * UIScale, lit ? color : skin.GlassRaised);
            if (lit)
                handle.DrawCircle(center - new Vector2(UIScale), UIScale, skin.Text.WithAlpha(0.7f));
        }
        var textSize = handle.GetDimensions(_font!, _caption, UIScale);
        handle.DrawString(_font!, new Vector2(20 * UIScale, (PixelHeight - textSize.Y) / 2f),
            _caption, UIScale, reading.State == WFCockpitLampState.Unavailable ? skin.TextDisabled : lit ? skin.Text : skin.TextMuted);
    }
}
