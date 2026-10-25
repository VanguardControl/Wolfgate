using System.Numerics;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// Draws a breaking rock: a hot flash and shockwave ring (additive), then tumbling rock chips flung outward and a
/// dust cloud that spreads and thins (alpha blended).
/// </summary>
public sealed class WFAsteroidCrackOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> AdditiveShader = "WFAdditiveUnshaded";
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly Color Rock = new(0.42f, 0.37f, 0.33f);
    private static readonly Color DarkRock = new(0.25f, 0.22f, 0.2f);
    private static readonly Color Dust = new(0.55f, 0.5f, 0.45f);
    private static readonly Color Flash = new(1f, 0.85f, 0.6f);
    private static readonly Color BlastFlash = new(1f, 0.5f, 0.2f);
    private const int RingSides = 48;
    private const int FanSides = 20;
    private const int MaxChips = 48;
    private const int DustPuffs = 7;

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;
    private readonly ShaderInstance _additive;
    private readonly ShaderInstance _unshaded;
    // Managed buffers avoid per-frame allocations; the client sandbox forbids stackalloc.
    private readonly DrawVertexUV2DColor[] _ring = new DrawVertexUV2DColor[(RingSides + 1) * 2];
    private readonly DrawVertexUV2DColor[] _fan = new DrawVertexUV2DColor[FanSides * 3];
    private readonly DrawVertexUV2DColor[] _chips = new DrawVertexUV2DColor[MaxChips * 6];
    private readonly List<(Vector2 Center, WFAsteroidCrackEffectComponent Effect, float T, int Seed)> _visible = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WFAsteroidCrackOverlay(IEntityManager entities, IPrototypeManager prototypes, IGameTiming timing)
    {
        _entities = entities;
        _transform = entities.System<SharedTransformSystem>();
        _timing = timing;
        _additive = prototypes.Index(AdditiveShader).Instance();
        _unshaded = prototypes.Index(UnshadedShader).Instance();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        _visible.Clear();
        var query = _entities.EntityQueryEnumerator<WFAsteroidCrackEffectComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var effect, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var t = (float) (_timing.CurTime - effect.Start).TotalSeconds / MathF.Max(effect.Duration, 0.1f);
            if (t < 0f || t >= 1f)
                continue;

            var center = _transform.GetWorldPosition(xform);
            if (!args.WorldAABB.Intersects(Box2.CenteredAround(center, new Vector2(effect.Radius * 8f))))
                continue;

            _visible.Add((center, effect, t, (int) (uid.Id * 7919 % 100000)));
        }

        if (_visible.Count == 0)
            return;

        var handle = args.WorldHandle;
        handle.UseShader(_unshaded);
        foreach (var (center, effect, t, seed) in _visible)
        {
            DrawDust(handle, center, effect, t, seed);
            DrawChips(handle, center, effect, t, seed);
        }

        handle.UseShader(_additive);
        foreach (var (center, effect, t, _) in _visible)
        {
            DrawFlash(handle, center, effect, t);
        }

        handle.UseShader(null);
    }

    /// <summary>A white-hot burst over the first sixth of the effect, then an expanding, thinning shockwave ring.</summary>
    private void DrawFlash(DrawingHandleWorld handle, Vector2 center, WFAsteroidCrackEffectComponent effect, float t)
    {
        var radius = effect.Radius;
        var color = effect.Violent ? BlastFlash : Flash;
        var flash = 1f - Math.Clamp(t / 0.16f, 0f, 1f);
        if (flash > 0f)
        {
            Glow(handle, center, radius * (1.1f + 1.4f * (1f - flash)), color.WithAlpha(0.8f * flash));
            Glow(handle, center, radius * 0.6f, Color.White.WithAlpha(flash));
        }

        var wave = Math.Clamp(t / 0.55f, 0f, 1f);
        if (wave >= 1f)
            return;

        var eased = 1f - (1f - wave) * (1f - wave);
        var outer = radius * (0.8f + (effect.Violent ? 3.2f : 2.4f) * eased);
        var inner = outer - radius * 0.35f * (1f - wave) - 0.15f;
        var ringColor = color.WithAlpha(0.55f * (1f - wave));
        var clear = ringColor.WithAlpha(0f);
        var count = 0;
        for (var i = 0; i <= RingSides; i++)
        {
            var angle = MathF.Tau * i / RingSides;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            _ring[count++] = new DrawVertexUV2DColor(center + dir * outer, clear);
            _ring[count++] = new DrawVertexUV2DColor(center + dir * MathF.Max(0f, inner), ringColor);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(_ring, 0, count));
    }

    /// <summary>Rock chips thrown outward and slowing, tumbling as they go, some flecked with the rock's ore.</summary>
    private void DrawChips(DrawingHandleWorld handle, Vector2 center, WFAsteroidCrackEffectComponent effect, float t, int seed)
    {
        var radius = effect.Radius;
        var chips = Math.Clamp((int) (14 + radius * 5f), 6, MaxChips);
        var fade = 1f - Math.Clamp((t - 0.55f) / 0.45f, 0f, 1f);
        var travel = t * (1f - 0.45f * t);
        var count = 0;
        for (var i = 0; i < chips; i++)
        {
            var a = Hash(seed + i * 3.1f);
            var b = Hash(seed + i * 7.7f + 1.3f);
            var c = Hash(seed + i * 11.9f + 2.9f);
            var angle = a * MathF.Tau;
            var speed = (1.5f + 3.5f * b) * (effect.Violent ? 1.6f : 1f) * MathF.Sqrt(radius);
            var start = radius * 0.6f * c;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var pos = center + dir * (start + speed * travel * effect.Duration);
            var size = (0.07f + 0.16f * c) * MathF.Min(1.6f, 0.6f + radius * 0.2f);
            var spin = (b - 0.5f) * 14f * t + a * 6f;
            var color = (c > 0.75f ? effect.VeinColor : b > 0.5f ? Rock : DarkRock).WithAlpha(fade);
            var sin = MathF.Sin(spin);
            var cos = MathF.Cos(spin);
            var u = new Vector2(cos, sin) * size;
            var v = new Vector2(-sin, cos) * size * 0.7f;
            var p0 = pos - u - v;
            var p1 = pos + u - v;
            var p2 = pos + u + v;
            var p3 = pos - u + v;
            _chips[count++] = new DrawVertexUV2DColor(p0, color);
            _chips[count++] = new DrawVertexUV2DColor(p1, color);
            _chips[count++] = new DrawVertexUV2DColor(p2, color);
            _chips[count++] = new DrawVertexUV2DColor(p0, color);
            _chips[count++] = new DrawVertexUV2DColor(p2, color);
            _chips[count++] = new DrawVertexUV2DColor(p3, color);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(_chips, 0, count));
    }

    /// <summary>Soft puffs of rock dust that spread from the rock and thin out over the whole effect.</summary>
    private void DrawDust(DrawingHandleWorld handle, Vector2 center, WFAsteroidCrackEffectComponent effect, float t, int seed)
    {
        var radius = effect.Radius;
        var alpha = 0.32f * (1f - t) * Math.Clamp(t / 0.08f, 0f, 1f);
        for (var i = 0; i < DustPuffs; i++)
        {
            var angle = Hash(seed + i * 5.3f + 9f) * MathF.Tau;
            var drift = radius * (0.2f + 1.3f * Hash(seed + i * 2.7f + 4f)) * MathF.Sqrt(t);
            var pos = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * drift;
            Glow(handle, pos, radius * (0.55f + 0.9f * t), Dust.WithAlpha(alpha));
        }
    }

    private void Glow(DrawingHandleWorld handle, Vector2 center, float radius, Color color)
    {
        var clear = color.WithAlpha(0f);
        var count = 0;
        for (var i = 0; i < FanSides; i++)
        {
            var a = MathF.Tau * i / FanSides;
            var b = MathF.Tau * (i + 1) / FanSides;
            _fan[count++] = new DrawVertexUV2DColor(center, color);
            _fan[count++] = new DrawVertexUV2DColor(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius, clear);
            _fan[count++] = new DrawVertexUV2DColor(center + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, clear);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(_fan, 0, count));
    }

    private static float Hash(float value)
    {
        var x = MathF.Sin(value * 12.9898f) * 43758.5453f;
        return x - MathF.Floor(x);
    }
}
