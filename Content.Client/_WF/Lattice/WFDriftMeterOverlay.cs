using System.Numerics;
using Content.Client.Resources;
using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// The Drift gauge: a small dial at the right edge of the screen, like an old sanity meter, that fills as Drift builds.
/// It shows only while the player is out of anchored space or still carrying Drift, and fades away once both are gone.
/// It reads the greater of their ship's Drift and what is in their own head, and burns red when a Monolith put it there.
/// In a garbling band (the Deep) it stops making sense: the needle shakes, the readings are noise, and the dial flickers.
/// </summary>
public sealed partial class WFDriftMeterOverlay : Overlay
{
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private const float Radius = 46f;
    private const int ArcSegments = 40;

    /// <summary>The dial runs from the left (no Drift) round the top to the right (full).</summary>
    private const float StartAngle = MathF.PI;
    private const float Sweep = MathF.PI;

    private static readonly Color Rim = Color.FromHex("#c9a45c");
    private static readonly Color Face = Color.FromHex("#120b10").WithAlpha(0.82f);
    private static readonly Color Calm = Color.FromHex("#6aa88a");
    private static readonly Color Uneasy = Color.FromHex("#d6a23a");
    private static readonly Color Lost = Color.FromHex("#b040e0");
    private static readonly Color Needle = Color.FromHex("#f0e0c0");
    private static readonly Color Stone = Color.FromHex("#c43434");

    private readonly Font _font;
    private readonly Font _small;
    private readonly Random _noise = new();
    private readonly Vector2[] _verts = new Vector2[ArcSegments * 2 + 2];

    /// <summary>The needle eases towards the Drift rather than jumping between its steps.</summary>
    private float _needle;

    /// <summary>The dial's opacity, so it fades in and out.</summary>
    private float _alpha;

    public WFDriftMeterOverlay()
    {
        IoCManager.InjectDependencies(this);
        var cache = IoCManager.Resolve<IResourceCache>();
        _font = cache.GetFont("/Fonts/Iceberg/Iceberg-Regular.ttf", 13);
        _small = cache.GetFont("/Fonts/Iceberg/Iceberg-Regular.ttf", 10);
        ZIndex = 10;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var hallucinations = _systems.GetEntitySystem<WFLatticeHallucinationSystem>();
        var drift = hallucinations.LocalDrift();
        var showing = drift != null && drift.Band > 0 || hallucinations.LocalLevel() > 0;
        var delta = (float) _timing.FrameTime.TotalSeconds;
        _alpha = Math.Clamp(_alpha + (showing ? 1.5f : -0.6f) * delta, 0f, 1f);
        var target = Math.Clamp(hallucinations.LocalLevel() * WFDriftComponent.LevelStep / 100f, 0f, 1f);
        _needle += (target - _needle) * Math.Min(1f, delta * 2f);

        return _alpha > 0.01f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var hallucinations = _systems.GetEntitySystem<WFLatticeHallucinationSystem>();
        var drift = hallucinations.LocalDrift();
        var garbled = drift != null && Garbled(drift);
        var mind = hallucinations.LocalMind();
        var stone = mind is { Flavour: WFDriftFlavour.Monolith } && mind.MindLevel >= (drift?.Level ?? 0) && mind.MindLevel > 0;
        var rim = stone ? Stone : Rim;
        var scale = _ui.DefaultUIScale;
        var size = args.ViewportBounds.Size;
        var centre = new Vector2(size.X - (Radius + 28f) * scale, size.Y * 0.36f);
        var radius = Radius * scale;
        var alpha = _alpha * (garbled ? 0.55f + 0.45f * (float) _noise.NextDouble() : 1f);

        // The face and rim.
        handle.DrawCircle(centre, radius + 6f * scale, Face.WithAlpha(Face.A * alpha));
        Arc(handle, centre, radius + 5f * scale, 0f, 1f, rim.WithAlpha(0.7f * alpha));

        // The bands of the scale: calm, uneasy, lost.
        Arc(handle, centre, radius - 2f * scale, 0f, 0.35f, Calm.WithAlpha(0.8f * alpha));
        Arc(handle, centre, radius - 2f * scale, 0.35f, 0.7f, Uneasy.WithAlpha(0.8f * alpha));
        Arc(handle, centre, radius - 2f * scale, 0.7f, 1f, Lost.WithAlpha(0.9f * alpha));
        for (var tick = 0; tick <= 10; tick++)
        {
            var at = Point(centre, radius - 2f * scale, tick / 10f);
            var inner = Point(centre, radius - (tick % 5 == 0 ? 11f : 7f) * scale, tick / 10f);
            handle.DrawLine(inner, at, Rim.WithAlpha(0.8f * alpha));
        }

        // The needle: steady in the bands, shaking and lost in the Deep.
        var reading = _needle;
        if (garbled)
            reading = Math.Clamp(reading + ((float) _noise.NextDouble() - 0.5f) * 0.5f, 0f, 1f);

        handle.DrawLine(centre, Point(centre, radius - 6f * scale, reading), Needle.WithAlpha(alpha));
        handle.DrawCircle(centre, 3.5f * scale, Rim.WithAlpha(alpha));

        var label = Loc.GetString("wf-drift-meter-label");
        var value = garbled
            ? WFLatticeMath.Garble("000", 0.9f, _noise.Next())
            : $"{MathF.Round(_needle * 100f):0}";
        if (garbled)
            label = WFLatticeMath.Garble(label, 0.6f, _noise.Next());

        DrawCentred(handle, _font, label, centre + new Vector2(0f, 10f * scale), rim.WithAlpha(alpha), scale);
        DrawCentred(handle, _small, value, centre + new Vector2(0f, 26f * scale), Needle.WithAlpha(0.8f * alpha), scale);
    }

    /// <summary>Whether the band the local player is in garbles, as the Deep does.</summary>
    private bool Garbled(WFDriftComponent drift)
    {
        var fields = _entities.EntityQueryEnumerator<WFLatticeFieldComponent>();
        if (!fields.MoveNext(out _, out var field) || !_proto.TryIndex(field.Profile, out var profile))
            return false;

        return drift.Band < profile.Bands.Count && profile.Bands[drift.Band].Garbled;
    }

    private void Arc(DrawingHandleScreen handle, Vector2 centre, float radius, float from, float to, Color color)
    {
        var count = 0;
        var steps = Math.Max(2, (int) (ArcSegments * (to - from)));
        for (var i = 0; i <= steps; i++)
        {
            _verts[count++] = Point(centre, radius, from + (to - from) * i / steps);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, new Span<Vector2>(_verts, 0, count), color);
    }

    /// <summary>A point on the dial at a reading from 0 to 1; screen Y runs down, so the dial bulges upward.</summary>
    private static Vector2 Point(Vector2 centre, float radius, float reading)
    {
        var angle = StartAngle + Sweep * reading;
        return centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static void DrawCentred(DrawingHandleScreen handle, Font font, string text, Vector2 at, Color color, float scale)
    {
        var dimensions = handle.GetDimensions(font, text, scale);
        handle.DrawString(font, at - new Vector2(dimensions.X / 2f, 0f), text, scale, color);
    }
}
