using System.Linq;
using System.Numerics;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Cracker.BUI;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client._WF.PlanetCracker.Cracker;

/// <summary>
/// One row per gravity projector on the hull, ordered by grid-local X so the rows keep the same order between
/// updates: the projector icon, a power pip, a broken pip, the state name and a beam pip while firing.
/// Rows come from <see cref="WFCrackConsoleState"/>, never from the projector entities, which are routinely outside
/// net.pvs_range of the console on a capital hull.
/// </summary>
public sealed class WFProjectorPanel : WFDiagramControl
{
    /// <summary>Placeholder icon set the panel draws from.</summary>
    private static readonly ResPath IconsRsi = new("/Textures/_WF/PlanetCracker/Interface/icons.rsi");

    /// <summary>Side of an icon, in pixels; the RSI ships at 16x16.</summary>
    private const float IconSize = 16f;

    /// <summary>Height of one row, in pixels.</summary>
    private const float RowHeight = 20f;

    /// <summary>Padding around the rows, in pixels.</summary>
    private const float Pad = 4f;

    /// <summary>Radius of a status pip, in pixels.</summary>
    private const float PipRadius = 3.5f;

    /// <summary>Gap between the pips, in pixels.</summary>
    private const float PipGap = 10f;

    /// <summary>Blink period of the beam pip, in seconds.</summary>
    private const float BeamBlinkPeriod = 0.6f;

    private readonly List<WFProjectorRow> _rows = new();

    private Texture? _projectorIcon;
    private Texture? _beamIcon;
    private Texture? _warningIcon;

    /// <summary>Takes the rows out of a console state, ordered by grid-local X.</summary>
    public void SetState(WFCrackConsoleState? state)
    {
        _rows.Clear();

        if (state is null)
            return;

        _rows.AddRange(state.Projectors.OrderBy(row => row.GridLocalPos.X));
    }

    /// <inheritdoc/>
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        base.MeasureOverride(availableSize);
        return new Vector2(0f, Pad * 2f + RowHeight * Math.Max(1, _rows.Count));
    }

    /// <inheritdoc/>
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        RefreshSkin();

        handle.DrawRect(PixelSizeBox, Geom(Skin.Ink));

        var box = PixelSizeBox;

        if (box.Width <= 0 || box.Height <= 0)
            return;

        if (_rows.Count == 0)
        {
            var empty = Loc.GetString("wf-crack-console-blocker-projectors-short");
            handle.DrawString(Font, new Vector2(Pad, Pad), empty, Skin.TextMuted);
            return;
        }

        EnsureIcons();

        var y = Pad;

        foreach (var row in _rows)
        {
            DrawRow(handle, row, y, box.Width);
            y += RowHeight;
        }
    }

    /// <summary>One projector row.</summary>
    private void DrawRow(DrawingHandleScreen handle, WFProjectorRow row, float y, float width)
    {
        var mid = y + RowHeight / 2f;
        var colour = RowColour(row);

        if (_projectorIcon is { } icon)
        {
            // DrawTextureRect modulates straight into Vertex2D.Modulate, which is linear.
            handle.DrawTextureRect(icon, new UIBox2(Pad, mid - IconSize / 2f, Pad + IconSize, mid + IconSize / 2f),
                Geom(colour));
        }

        var x = Pad + IconSize + PipGap;

        // Filled DrawCircle converts internally, so the pips take the raw skin colour.
        handle.DrawCircle(new Vector2(x, mid), PipRadius, row.Powered ? Skin.Good : Skin.Danger, true);
        x += PipGap;

        handle.DrawCircle(new Vector2(x, mid), PipRadius, row.Broken ? Skin.Danger : Skin.EdgeLight, true);
        x += PipGap;

        if (row.Broken && _warningIcon is { } warning)
        {
            handle.DrawTextureRect(warning,
                new UIBox2(x, mid - IconSize / 2f, x + IconSize, mid + IconSize / 2f), Geom(Skin.Danger));
        }

        x += IconSize + PipGap / 2f;

        var text = Loc.GetString(StateKey(row.State));
        var size = handle.GetDimensions(Font, text, 1f);
        handle.DrawString(Font, new Vector2(x, mid - size.Y / 2f), text, colour);

        if (row.State != WFProjectorState.Firing || _beamIcon is not { } beam)
            return;

        if (!Blink(BeamBlinkPeriod))
            return;

        handle.DrawTextureRect(beam,
            new UIBox2(width - Pad - IconSize, mid - IconSize / 2f, width - Pad, mid + IconSize / 2f),
            Geom(Skin.Accent));
    }

    /// <summary>Loads the three icons once; done here rather than in the constructor so a headless host never needs them.</summary>
    private void EnsureIcons()
    {
        if (_projectorIcon is not null)
            return;

        var sprites = EntityManager.System<SpriteSystem>();

        _projectorIcon = sprites.Frame0(new SpriteSpecifier.Rsi(IconsRsi, "projector"));
        _beamIcon = sprites.Frame0(new SpriteSpecifier.Rsi(IconsRsi, "beam"));
        _warningIcon = sprites.Frame0(new SpriteSpecifier.Rsi(IconsRsi, "warning"));
    }

    /// <summary>Colour of one row, worst fault first.</summary>
    private Color RowColour(WFProjectorRow row)
    {
        if (row.Broken)
            return Skin.Danger;

        if (!row.Powered)
            return Skin.Caution;

        return row.State switch
        {
            WFProjectorState.Firing => Skin.Accent,
            WFProjectorState.Charging => Skin.Caution,
            _ => Skin.TextMuted,
        };
    }

    /// <summary>Locale key naming one projector state; the panel never shows a server-sent string.</summary>
    private static string StateKey(WFProjectorState state)
    {
        return state switch
        {
            WFProjectorState.Off => "wf-projector-state-off",
            WFProjectorState.Idle => "wf-projector-state-idle",
            WFProjectorState.Charging => "wf-projector-state-charging",
            WFProjectorState.Firing => "wf-projector-state-firing",
            WFProjectorState.Broken => "wf-projector-state-broken",
            _ => "wf-projector-state-off",
        };
    }
}
