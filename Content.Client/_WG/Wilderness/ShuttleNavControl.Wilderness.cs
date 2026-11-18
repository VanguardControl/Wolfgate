using System.Numerics;
using Content.Client._WG.DroneRings;
using Content.Shared._WG.Wilderness;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Shuttles.UI;

/// <summary>Zone edges on the scope, and a skull by every ship with a Wilderness mark.</summary>
public partial class ShuttleNavControl
{
    private static readonly Color WgMarkedColor = Color.FromHex("#ff3b30");
    private static readonly SpriteSpecifier WgSkullSprite = new SpriteSpecifier.Rsi(new ResPath("/Textures/_WG/Encounters/markers.rsi"), "skull");

    private readonly List<float> _wgZoneRadii = new() { 0f };
    private readonly List<string> _wgZoneLabels = new() { string.Empty };
    private readonly List<Color> _wgZoneColors = new() { Color.White };
    private Texture? _wgSkull;

    /// <summary>Each zone's edge as a dashed ring in its colour, named where it comes nearest the middle of the scope.</summary>
    private void DrawWgZoneRings(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (!_wgSettingsCfg.GetCVar(WildernessCVars.RadarRings)
            || !EntManager.TryGetComponent<WGZoneRingsComponent>(map, out var rings)
            || rings.Rings.Count == 0)
            return;

        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        foreach (var ring in rings.Rings)
        {
            if (!prototypes.TryIndex(ring.Zone, out var zone))
                continue;

            _wgZoneRadii[0] = ring.Radius;
            _wgZoneLabels[0] = Loc.GetString(zone.RingLabel);
            _wgZoneColors[0] = zone.Color;
            WGDroneRingDrawing.Draw(handle, Font, _wgZoneRadii, Vector2.Transform(ring.Centre, worldToView), MinimapScale, PixelSize, UIScale,
                _wgZoneLabels, _wgZoneColors);
        }
    }

    /// <summary>A marked ship's name in red.</summary>
    private Color WgMarkColor(EntityUid grid, Color color)
    {
        return EntManager.HasComponent<WGSkullComponent>(grid) ? WgMarkedColor : color;
    }

    /// <summary>
    /// For a marked ship with a tag: a skull after its name in the tag, and one over its blip at
    /// <paramref name="blip"/> even while the tag layout keeps the name for the cursor.
    /// </summary>
    private void WgTagSkull(DrawingHandleScreen handle, EntityUid grid, Vector2 blip, string name)
    {
        if (!EntManager.HasComponent<WGSkullComponent>(grid))
            return;

        _wgSkull ??= EntManager.System<SpriteSystem>().Frame0(WgSkullSprite);
        var skull = _wgSkull;
        var line = handle.GetDimensions(Font, name, UIScale * 0.9f).Y;
        WgTagHeadExtra(line + 3f * UIScale,
            (h, at) => h.DrawTextureRect(skull, UIBox2.FromDimensions(at + new Vector2(3f * UIScale, 0f), new Vector2(line)), WgMarkedColor));

        var size = 12f * UIScale;
        handle.DrawTextureRect(skull, UIBox2.FromDimensions(blip + new Vector2(-size / 2f, -size - 6f * UIScale), new Vector2(size)), WgMarkedColor);
    }
}
