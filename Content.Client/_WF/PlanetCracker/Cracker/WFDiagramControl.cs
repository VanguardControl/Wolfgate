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

/// <summary>Base for the crack console diagrams: skin, mono font, colour-space helper and cached rings.</summary>
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

    /// <summary>Skin read by the last <see cref="RefreshSkin"/>.</summary>
    protected WolfgateSkin Skin = WolfgateSkins.Futurist;

    /// <summary>Mono face for every readout drawn by hand rather than by a Label.</summary>
    protected Font Font = default!;

    private string? _fontSkin;

    protected WFDiagramControl()
    {
        IoCManager.InjectDependencies(this);
    }

    /// <summary>True once IoC has filled every dependency; the headless draw smoke asserts on it.</summary>
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

    /// <summary>Linear skin colour for DrawLine, unfilled DrawCircle, DrawRect and DrawTextureRect; DrawPrimitives and DrawString take it raw.</summary>
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

    /// <summary>One DrawPrimitives call per colour bucket, with the raw skin colour.</summary>
    protected static void Flush(DrawingHandleScreen handle, ref ValueList<Vector2> bucket, Color colour)
    {
        if (bucket.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.LineList, bucket.Span, colour);
    }

    /// <summary>Rebuilds a closed line strip only when the circle moves, resizes or changes segment count.</summary>
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

    /// <summary>Draws a cached ring as one line strip, with the raw skin colour.</summary>
    protected static void DrawRing(DrawingHandleScreen handle, ref Ring ring, Color colour)
    {
        if (ring.Vertices.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, ring.Vertices.Span, colour);
    }

    /// <summary>Blink gate on real time rather than sim time.</summary>
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
