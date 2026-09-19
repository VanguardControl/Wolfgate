// COLOUR SPACE, derived from the engine and identical to what WFCrackCircleOverlay already does.
// The skin stores sRGB hex, and the two draw paths want it in different spaces, so getting this backwards draws a
// line and its own ring a visibly different shade:
//   * DrawPrimitives(Vector2 span)        -> RAW skin colour. PadVerticesV2 applies Color.FromSrgb internally.
//   * DrawCircle(filled: true)            -> RAW skin colour. The filled branch does colorReal = Color.FromSrgb(color)
//                                            itself, because it goes through DrawPrimitives.
//   * DrawString                          -> RAW skin colour, so diagram text matches the stock Labels beside it.
//   * DrawLine, DrawCircle(filled: false),
//     DrawRect, DrawTextureRect           -> Color.FromSrgb(skinColour), i.e. Geom(). These write straight into
//                                            Vertex2D.Modulate, which is linear; the unfilled circle branch is
//                                            DrawLine per segment and converts nothing.
// Corollary: DrawCircle takes no segment count (divisions = Math.Max(16, radius * 16)), so every ring that wants a
// fixed vertex budget - the centrifuge face included - uses the cached line-strip DrawPrimitives recipe instead.

using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Client.Resources;
using Content.Shared._WF.CCVar;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._WF.PlanetCracker.Cracker;

/// <summary>
/// Shared base of the crack console's four diagrams: skin lookup, the mono face, the colour-space helper and the
/// cached ring recipe. Control's own constructor injects nothing, so the first line here is
/// IoCManager.InjectDependencies(this) and every derived control chains to it; without it every [Dependency] below is
/// null and the first Draw throws.
/// </summary>
public abstract partial class WFDiagramControl : Control
{
    [Dependency] protected IEntityManager EntityManager = default!;
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] protected IConfigurationManager Cfg = default!;
    [Dependency] protected IResourceCache Cache = default!;

    /// <summary>Point size of the mono face every diagram labels with.</summary>
    protected const int FontSize = 10;

    /// <summary>How far a cached ring's centre or radius may drift before it is rebuilt, in control pixels.</summary>
    private const float RingRebuildTolerance = 0.5f;

    /// <summary>Skin the last <see cref="RefreshSkin"/> read; re-read every Draw so a style switch lands at once.</summary>
    protected WolfgateSkin Skin = WolfgateSkins.Futurist;

    /// <summary>Mono face for every readout drawn by hand rather than by a Label.</summary>
    protected Font Font = default!;

    /// <summary>Skin id <see cref="Font"/> was built for, so the face is not re-resolved every frame.</summary>
    private string? _fontSkin;

    protected WFDiagramControl()
    {
        IoCManager.InjectDependencies(this);
    }

    /// <summary>
    /// True once IoC has filled every dependency. The fields are declared non-nullable with default!, so this reads
    /// them through ReferenceEquals rather than a null pattern the compiler would fold away. The headless draw smoke
    /// asserts on it, because a missing InjectDependencies call compiles clean and only shows up as a Draw-time throw.
    /// </summary>
    public bool DependenciesInjected =>
        !ReferenceEquals(EntityManager, null) &&
        !ReferenceEquals(Timing, null) &&
        !ReferenceEquals(Cfg, null) &&
        !ReferenceEquals(Cache, null);

    /// <summary>Runs the control's own draw path against a handle. For the headless draw smoke only.</summary>
    public void DrawSmoke(DrawingHandleScreen handle)
    {
        Draw(handle);
    }

    /// <summary>Runs the control's own frame update. For the headless draw smoke only.</summary>
    public void FrameUpdateSmoke(FrameEventArgs args)
    {
        FrameUpdate(args);
    }

    /// <summary>Re-reads the active skin and the mono face. Called at the top of every derived Draw.</summary>
    protected void RefreshSkin()
    {
        Skin = WolfgateSkins.Get(Cfg.GetCVar(WolfgateCVars.UiStyle));

        if (_fontSkin == Skin.Id)
            return;

        Font = Cache.GetFont(Skin.MonoFonts, FontSize);
        _fontSkin = Skin.Id;
    }

    /// <summary>
    /// A skin colour for the linear-target calls: DrawLine, an unfilled DrawCircle, DrawRect and DrawTextureRect.
    /// Everything that routes through DrawPrimitives, and DrawString, take the raw colour instead.
    /// </summary>
    protected static Color Geom(Color colour)
    {
        return Color.FromSrgb(colour);
    }

    /// <summary>Adds one line segment to a bucket flushed later as a LineList.</summary>
    protected static void AddLine(ref ValueList<Vector2> bucket, Vector2 from, Vector2 to)
    {
        bucket.Add(from);
        bucket.Add(to);
    }

    /// <summary>Adds the four edges of a quad to a LineList bucket.</summary>
    protected static void AddQuad(ref ValueList<Vector2> bucket, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        AddLine(ref bucket, a, b);
        AddLine(ref bucket, b, c);
        AddLine(ref bucket, c, d);
        AddLine(ref bucket, d, a);
    }

    /// <summary>
    /// One DrawPrimitives call per colour bucket, with the RAW skin colour: the Vector2-span overload converts.
    /// </summary>
    protected static void Flush(DrawingHandleScreen handle, ref ValueList<Vector2> bucket, Color colour)
    {
        if (bucket.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.LineList, bucket.Span, colour);
    }

    /// <summary>
    /// Rebuilds a closed line strip only when the circle actually moved or resized, so a ring costs one
    /// DrawPrimitives call rather than one GL line per segment per frame.
    /// </summary>
    protected static void EnsureRing(ref Ring ring, Vector2 centre, float radius, int segments)
    {
        if (ring.Vertices.Count > 0 &&
            ring.Segments == segments &&
            (ring.Centre - centre).Length() <= RingRebuildTolerance &&
            MathF.Abs(ring.Radius - radius) <= RingRebuildTolerance)
        {
            return;
        }

        ring.Vertices.Clear();
        ring.Centre = centre;
        ring.Radius = radius;
        ring.Segments = segments;

        for (var i = 0; i <= segments; i++)
        {
            // The last vertex repeats the first so the strip closes.
            var angle = MathF.Tau * i / segments;
            ring.Vertices.Add(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
    }

    /// <summary>Draws a cached ring as one line strip, with the RAW skin colour.</summary>
    protected static void DrawRing(DrawingHandleScreen handle, ref Ring ring, Color colour)
    {
        if (ring.Vertices.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, ring.Vertices.Span, colour);
    }

    /// <summary>Blink gate on real time, so a paused readout pulls the eye without depending on sim time.</summary>
    protected bool Blink(float period)
    {
        return Timing.RealTime.TotalSeconds % period > period / 2f;
    }

    /// <summary>One cached ring: the vertices plus the pose they were built for.</summary>
    protected struct Ring
    {
        /// <summary>Centre the vertices were built around.</summary>
        public Vector2 Centre;

        /// <summary>Radius the vertices were built with.</summary>
        public float Radius;

        /// <summary>Segment count the vertices were built with.</summary>
        public int Segments;

        /// <summary>The closed line strip, first vertex repeated at the end.</summary>
        public ValueList<Vector2> Vertices;
    }
}
