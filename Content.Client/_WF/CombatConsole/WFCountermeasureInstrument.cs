using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.CombatConsole;

/// <summary>Lights the physical dispenser rack from authoritative ammunition and threat telemetry.</summary>
public sealed class WFCountermeasureInstrument : Control
{
    private readonly Texture _rack = WFInstrumentTheme.Texture("flare_rack");
    private readonly Texture _lamp = WFInstrumentTheme.Texture("warning_lamp");

    /// <summary>Connected finite ammunition, capped to the rack's thirty display lamps.</summary>
    public int Ammunition;
    /// <summary>Whether an autoloader can supply further rounds.</summary>
    public bool Unlimited;
    /// <summary>Whether any launcher is connected.</summary>
    public bool Connected;
    /// <summary>Whether a missile currently tracks this ship.</summary>
    public bool Incoming;
    /// <summary>Whether launchers are still in their burst lockout.</summary>
    public bool CoolingDown;

    public WFCountermeasureInstrument()
    {
        SetWidth = 108;
        MinHeight = 108;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (WFInstrumentTheme.Digital)
        {
            DrawDigital(handle);
            return;
        }
        var scale = Math.Min(PixelWidth / 245f, PixelHeight / 249f);
        var origin = (PixelSize - new Vector2(245, 249) * scale) / 2;
        handle.DrawTextureRect(_rack, UIBox2.FromDimensions(origin, new Vector2(245, 249) * scale), WFInstrumentTheme.Skin.Text);
        var rounds = Connected ? Unlimited ? 30 : Math.Clamp(Ammunition, 0, 30) : 0;
        var color = CoolingDown ? WFInstrumentTheme.Muted : WFInstrumentTheme.Accent;
        for (var i = 0; i < rounds; i++)
        {
            var position = origin + new Vector2(45 + i % 5 * 28.3f, 34 + i / 5 * 28) * scale;
            handle.DrawCircle(position, 6 * scale, color.WithAlpha(0.8f));
            handle.DrawCircle(position, 10 * scale, color.WithAlpha(0.14f));
        }
        var lamp = UIBox2.FromDimensions(origin + new Vector2(194, 106) * scale, new Vector2(36, 38) * scale);
        handle.DrawTextureRect(_lamp, lamp, Incoming ? WFInstrumentTheme.Red : WFInstrumentTheme.Muted);
    }
    private void DrawDigital(DrawingHandleScreen handle)
    {
        var skin = WFInstrumentTheme.Skin;
        var size = new Vector2(PixelWidth, MathF.Min(PixelHeight, 108 * UIScale));
        var origin = new Vector2(0, (PixelHeight - size.Y) / 2);
        WFConsoleDigital.Panel(handle, UIBox2.FromDimensions(origin, size), UIScale, skin.Glass, skin.Edge);
        var rounds = Connected ? Unlimited ? 30 : Math.Clamp(Ammunition, 0, 30) : 0;
        var color = CoolingDown ? skin.TextMuted : skin.Accent;
        var pitch = (size - new Vector2(24, 24) * UIScale) / new Vector2(5, 6);
        for (var i = 0; i < 30; i++)
        {
            var point = origin + new Vector2(10, 12) * UIScale + new Vector2(i % 5, i / 5) * pitch;
            handle.DrawRect(UIBox2.FromDimensions(point, pitch - new Vector2(4 * UIScale)), i < rounds ? color : skin.GlassLight);
        }
        var warning = Incoming ? skin.Danger : Connected ? skin.Good : skin.TextMuted;
        handle.DrawRect(UIBox2.FromDimensions(origin + new Vector2(size.X - 7 * UIScale, 14 * UIScale),
            new Vector2(2 * UIScale, size.Y - 28 * UIScale)), warning);
    }

}
