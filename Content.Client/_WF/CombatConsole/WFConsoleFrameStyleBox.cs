using Robust.Client.Graphics;

namespace Content.Client._WF.CombatConsole;

/// <summary>Uses the selected Wolfgate palette for either cockpit housings or clean digital glass.</summary>
public sealed class WFConsoleFrameStyleBox : StyleBox
{
    private readonly StyleBoxTexture? _texture;
    private readonly bool _bezel;

    /// <summary>Retains the same content insets in both skins so changing theme does not move controls.</summary>
    public WFConsoleFrameStyleBox(int padding, string? texture = null)
    {
        _bezel = texture == "crt_bezel";
        if (texture != null)
            _texture = WFInstrumentTheme.TextureFace(texture, _bezel ? 32 : 16, padding, _bezel ? 0.8f : 1);
        SetContentMarginOverride(Margin.All, padding);
    }

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var skin = WFInstrumentTheme.Skin;
        if (WFInstrumentTheme.Digital)
        {
            WFConsoleDigital.Panel(handle, box, uiScale, _bezel ? skin.Ink : skin.Glass, skin.Edge);
            return;
        }
        if (_texture != null)
        {
            _texture.Modulate = _bezel ? skin.Text : Color.InterpolateBetween(skin.GlassLight, skin.Text, 0.3f);
            _texture.Draw(handle, box, uiScale);
            return;
        }
        handle.DrawRect(box, skin.Glass);
        handle.DrawRect(box, skin.EdgeLight, false);
    }
}
