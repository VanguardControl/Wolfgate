using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.CombatConsole;

/// <summary>Combines a scalable key cap with an optional mechanical switch.</summary>
public sealed class WFConsoleStyleBox : StyleBox
{
    private readonly StyleBoxTexture _face;
    private readonly Texture? _toggle;
    private readonly bool _pressed;
    private readonly bool _disabled;
    private readonly bool _danger;
    private readonly bool _hover;
    private readonly bool _accent;
    private readonly bool _compact;
    private readonly BaseButton? _disabledSwitch;
    private readonly Texture? _onToggle;

    public WFConsoleStyleBox(string face, bool toggle, bool pressed, bool disabled = false, BaseButton? disabledSwitch = null, bool compact = false)
    {
        _compact = compact;
        _pressed = pressed;
        _disabled = disabled;
        _danger = face.Contains("red");
        _hover = face.Contains("hover");
        _accent = face.Contains("amber");
        _face = WFInstrumentTheme.TextureFace(face, 12, 7, 0.55f);
        _disabledSwitch = disabledSwitch;
        _onToggle = toggle && disabledSwitch != null ? WFInstrumentTheme.Texture("switch_on") : null;
        _toggle = toggle ? WFInstrumentTheme.Texture(pressed ? "switch_on" : "switch_off") : null;
        SetContentMarginOverride(Margin.All, compact ? 3 : 7);
        if (toggle)
            SetContentMarginOverride(Margin.Left, 30);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var skin = WFInstrumentTheme.Skin;
        var pressed = _disabledSwitch?.Pressed ?? _pressed;
        var accent = _danger ? skin.Danger : skin.Accent;
        if (WFInstrumentTheme.Digital)
        {
            var fill = _disabled ? skin.ButtonDisabled : pressed ? skin.ButtonPressed :
                _hover ? skin.ButtonHovered : skin.ButtonDefault;
            var edge = _disabled ? skin.EdgeSoft : pressed || _danger ? accent : skin.EdgeLight;
            WFConsoleDigital.Panel(handle, box, uiScale, fill, edge);
            if (pressed && !_compact)
                handle.DrawRect(new UIBox2(box.Left + 4 * uiScale, box.Top + 7 * uiScale,
                    box.Left + 6 * uiScale, box.Bottom - 7 * uiScale), accent);
            if (_toggle != null)
            {
                var center = new Vector2(box.Left + 17 * uiScale, box.Top + box.Height / 2);
                handle.DrawCircle(center, 5 * uiScale, pressed ? accent : skin.EdgeLight, false);
                if (pressed)
                    handle.DrawCircle(center, 2 * uiScale, accent);
            }
            return;
        }
        var tint = _disabled ? skin.TextDisabled : _danger ? skin.Danger : _accent ? skin.Accent : skin.Text;
        _face.Modulate = tint;
        _face.Draw(handle, box, uiScale);
        if (_toggle == null)
            return;
        var toggle = _disabledSwitch?.Pressed == true ? _onToggle ?? _toggle : _toggle;
        var size = toggle.Size * (0.48f * uiScale);
        var origin = new Vector2(box.Left + 9 * uiScale, box.Top + (box.Height - size.Y) / 2);
        handle.DrawTextureRect(toggle, UIBox2.FromDimensions(origin, size), tint);
    }
}

/// <summary>Preserves the rotary grip's proportions while a slider moves it along its track.</summary>
public sealed class WFKnobStyleBox : StyleBox
{
    private readonly Texture _texture = WFInstrumentTheme.Texture("knob");

    public WFKnobStyleBox()
    {
        SetContentMarginOverride(Margin.All, 14);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var side = Math.Min(box.Width, box.Height);
        var origin = box.TopLeft + (box.Size - new Vector2(side)) / 2;
        if (WFInstrumentTheme.Digital)
        {
            var center = origin + new Vector2(side / 2);
            var skin = WFInstrumentTheme.Skin;
            handle.DrawRect(UIBox2.FromDimensions(center - new Vector2(3, side / (2 * uiScale)) * uiScale,
                new Vector2(6 * uiScale, side)), skin.Accent.WithAlpha(0.15f));
            handle.DrawLine(center - new Vector2(0, side / 2), center + new Vector2(0, side / 2), skin.Accent);
            handle.DrawCircle(center, 3 * uiScale, skin.Accent);
            return;
        }
        handle.DrawTextureRect(_texture, UIBox2.FromDimensions(origin, new Vector2(side)), WFInstrumentTheme.Skin.Text);
    }
}
