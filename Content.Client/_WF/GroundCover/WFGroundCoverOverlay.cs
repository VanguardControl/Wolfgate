using System.Numerics;
using System.Runtime.InteropServices;
using Content.Client._WF.WolfWeather;
using Content.Client.Graphics;
using Content.Shared._WF.GroundCover;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Light.Components;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.GroundCover;

/// <summary>Darkens wet open ground, spreads puddles on it and lays snow cover, over the floor and under everything else.</summary>
public sealed class WFGroundCoverOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "WFGroundCover";
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";

    /// <summary>Mask texels per tile; the linear filter between them softens edges by about a quarter tile.</summary>
    private const float TexelsPerTile = 4f;

    private const int MaxMaskSize = 512;
    private const float MaskMargin = 4f;
    private const float MaskSnap = 4f;

    /// <summary>World coordinates are wrapped by this before noise, which repeats on it.</summary>
    private const float NoisePeriod = 1024f;

    private const double TimePeriod = 3600.0;

    /// <summary>Wetness below this draws nothing.</summary>
    private const float MinWetness = 0.01f;

    private static readonly TimeSpan MaskRefresh = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaskMinAge = TimeSpan.FromSeconds(0.25);
    private static readonly Color ClearColor = new(0f, 0f, 0f, 0f);

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly WFGroundCoverSystem _cover;
    private readonly WolfWeatherSystem _wolfWeather;
    private readonly EntityLookupSystem _lookup;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _xform;
    private readonly EntityQuery<WFGroundWeatherComponent> _groundQuery;
    private readonly EntityQuery<RoofComponent> _roofQuery;
    private readonly OverlayResourceCache<MaskTarget> _masks = new();
    private readonly List<WorldRect> _rects = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private readonly ShaderInstance _shader;
    private readonly ShaderInstance _unshaded;
    private readonly Action _drawMask;

    private DrawingHandleWorld _maskHandle = default!;
    private Matrix3x2 _maskMatrix;
    private MapId _maskMapId;
    private Box2 _maskRect;
    private bool _maskWet;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public WFGroundCoverOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 10;
        _cover = _entManager.System<WFGroundCoverSystem>();
        _wolfWeather = _entManager.System<WolfWeatherSystem>();
        _lookup = _entManager.System<EntityLookupSystem>();
        _map = _entManager.System<SharedMapSystem>();
        _xform = _entManager.System<SharedTransformSystem>();
        _groundQuery = _entManager.GetEntityQuery<WFGroundWeatherComponent>();
        _roofQuery = _entManager.GetEntityQuery<RoofComponent>();
        _shader = _proto.Index(Shader).InstanceUnique();
        _unshaded = _proto.Index(UnshadedShader).Instance();
        _drawMask = DrawMask;
    }

    protected override void DisposeBehavior()
    {
        _masks.Dispose();
        _shader.Dispose();
        base.DisposeBehavior();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        var quality = _wolfWeather.Quality;
        var mapUid = _map.GetMapOrInvalid(args.MapId);
        var wetness = 0f;
        var puddles = 0f;
        if (quality > 0 && _groundQuery.TryComp(mapUid, out var ground))
        {
            wetness = ground.Wetness;
            puddles = quality >= 2 ? ground.Puddles : 0f;
        }

        var wet = wetness >= MinWetness;
        if (!wet && !AnySnowInView(args))
            return;

        var mask = GetMask(args, wet);
        var bounds = args.WorldAABB;
        var origin = new Vector2(Wrap(bounds.Left), Wrap(bounds.Bottom));

        _shader.SetParameter("mask", mask.Target!.Texture);
        _shader.SetParameter("maskOffset", bounds.BottomLeft - mask.Rect.BottomLeft);
        _shader.SetParameter("maskSize", mask.Rect.Size);
        _shader.SetParameter("size", bounds.Size);
        _shader.SetParameter("origin", origin);
        _shader.SetParameter("wetness", wetness);
        _shader.SetParameter("puddles", puddles);
        _shader.SetParameter("time", (float) (_timing.RealTime.TotalSeconds % TimePeriod));

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(bounds, Color.White);
        handle.UseShader(null);
    }

    private static float Wrap(float value)
    {
        return value - MathF.Floor(value / NoisePeriod) * NoisePeriod;
    }

    private bool AnySnowInView(in OverlayDrawArgs args)
    {
        _grids.Clear();
        _map.FindGridsIntersecting(args.MapId, args.WorldAABB, ref _grids);
        foreach (var grid in _grids)
        {
            if (_cover.HasSnow(grid.Owner))
                return true;
        }

        return false;
    }

    /// <summary>The viewport's mask of open ground and snow depth, redrawn when the view leaves it, things change or it ages.</summary>
    private MaskTarget GetMask(in OverlayDrawArgs args, bool wet)
    {
        var mask = _masks.GetForViewport(args.Viewport, static _ => new MaskTarget());
        var view = args.WorldAABB.Enlarged(MaskMargin * 0.5f);
        var real = _timing.RealTime;
        var version = _wolfWeather.MaskVersion * 31 + _cover.Version;

        if (mask.Target != null
            && mask.MapId == args.MapId
            && mask.Wet == wet
            && mask.Rect.Contains(view.BottomLeft)
            && mask.Rect.Contains(view.TopRight)
            && (mask.Version == version || real < mask.BuiltAt + MaskMinAge)
            && real < mask.BuiltAt + MaskRefresh)
            return mask;

        var outer = args.WorldAABB.Enlarged(MaskMargin);
        var rect = new Box2(
            MathF.Floor(outer.Left / MaskSnap) * MaskSnap,
            MathF.Floor(outer.Bottom / MaskSnap) * MaskSnap,
            MathF.Ceiling(outer.Right / MaskSnap) * MaskSnap,
            MathF.Ceiling(outer.Top / MaskSnap) * MaskSnap);

        var texels = MathF.Min(TexelsPerTile, MaxMaskSize / MathF.Max(rect.Width, rect.Height));
        var size = new Vector2i((int) MathF.Ceiling(rect.Width * texels), (int) MathF.Ceiling(rect.Height * texels));
        if (mask.Target == null || mask.Target.Texture.Size != size)
        {
            mask.Target?.Dispose();
            mask.Target = _clyde.CreateRenderTarget(size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                new TextureSampleParameters { Filter = true },
                "wf-ground-cover-mask");
        }

        mask.MapId = args.MapId;
        mask.Rect = rect;
        mask.Version = version;
        mask.BuiltAt = real;
        mask.Wet = wet;

        _maskHandle = args.WorldHandle;
        _maskMapId = args.MapId;
        _maskRect = rect;
        _maskWet = wet;
        _maskMatrix = Matrix3x2.CreateTranslation(-rect.Left, -rect.Top) * Matrix3x2.CreateScale(texels, -texels);
        args.WorldHandle.RenderInRenderTarget(mask.Target, _drawMask, ClearColor);
        return mask;
    }

    /// <summary>Draws the mask: red where weather reaches the ground, green for snow depth.</summary>
    private void DrawMask()
    {
        var handle = _maskHandle;
        handle.UseShader(_unshaded);
        _grids.Clear();
        _map.FindGridsIntersecting(_maskMapId, _maskRect, ref _grids);

        foreach (var grid in _grids)
        {
            var snow = _cover.HasSnow(grid.Owner);
            if (!_maskWet && !snow)
                continue;

            handle.SetTransform(Matrix3x2.Multiply(_xform.GetWorldMatrix(grid.Owner), _maskMatrix));
            _roofQuery.TryComp(grid.Owner, out var roof);
            var roofed = new Entity<MapGridComponent, RoofComponent?>(grid.Owner, grid.Comp, roof);
            _rects.Clear();

            var tiles = _map.GetTilesIntersecting(grid.Owner, grid, _maskRect);
            while (tiles.MoveNext(out var tile))
            {
                var open = 0f;
                if (_maskWet)
                {
                    var flags = _wolfWeather.Classify(roofed, tile);
                    if ((flags & WolfWeatherTile.Exposed) != 0 && (flags & WolfWeatherTile.Blocker) == 0)
                        open = 1f;
                }

                var depth = snow ? _cover.GetDepth(grid.Owner, tile.GridIndices) / (float) WFSnowCover.MaxDepth : 0f;
                if (open <= 0f && depth <= 0f)
                    continue;

                _rects.Add(new WorldRect(_lookup.GetLocalBounds(tile, grid.Comp.TileSize), new Color(open, depth, 0f)));
            }

            handle.DrawRectsUnmodulated(CollectionsMarshal.AsSpan(_rects));
        }

        handle.UseShader(null);
    }

    private sealed class MaskTarget : IDisposable
    {
        public IRenderTexture? Target;
        public MapId MapId;
        public Box2 Rect;
        public int Version;
        public TimeSpan BuiltAt;
        public bool Wet;

        public void Dispose()
        {
            Target?.Dispose();
        }
    }
}
