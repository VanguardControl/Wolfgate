using System.Numerics;
using Content.Client.Resources;
using Content.Client._WF.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.CombatConsole;

/// <summary>Displays a weapon name and exact supply above a quiet ammunition strip.</summary>
public sealed class WFWeaponRow : Control
{
    private readonly Button _button;
    private readonly Func<WFGaugeReading> _read;
    private WolfgateSkin? _skin;
    private WFGaugeReading _reading;
    private float _sampleElapsed;

    /// <summary>Returns the authoritative supply without visual smoothing.</summary>
    public WFGaugeReading Reading => _read();
    /// <summary>Readable weapon name; the button tooltip retains its full text when clipped.</summary>
    public Label NameLabel { get; }
    /// <summary>Exact ammunition, energy or replenishment state.</summary>
    public Label SupplyLabel { get; }

    public WFWeaponRow(Button button, Func<WFGaugeReading> read)
    {
        _button = button;
        _read = read;
        HorizontalExpand = VerticalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
        NameLabel = new Label
        {
            Name = "WfWeaponName", ClipText = true, VerticalAlignment = VAlignment.Top,
            SetHeight = 24, MouseFilter = MouseFilterMode.Ignore,
        };
        SupplyLabel = new Label
        {
            Name = "WfWeaponSupply", ClipText = true, VerticalAlignment = VAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0), SetHeight = 20, MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(NameLabel);
        AddChild(SupplyLabel);
        Refresh();
    }

    /// <summary>Refreshes labels without replacing the selected button or its input handlers.</summary>
    public void Refresh() => Refresh(Reading);

    /// <summary>Refreshes the labels from a supply reading the caller has already taken.</summary>
    public void Refresh(WFGaugeReading reading)
    {
        _reading = reading;
        var skin = WFInstrumentTheme.Skin;
        if (_skin != skin)
        {
            var cache = IoCManager.Resolve<IResourceCache>();
            NameLabel.FontOverride = cache.GetFont(skin.MenuFonts, 12);
            SupplyLabel.FontOverride = cache.GetFont(skin.MonoFonts, 10);
            _skin = skin;
        }
        if (NameLabel.Text != _button.Text)
            NameLabel.Text = _button.Text;
        if (SupplyLabel.Text != reading.Text)
            SupplyLabel.Text = reading.Text;
        NameLabel.FontColorOverride = _button.Disabled ? skin.TextMuted : skin.Text;
        SupplyLabel.FontColorOverride = reading.Tint == skin.Danger ? skin.Danger : skin.TextMuted;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _sampleElapsed += args.DeltaSeconds;
        if (_sampleElapsed < 0.1f && _skin == WFInstrumentTheme.Skin)
            return;
        _sampleElapsed = 0;
        Refresh();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        var bar = new UIBox2(0, Math.Max(0, PixelHeight - 3 * UIScale), PixelWidth, PixelHeight);
        handle.DrawRect(bar, skin.Ink);
        var reading = _reading;
        if (WFGaugeScale.Fraction(reading.Value, reading.Minimum, reading.Maximum) is not { } fraction)
            return;
        var color = reading.Tint ?? skin.Accent;
        handle.DrawRect(new UIBox2(bar.Left, bar.Top, bar.Left + bar.Width * fraction, bar.Bottom),
            color.WithAlpha(WFInstrumentTheme.Digital ? 0.85f : 0.65f));
    }
}

/// <summary>Separates weapon rows with a selection rail instead of nested mechanical card borders.</summary>
public sealed class WFWeaponRowStyleBox : StyleBox
{
    private readonly Button _button;

    public WFWeaponRowStyleBox(Button button)
    {
        _button = button;
        SetContentMarginOverride(Margin.Horizontal, 10);
        SetContentMarginOverride(Margin.Vertical, 2);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var skin = WFInstrumentTheme.Skin;
        var selected = _button.Pressed;
        var fill = _button.Disabled ? skin.ButtonDisabled : _button.IsHovered ? skin.ButtonHovered :
            selected ? Color.InterpolateBetween(skin.GlassRaised, skin.Accent, 0.13f) : skin.GlassRaised;
        handle.DrawRect(box, fill);
        handle.DrawLine(new Vector2(box.Left, box.Bottom - uiScale), box.BottomRight - Vector2.UnitY * uiScale,
            selected ? skin.Accent.WithAlpha(0.45f) : skin.EdgeSoft);
        if (selected)
            handle.DrawRect(new UIBox2(box.Left, box.Top + 3 * uiScale, box.Left + 3 * uiScale, box.Bottom - 3 * uiScale), skin.Accent);
    }
}
