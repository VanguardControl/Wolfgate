using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Collections;
using Robust.Shared.Timing;

namespace Content.Client._WF.PlanetCracker.Cracker;

/// <summary>
/// The gravitic centrifuge as a face dial: a static ring, a rotor that actually turns at the spin it is reading, the
/// spin percentage, an AT FULL pip and a load-against-capacity bar.
/// It takes spin, at-full, load and capacity as plain values so the crack console (which reads them out of
/// WFCrackConsoleState) and the machine's own window (which reads the networked WFCentrifugeComponent) feed one dial
/// rather than two subtly different ones.
/// The readouts are stacked off one bottom-up cursor rather than off fractions of the box, because fractions put the
/// AT FULL line and the load line seven pixels apart at every size - less than one line of the mono face - so the two
/// strings drew through each other. <see cref="ShowReadouts"/> turns the whole text stack off for a host that shows
/// the same four numbers in real Labels and wants only the rotor face.
/// </summary>
public sealed class WFCentrifugeDial : WFDiagramControl
{
    /// <summary>Spokes on the rotor.</summary>
    private const int Spokes = 6;

    /// <summary>Vertices in the face ring; fixed, which is why it is a cached line strip and not DrawCircle.</summary>
    private const int FaceSegments = 96;

    /// <summary>Revolutions per second the rotor turns at full spin.</summary>
    private const float MaxRps = 1.6f;

    /// <summary>Fraction of the face radius the rotor hub sits at.</summary>
    private const float HubFraction = 0.12f;

    /// <summary>Fraction of the face radius the spokes reach.</summary>
    private const float SpokeFraction = 0.78f;

    /// <summary>Fraction of the free band the face radius takes, so the ring never touches its neighbours.</summary>
    private const float FaceFraction = 0.44f;

    /// <summary>Height of the load bar, in pixels.</summary>
    private const float BarHeight = 8f;

    /// <summary>Padding between stacked readouts, in pixels.</summary>
    private const float Pad = 4f;

    /// <summary>Radius of the AT FULL pip, in pixels.</summary>
    private const float PipRadius = 4f;

    /// <summary>Load fraction above which the bar reads Caution.</summary>
    private const float LoadCaution = 0.75f;

    /// <summary>Load fraction above which the bar reads Danger.</summary>
    private const float LoadDanger = 0.95f;

    /// <summary>Blink period of the AT FULL pip while the rotor is still climbing, in seconds.</summary>
    private const float PipBlinkPeriod = 0.8f;

    /// <summary>Smallest face the rotor still reads as a rotor, in pixels; below it the ring is skipped entirely.</summary>
    private const float MinFaceRadius = 10f;

    /// <summary>Intrinsic width the control asks for, readouts or not.</summary>
    private const float DesiredWidth = 150f;

    /// <summary>Intrinsic height the control asks for with the text stack drawn.</summary>
    private const float DesiredHeightWithText = 150f;

    /// <summary>Intrinsic height the control asks for as a bare rotor face.</summary>
    private const float DesiredHeightBare = 90f;

    /// <summary>Unit-space spoke endpoints, built once; each Draw rotates them by the accumulated phase.</summary>
    private static readonly Vector2[] SpokeUnits = BuildSpokes();

    /// <summary>Rotor angle in radians, accumulated in FrameUpdate so the rotor keeps turning between states.</summary>
    private float _phase;

    private Ring _face;

    /// <summary>Line buffer for the rotor, cleared and refilled each Draw.</summary>
    private ValueList<Vector2> _rotorLines;

    /// <summary>Draws the spin, AT FULL and load readouts under the face. Off for a host that labels them itself.</summary>
    public bool ShowReadouts { get; set; } = true;

    /// <summary>Rotor spin as a fraction of full, 0 to 1.</summary>
    public float Spin { get; private set; }

    /// <summary>True once the rotor counts as at full, with the design hysteresis already applied server-side.</summary>
    public bool AtFull { get; private set; }

    /// <summary>Mass the hull's pooled gravgens are carrying.</summary>
    public float Load { get; private set; }

    /// <summary>Mass the hull's pooled gravgens can carry.</summary>
    public float Capacity { get; private set; }

    /// <summary>Feeds the dial one reading. Both hosts call this with the same four values.</summary>
    public void SetReadout(float spin, bool atFull, float load, float capacity)
    {
        Spin = Math.Clamp(spin, 0f, 1f);
        AtFull = atFull;
        Load = load;
        Capacity = capacity;
    }

    /// <summary>
    /// A size the control can actually draw at. Control's own MeasureOverride returns zero for a leaf, so without this
    /// the dial is invisible in any parent that does not pin it with MinSize or stretch it.
    /// Deliberately a constant rather than a measured string: MeasureOverride runs before the first Draw, and the mono
    /// face is only resolved inside <see cref="WFDiagramControl.RefreshSkin"/> on the draw path.
    /// </summary>
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(DesiredWidth, ShowReadouts ? DesiredHeightWithText : DesiredHeightBare);
    }

    /// <inheritdoc/>
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Accumulated, not derived from the clock, so the rotor never snaps when the spin changes.
        _phase = (_phase + args.DeltaSeconds * Spin * MaxRps * MathF.Tau) % MathF.Tau;
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

        // One line of the mono face, measured once: the whole stack is laid out off it.
        var line = handle.GetDimensions(Font, "0", 1f).Y;

        // The face gets whatever is left between the spin line at the top and the AT FULL line at the bottom, so no
        // two pieces of the dial can ever be given the same pixels.
        var faceTop = Pad;
        var faceBottom = box.Height - Pad;

        if (ShowReadouts)
        {
            var spinText = Loc.GetString("wf-centrifuge-spin", ("percent", (int)MathF.Round(Spin * 100f)));
            var spinSize = handle.GetDimensions(Font, spinText, 1f);
            handle.DrawString(Font, new Vector2((box.Width - spinSize.X) / 2f, Pad), spinText, Skin.Text);

            faceTop = Pad + line + Pad;
            faceBottom = DrawReadouts(handle, box, line);
        }

        DrawFace(handle, box, faceTop, faceBottom);
    }

    /// <summary>The ring and the rotor, centred in whatever vertical band the readouts left free.</summary>
    private void DrawFace(DrawingHandleScreen handle, UIBox2i box, float top, float bottom)
    {
        var band = bottom - top;

        if (band <= 0f)
            return;

        var radius = MathF.Min(band, box.Width - Pad * 2f) * FaceFraction;

        if (radius < MinFaceRadius)
            return;

        var centre = new Vector2(box.Width / 2f, top + band / 2f);

        EnsureRing(ref _face, centre, radius, FaceSegments);
        DrawRing(handle, ref _face, AtFull ? Skin.Good : Skin.AccentDim);

        DrawRotor(handle, centre, radius);
    }

    /// <summary>
    /// The AT FULL line, the load line and the load bar, stacked upward from the bottom edge.
    /// Returns the y the face may draw down to.
    /// </summary>
    private float DrawReadouts(DrawingHandleScreen handle, UIBox2i box, float line)
    {
        var barBottom = box.Height - Pad;
        var barTop = barBottom - BarHeight;
        var loadTop = barTop - Pad - line;
        var pipTop = loadTop - Pad - line;

        DrawLoadBar(handle, box, barTop, barBottom, loadTop);
        DrawPip(handle, box, pipTop, line);

        return pipTop - Pad;
    }

    /// <summary>The hub and its spokes, rotated by the accumulated phase.</summary>
    private void DrawRotor(DrawingHandleScreen handle, Vector2 centre, float radius)
    {
        _rotorLines.Clear();

        var sin = MathF.Sin(_phase);
        var cos = MathF.Cos(_phase);

        foreach (var unit in SpokeUnits)
        {
            var turned = new Vector2(unit.X * cos - unit.Y * sin, unit.X * sin + unit.Y * cos);
            AddLine(ref _rotorLines, centre + turned * radius * HubFraction, centre + turned * radius * SpokeFraction);
        }

        Flush(handle, ref _rotorLines, AtFull ? Skin.Accent : Skin.AccentDim);

        // Filled DrawCircle converts internally, so the hub takes the raw skin colour.
        handle.DrawCircle(centre, radius * HubFraction, Skin.EdgeLight, true);
    }

    /// <summary>The AT FULL pip: lit and steady at full, blinking dim while the rotor climbs.</summary>
    private void DrawPip(DrawingHandleScreen handle, UIBox2i box, float top, float line)
    {
        var text = Loc.GetString("wf-centrifuge-at-full");
        var size = handle.GetDimensions(Font, text, 1f);
        var x = (box.Width - size.X - PipRadius * 3f) / 2f;

        var lit = AtFull || Blink(PipBlinkPeriod);
        var colour = AtFull ? Skin.Good : Skin.TextMuted;

        if (lit)
            handle.DrawCircle(new Vector2(x + PipRadius, top + line / 2f), PipRadius, colour, true);

        handle.DrawString(Font, new Vector2(x + PipRadius * 3f, top), text, colour);
    }

    /// <summary>Load against capacity, with the Good/Caution/Danger bands.</summary>
    private void DrawLoadBar(DrawingHandleScreen handle, UIBox2i box, float top, float bottom, float textTop)
    {
        var fraction = Capacity > 0f ? Math.Clamp(Load / Capacity, 0f, 1f) : 0f;

        var colour = fraction switch
        {
            >= LoadDanger => Skin.Danger,
            >= LoadCaution => Skin.Caution,
            _ => Skin.Good,
        };

        var track = new UIBox2(Pad, top, box.Width - Pad, bottom);

        // DrawRect writes straight into Vertex2D.Modulate, which is linear, so both rects take the converted colour.
        handle.DrawRect(track, Geom(Skin.Glass));

        if (fraction > 0f)
            handle.DrawRect(new UIBox2(track.Left, track.Top, track.Left + track.Width * fraction, track.Bottom), Geom(colour));

        handle.DrawRect(track, Geom(Skin.Edge), false);

        var text = Loc.GetString("wf-centrifuge-load",
            ("mass", (int)MathF.Round(Load)),
            ("capacity", (int)MathF.Round(Capacity)));

        var size = handle.GetDimensions(Font, text, 1f);
        handle.DrawString(Font, new Vector2((box.Width - size.X) / 2f, textTop), text, Skin.TextMuted);
    }

    /// <summary>Unit-space spoke directions, evenly spaced; the phase does the turning.</summary>
    private static Vector2[] BuildSpokes()
    {
        var spokes = new Vector2[Spokes];

        for (var i = 0; i < Spokes; i++)
        {
            var angle = MathF.Tau * i / Spokes;
            spokes[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        return spokes;
    }
}
