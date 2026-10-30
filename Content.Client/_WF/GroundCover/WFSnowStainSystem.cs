using System.Numerics;
using Content.Shared._WF.GroundCover;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._WF.GroundCover;

/// <summary>
/// Stains in snow cover under puddles of coloured liquid such as blood, laid on this client from the puddles it sees.
/// A stain stays after its puddle is gone, then fades with time and with the snow over it, as footprints do.
/// </summary>
public sealed class WFSnowStainSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private WFGroundCoverSystem _cover = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Stains kept at most; the oldest go first.</summary>
    public const int MaxStains = 400;

    /// <summary>Opacity of a fresh stain's centre.</summary>
    public const float Opacity = 0.5f;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<EntityUid, Dictionary<Vector2i, WFSnowStain>> _stains = new();
    private readonly Queue<WFSnowStain> _order = new();
    private readonly List<EntityUid> _stale = new();
    private readonly List<Vector2i> _bare = new();
    private TimeSpan _nextScan;
    private int _seenVersion = -1;

    /// <summary>Stains laid and not yet gone.</summary>
    public int Count { get; private set; }

    /// <summary>Stains by grid and tile.</summary>
    public IReadOnlyDictionary<EntityUid, Dictionary<Vector2i, WFSnowStain>> Stains => _stains;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new WFSnowStainOverlay(this));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<WFSnowStainOverlay>();
        Clear();
    }

    /// <summary>Forgets every stain.</summary>
    public void Clear()
    {
        _stains.Clear();
        _order.Clear();
        Count = 0;
    }

    /// <summary>How visible a stain is now, from 1 fresh to 0 gone.</summary>
    public float Visibility(WFSnowStain stain)
    {
        var age = (float) ((_timing.CurTime - stain.Seen) / Lifetime);
        return Math.Clamp(1f - age, 0f, 1f) * (1f - stain.Filled) * stain.Thickness;
    }

    /// <summary>Whether a puddle colour stains snow: water and other pale blue liquids don't.</summary>
    public static bool StainsSnow(Color color)
    {
        var paleBlue = color.B >= color.R && color.B >= color.G && color.R * 0.3f + color.G * 0.6f + color.B * 0.1f > 0.4f;
        return color.A > 0.05f && !paleBlue;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_cover.Version != _seenVersion)
        {
            _seenVersion = _cover.Version;
            Recheck();
        }

        var now = _timing.CurTime;
        while (_order.TryPeek(out var oldest) && (oldest.Gone || now - oldest.Seen > Lifetime || Count > MaxStains))
        {
            _order.Dequeue();
            Remove(oldest);
        }

        if (now < _nextScan)
            return;

        _nextScan = now + ScanInterval;
        Scan(now);
    }

    /// <summary>Lays or refreshes a stain under every puddle on a snow covered tile.</summary>
    private void Scan(TimeSpan now)
    {
        var query = EntityQueryEnumerator<PuddleComponent, AppearanceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var appearance, out var xform))
        {
            if (xform.GridUid is not { } grid || xform.ParentUid != grid || !_cover.HasSnow(grid)
                || !TryComp<MapGridComponent>(grid, out var gridComp)
                || !_appearance.TryGetData(uid, PuddleVisuals.SolutionColor, out Color color, appearance)
                || !StainsSnow(color))
                continue;

            var tile = _map.LocalToTile(grid, gridComp, xform.Coordinates);
            var depth = _cover.GetDepth(grid, tile);
            if (depth < WFSnowCover.CoverFrom)
                continue;

            _appearance.TryGetData(uid, PuddleVisuals.CurrentVolume, out float volume, appearance);
            Lay(grid, tile, xform.LocalPosition, color, Math.Clamp(volume, 0f, 1f), depth, now);
        }
    }

    /// <summary>Lays a stain on a tile, or refreshes the one there.</summary>
    public WFSnowStain Lay(EntityUid grid, Vector2i tile, Vector2 position, Color color, float volume, int depth, TimeSpan now)
    {
        if (!_stains.TryGetValue(grid, out var tiles))
        {
            tiles = new Dictionary<Vector2i, WFSnowStain>();
            _stains[grid] = tiles;
        }

        if (tiles.TryGetValue(tile, out var stain) && !stain.Gone)
        {
            stain.Seen = now;
            stain.Color = color;
            stain.Radius = MathF.Max(stain.Radius, RadiusOf(volume));
            return stain;
        }

        stain = new WFSnowStain(grid, tile, position, color, RadiusOf(volume), now, depth);
        tiles[tile] = stain;
        _order.Enqueue(stain);
        Count++;
        return stain;
    }

    private static float RadiusOf(float volume)
    {
        return 0.3f + 0.3f * volume;
    }

    /// <summary>Drops stains whose snow is gone, and fades those whose snow has thinned or filled in.</summary>
    private void Recheck()
    {
        _stale.Clear();
        foreach (var (grid, tiles) in _stains)
        {
            if (TerminatingOrDeleted(grid))
            {
                _stale.Add(grid);
                continue;
            }

            _bare.Clear();
            foreach (var (tile, stain) in tiles)
            {
                var depth = _cover.GetDepth(grid, tile);
                stain.Filled = Math.Clamp((depth - stain.Depth) / (float) WFSnowPrintsSystem.FillUnits, 0f, 1f);
                stain.Thickness = WFSnowPrintsSystem.Thickness(stain.Depth, depth);
                if (depth > 0 && stain.Filled < 1f)
                    continue;

                stain.Gone = true;
                Count--;
                _bare.Add(tile);
            }

            foreach (var tile in _bare)
            {
                tiles.Remove(tile);
            }
        }

        foreach (var grid in _stale)
        {
            foreach (var stain in _stains[grid].Values)
            {
                stain.Gone = true;
                Count--;
            }

            _stains.Remove(grid);
        }
    }

    private void Remove(WFSnowStain stain)
    {
        if (stain.Gone)
            return;

        stain.Gone = true;
        Count--;
        if (!_stains.TryGetValue(stain.Grid, out var tiles))
            return;

        tiles.Remove(stain.Tile);
        if (tiles.Count == 0)
            _stains.Remove(stain.Grid);
    }
}

/// <summary>One stain in snow, in its grid's space.</summary>
public sealed class WFSnowStain(EntityUid grid, Vector2i tile, Vector2 position, Color color, float radius, TimeSpan seen, int depth)
{
    public readonly EntityUid Grid = grid;
    public readonly Vector2i Tile = tile;
    public readonly Vector2 Position = position;

    /// <summary>The liquid's colour.</summary>
    public Color Color = color;

    /// <summary>Tiles across, from the middle out.</summary>
    public float Radius = radius;

    /// <summary>When a puddle was last seen over it.</summary>
    public TimeSpan Seen = seen;

    /// <summary>Snow depth on its tile when it was laid.</summary>
    public readonly int Depth = depth;

    /// <summary>How far fresh snow has covered it, 0 to 1.</summary>
    public float Filled;

    /// <summary>How much of its snow is left, 1 to 0, as melt thins the cover.</summary>
    public float Thickness = 1f;

    public bool Gone;
}
