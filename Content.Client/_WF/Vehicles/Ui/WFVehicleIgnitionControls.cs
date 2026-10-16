using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>A cockpit annunciator lens that is also a starter switch or a passive warning light.</summary>
public sealed class WFVehicleLamp : ContainerButton
{
    public static bool FuelWarningLit(float percent, bool powered, double seconds)
    {
        return powered && percent < 20 && (percent >= 5 || seconds % 1 < 0.5);
    }

    public bool Lit;
    public Color LensColor = Color.Green;
    public string Legend = string.Empty;

    public WFVehicleLamp()
    {
        MinWidth = 94;
        SetHeight = 48;
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var box = WFConsoleAnnunciator.Face(handle, PixelSizeBox, UIScale, LensColor, Lit);
        var font = IoCManager.Resolve<IResourceCache>().GetFont(WFInstrumentTheme.Skin.MenuFonts, 12);
        var color = Lit ? WFInstrumentTheme.Digital ? LensColor : WFInstrumentTheme.Ink : WFInstrumentTheme.Muted;
        WFInstrumentText.DrawFitted(handle, font, Legend, box, UIScale, color);
    }
}

/// <summary>Draws the inserted ignition key, turned clockwise when the accessory circuit is on.</summary>
public sealed class WFVehicleIgnitionSlot : ContainerButton
{
    public bool Inserted;
    public bool On;

    public WFVehicleIgnitionSlot()
    {
        MinWidth = 110;
        SetHeight = 48;
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var center = new Vector2(PixelWidth * 0.35f, PixelHeight * 0.5f);
        var skin = WFInstrumentTheme.Skin;
        handle.DrawCircle(center, 18 * UIScale, skin.EdgeLight);
        handle.DrawCircle(center, 15 * UIScale, skin.Ink);
        var direction = On ? Vector2.UnitX : -Vector2.UnitY;
        var side = new Vector2(-direction.Y, direction.X);
        var color = Inserted ? skin.Caution : skin.TextMuted;
        if (Inserted)
        {
            var head = center + direction * 17 * UIScale;
            handle.DrawCircle(head, 7 * UIScale, color);
            handle.DrawCircle(head, 3 * UIScale, skin.Ink);
            handle.DrawLine(center - direction * 8 * UIScale, head, color);
            for (var i = 0; i < 3; i++)
            {
                var tooth = center - direction * i * 3 * UIScale;
                handle.DrawLine(tooth, tooth + side * 5 * UIScale, color);
            }
        }
        else
            handle.DrawLine(center - Vector2.UnitY * 8 * UIScale, center + Vector2.UnitY * 8 * UIScale, color);
        var font = IoCManager.Resolve<IResourceCache>().GetFont(skin.MenuFonts, 10);
        WFInstrumentText.DrawFitted(handle, font, Loc.GetString(On ? "wf-vehicle-key-on" : "wf-vehicle-key-off"),
            new UIBox2(PixelWidth * 0.6f, 0, PixelWidth, PixelHeight), UIScale, color);
    }
}
