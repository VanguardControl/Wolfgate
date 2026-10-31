using System.Numerics;
using System.Runtime.InteropServices;
using Content.Client._WF.WolfWeather;
using Content.Client.Graphics;
using Content.Client.Viewport;
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

/// <summary>
/// Draws wet open ground, puddles and snow cover over the floor and under everything else. Tiles under a vessel
/// are left out of the mask, so nothing is drawn on a ship that sits on the ground.
/// </summary>
public sealed partial class WFGroundCoverOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "WFGroundCover";
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly ProtoId<ShaderPrototype> BlurShader = "WFWolfWeatherBlur";

    /// <summary>Mask texels per tile; the linear filter between them softens edges by about a quarter tile.</summary>
    private const float TexelsPerTile = 4f;

    private const int MaxMaskSize = 512;

    /// <summary>How far snow edges are softened, in tiles: the blur that rounds tile corners into soft, organic edges.</summary>
    private const float SnowSoftTiles = 0.6f;

    /// <summary>Tiles drawn past the wet and snowy ones, for the soft edge of snow.</summary>
    private const float ContentMargin = 1f;
    private const float MaskMargin = 4f;
    private const float MaskSnap = 4f;

    /// <summary>World coordinates are wrapped by this before noise, which repeats on it.</summary>
    private const float NoisePeriod = 1024f;

    private const double TimePeriod = 3600.0;

    /// <summary>Wetness below this draws nothing.</summary>
    private const float MinWetness = 0.01f;

    /// <summary>Quality that draws puddles.</summary>
    private const int FullQuality = 2;

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
    private readonly ShaderInstance _blurH;
    private readonly ShaderInstance _blurV;
    private readonly Action _drawMask;
    private readonly Action _eraseCover;

    private DrawingHandleWorld _maskHandle = default!;
    private Matrix3x2 _maskMatrix;
    private MapId _maskMapId;
    private Box2 _maskRect;
    private bool _maskWet;
    private bool _anySnow;
    private Box2 _content;
    private bool _hasContent;

    private int _coverStamp;
    private readonly List<Entity<MapGridComponent>> _coverGrids = new();

    private float _wetness;
    private float _puddles;
    private Vector3 _tint = new(WFGroundWeatherComponent.Water.R, WFGroundWeatherComponent.Water.G, WFGroundWeatherComponent.Water.B);
    private bool _wet;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    /// <summary>How many times the ground shader drew, for tests.</summary>
    public int Draws { get; private set; }

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
        _blurH = _proto.Index(BlurShader).InstanceUnique();
        _blurV = _proto.Index(BlurShader).InstanceUnique();
        _drawMask = DrawMask;
        _eraseCover = EraseCover;
    }

    protected override void DisposeBehavior()
    {
        _masks.Dispose();
        _shader.Dispose();
        _blurH.Dispose();
        _blurV.Dispose();
        base.DisposeBehavior();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // Only the view's own level: z-levels drawn under or over it would stack a second layer of wet ground.
        if (args.MapId == MapId.Nullspace || args.Viewport.Eye is ScalingViewport.ZEye { Primary: false })
        {
            _wet = false;
            return false;
        }

        var wet = UpdateWeather(args.MapId);
        _anySnow = ScanGrids(args);
        if (!wet && !_anySnow)
            return false;

        // A fresh mask with nothing wet or snowy in view needs no pass.
        var mask = _masks.GetForViewport(args.Viewport, static _ => new MaskTarget());
        return !MaskFresh(mask, args, _wet) || mask.HasContent;
    }

    /// <summary>Reads a map's wetness, puddles and tint at the current quality; true if wet ground draws.</summary>
    public bool UpdateWeather(MapId mapId)
    {
        var quality = _wolfWeather.Quality;
        var mapUid = _map.GetMapOrInvalid(mapId);
        _wetness = 0f;
        _puddles = 0f;
        if (quality > 0 && _wolfWeather.IsSurface(mapUid) && _groundQuery.TryComp(mapUid, out var ground))
        {
            _wetness = ground.Wetness;
            _puddles = quality >= FullQuality ? ground.Puddles : 0f;
            _tint = new Vector3(ground.Tint.R, ground.Tint.G, ground.Tint.B);
        }

        _wet = _wetness >= MinWetness;
        return _wet;
    }

    /// <summary>The wetness read by the last <see cref="UpdateWeather"/>.</summary>
    public float Wetness => _wetness;

    /// <summary>The puddles read by the last <see cref="UpdateWeather"/>, none below Full quality.</summary>
    public float Puddles => _puddles;

    /// <summary>The puddle tint read by the last <see cref="UpdateWeather"/>.</summary>
    public Vector3 Tint => _tint;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mask = GetMask(args, _wet);
        var maskTexture = mask.Target!.Texture;

        // Only the part of the view that has wet ground or snow in it is drawn.
        if (!mask.HasContent || !TryGetArea(args, mask, out var area))
            return;

        var corner = area.BottomLeft;
        _shader.SetParameter("mask", maskTexture);
        _shader.SetParameter("maskOffset", corner - mask.Rect.BottomLeft);
        _shader.SetParameter("maskSize", mask.Rect.Size);
        _shader.SetParameter("size", area.Size);
        _shader.SetParameter("origin", new Vector2(Wrap(corner.X), Wrap(corner.Y)));
        _shader.SetParameter("wetness", _wetness);
        _shader.SetParameter("puddles", _puddles);
        _shader.SetParameter("tint", _tint);
        _shader.SetParameter("time", (float) (_timing.RealTime.TotalSeconds % TimePeriod));

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(area, Color.White);
        handle.UseShader(null);
        Draws++;
    }

    private static float Wrap(float value)
    {
        return value - MathF.Floor(value / NoisePeriod) * NoisePeriod;
    }

    /// <summary>
    /// Looks over the grids in view: whether any holds snow, and a stamp of where the vessel-like ones are, so the mask is
    /// redrawn when a ship lands, lifts or turns.
    /// </summary>
    private bool ScanGrids(in OverlayDrawArgs args)
    {
        var snow = false;
        var stamp = 17;
        _grids.Clear();
        _map.FindGridsIntersecting(args.MapId, args.WorldAABB, ref _grids);
        foreach (var grid in _grids)
        {
            if (_wolfWeather.IsCoverGrid(grid.Owner))
            {
                var (position, rotation) = _xform.GetWorldPositionRotation(grid.Owner);
                stamp = unchecked(stamp * 31 + HashCode.Combine(grid.Owner.Id, (int) (position.X * 8f), (int) (position.Y * 8f),
                    (int) (rotation.Theta * 64.0)));
                continue;
            }

            snow |= _cover.HasSnow(grid.Owner);
        }

        _coverStamp = stamp;
        return snow;
    }

    /// <summary>Whether the viewport's mask still holds for this view, so it needn't be redrawn.</summary>
    private bool MaskFresh(MaskTarget mask, in OverlayDrawArgs args, bool wet)
    {
        var view = args.WorldAABB.Enlarged(MaskMargin * 0.5f);
        var real = _timing.RealTime;
        var version = MaskVersion();

        return mask.Target != null
               && mask.MapId == args.MapId
               && mask.Wet == wet
               && mask.Rect.Contains(view.BottomLeft)
               && mask.Rect.Contains(view.TopRight)
               && (mask.Version == version || real < mask.BuiltAt + MaskMinAge)
               && real < mask.BuiltAt + MaskRefresh;
    }

    private int MaskVersion()
    {
        return unchecked((_wolfWeather.MaskVersion * 31 + _cover.Version) * 31 + _coverStamp);
    }

    /// <summary>The part of the view with wet ground or snow in it, which is all that draws.</summary>
    private static bool TryGetArea(in OverlayDrawArgs args, MaskTarget mask, out Box2 area)
    {
        area = args.WorldAABB.Intersect(mask.Content);
        return area.Width > 0f && area.Height > 0f;
    }

    /// <summary>The viewport's mask of open ground and snow depth, redrawn when the view leaves it, things change or it ages.</summary>
    private MaskTarget GetMask(in OverlayDrawArgs args, bool wet)
    {
        var mask = _masks.GetForViewport(args.Viewport, static _ => new MaskTarget());
        if (MaskFresh(mask, args, wet))
            return mask;

        var real = _timing.RealTime;
        var outer = args.WorldAABB.Enlarged(MaskMargin);
        var rect = new Box2(
            MathF.Floor(outer.Left / MaskSnap) * MaskSnap,
            MathF.Floor(outer.Bottom / MaskSnap) * MaskSnap,
            MathF.Ceiling(outer.Right / MaskSnap) * MaskSnap,
            MathF.Ceiling(outer.Top / MaskSnap) * MaskSnap);

        var texels = MathF.Min(TexelsPerTile, MaxMaskSize / MathF.Max(rect.Width, rect.Height));
        var size = new Vector2i((int) MathF.Ceiling(rect.Width * texels), (int) MathF.Ceiling(rect.Height * texels));
        EnsureTarget(ref mask.Target, size, "wf-ground-cover-mask");
        if (_anySnow)
        {
            EnsureTarget(ref mask.Raw, size, "wf-ground-cover-mask-raw");
            EnsureTarget(ref mask.Across, size, "wf-ground-cover-mask-across");
        }

        mask.MapId = args.MapId;
        mask.Rect = rect;
        mask.Version = MaskVersion();
        mask.BuiltAt = real;
        mask.Wet = wet;

        _maskHandle = args.WorldHandle;
        _maskMapId = args.MapId;
        _maskRect = rect;
        _maskWet = wet;
        _hasContent = false;
        _coverGrids.Clear();
        _maskMatrix = Matrix3x2.CreateTranslation(-rect.Left, -rect.Top) * Matrix3x2.CreateScale(texels, -texels);

        // Snow is drawn sharp, then blurred so its edges round off; with none in view the mask is drawn as it is.
        args.WorldHandle.RenderInRenderTarget(_anySnow ? mask.Raw! : mask.Target!, _drawMask, ClearColor);
        if (_anySnow)
        {
            var radius = new Vector3(0f, 1f, 1f) * MathF.Max(1f, SnowSoftTiles * texels);
            Blur(args.WorldHandle, mask.Raw!, mask.Across!, _blurH, new Vector2(1f / size.X, 0f), radius);
            Blur(args.WorldHandle, mask.Across!, mask.Target!, _blurV, new Vector2(0f, 1f / size.Y), radius);
        }

        // Last, so the blur can't carry snow onto a ship's edge: a vessel's tiles are blanked, whatever lies below them.
        if (_hasContent && _coverGrids.Count > 0)
            args.WorldHandle.RenderInRenderTarget(mask.Target!, _eraseCover, null);

        mask.HasContent = _hasContent;
        mask.Content = _hasContent ? _content.Enlarged(ContentMargin) : default;
        return mask;
    }

    private void EnsureTarget(ref IRenderTexture? target, Vector2i size, string name)
    {
        if (target != null && target.Texture.Size == size)
            return;

        target?.Dispose();
        target = _clyde.CreateRenderTarget(size,
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            name);
    }

    private static void Blur(DrawingHandleWorld handle, IRenderTexture from, IRenderTexture to, ShaderInstance shader, Vector2 step, Vector3 radius)
    {
        shader.SetParameter("texelStep", step);
        shader.SetParameter("radius", radius);
        handle.RenderInRenderTarget(to,
            () =>
            {
                handle.SetTransform(Matrix3x2.Identity);
                handle.UseShader(shader);
                handle.DrawTextureRect(from.Texture, Box2.FromDimensions(Vector2.Zero, (Vector2) to.Size));
                handle.UseShader(null);
            },
            ClearColor);
    }

    /// <summary>Draws the mask: red where weather reaches the ground, green for snow depth, blue where snow lies.</summary>
    private void DrawMask()
    {
        var handle = _maskHandle;
        handle.UseShader(_unshaded);
        _grids.Clear();
        _map.FindGridsIntersecting(_maskMapId, _maskRect, ref _grids);

        foreach (var grid in _grids)
        {
            if (_wolfWeather.IsCoverGrid(grid.Owner))
            {
                _coverGrids.Add(grid);
                continue;
            }

            var snow = _cover.HasSnow(grid.Owner);
            if (!_maskWet && !snow)
                continue;

            handle.SetTransform(Matrix3x2.Multiply(_xform.GetWorldMatrix(grid.Owner), _maskMatrix));
            _roofQuery.TryComp(grid.Owner, out var roof);
            var roofed = new Entity<MapGridComponent, RoofComponent?>(grid.Owner, grid.Comp, roof);
            _rects.Clear();
            var any = false;
            var local = default(Box2);

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

                var bounds = _lookup.GetLocalBounds(tile, grid.Comp.TileSize);
                _rects.Add(new WorldRect(bounds, new Color(open, depth, depth > 0f ? 1f : 0f)));
                local = any ? local.Union(bounds) : bounds;
                any = true;
            }

            handle.DrawRectsUnmodulated(CollectionsMarshal.AsSpan(_rects));
            if (any)
                GrowContent(_xform.GetWorldMatrix(grid.Owner).TransformBox(local));
        }

        handle.UseShader(null);
    }

    /// <summary>Blanks the mask under every tile of the vessels the mask pass met, onto the finished mask.</summary>
    private void EraseCover()
    {
        var handle = _maskHandle;
        handle.UseShader(_unshaded);
        var black = new Color(0f, 0f, 0f, 1f);
        foreach (var grid in _coverGrids)
        {
            handle.SetTransform(Matrix3x2.Multiply(_xform.GetWorldMatrix(grid.Owner), _maskMatrix));
            _rects.Clear();
            var tiles = _map.GetTilesIntersecting(grid.Owner, grid, _maskRect);
            while (tiles.MoveNext(out var tile))
            {
                _rects.Add(new WorldRect(_lookup.GetLocalBounds(tile, grid.Comp.TileSize), black));
            }

            handle.DrawRectsUnmodulated(CollectionsMarshal.AsSpan(_rects));
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    /// <summary>Grows the box of wet and snowy ground, in world space.</summary>
    private void GrowContent(Box2 world)
    {
        _content = _hasContent ? _content.Union(world) : world;
        _hasContent = true;
    }

    private sealed class MaskTarget : IDisposable
    {
        public IRenderTexture? Target;
        public IRenderTexture? Raw;
        public IRenderTexture? Across;
        public MapId MapId;
        public Box2 Rect;
        public Box2 Content;
        public bool HasContent;
        public int Version;
        public TimeSpan BuiltAt;
        public bool Wet;

        public void Dispose()
        {
            Target?.Dispose();
            Raw?.Dispose();
            Across?.Dispose();
        }
    }
}
