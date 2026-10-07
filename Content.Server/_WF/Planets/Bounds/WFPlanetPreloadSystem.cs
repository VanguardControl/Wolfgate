using System.Diagnostics;
using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.Parallax;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets.Bounds;

/// <summary>
/// Generates a bounded world's whole ground when its network is built, nearest the centre first and a slice a tick,
/// then walls its outermost ring. One world at a time; the others wait their turn.
/// </summary>
public sealed partial class WFPlanetPreloadSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WFPlanetBoundsSystem _bounds = default!;

    /// <summary>Worlds waiting for or in the middle of their preload, first come first served.</summary>
    private readonly List<Preload> _queue = new();
    private readonly Stopwatch _watch = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetNetworkBuiltEvent>(OnNetworkBuilt);
    }

    /// <summary>Whether a ground is queued or in the middle of its preload.</summary>
    public bool IsPreloading(EntityUid ground)
    {
        foreach (var preload in _queue)
        {
            if (preload.Ground == ground)
                return true;
        }

        return false;
    }

    private void OnNetworkBuilt(ref WFPlanetNetworkBuiltEvent args)
    {
        if (!_cfg.GetCVar(PlanetCVars.Preload) || !args.Surface.Preload)
            return;

        // Starts next tick, once every handler of the build has had its turn with the fresh ground; the bounds are
        // read then, since the handler that marks them may run after this one.
        _queue.Add(new Preload(args.Ground, args.Surface.BoundaryWall));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        while (_queue.Count > 0)
        {
            var preload = _queue[0];

            if (TerminatingOrDeleted(preload.Ground)
                || !TryComp<BiomeComponent>(preload.Ground, out var biome)
                || !TryComp<MapGridComponent>(preload.Ground, out var grid)
                || !TryComp<WFPlanetBoundsComponent>(preload.Ground, out var bounds)
                || bounds.Radius <= 0f)
            {
                _queue.RemoveAt(0);
                continue;
            }

            if (preload.Chunks == null)
            {
                var starting = new WFPlanetPreloadStartingEvent(preload.Ground, bounds.Centre, bounds.Radius);
                RaiseLocalEvent(ref starting);
                preload.Chunks = _biome.WfChunksInBounds(bounds);
                preload.Started = Stopwatch.GetTimestamp();
            }

            Load(preload, (preload.Ground, biome, grid));

            if (preload.Next < preload.Chunks.Count)
                return;

            Finish(preload, (preload.Ground, biome, grid), bounds);
            _queue.RemoveAt(0);
        }
    }

    /// <summary>Loads chunks until the tick's budget is spent, the lobby's while the round hasn't started; one always goes.</summary>
    private void Load(Preload preload, Entity<BiomeComponent, MapGridComponent> ground)
    {
        var budget = _cfg.GetCVar(_ticker.RunLevel == GameRunLevel.PreRoundLobby ? PlanetCVars.PreloadBudgetLobby : PlanetCVars.PreloadBudget);
        _watch.Restart();

        while (preload.Next < preload.Chunks!.Count)
        {
            _biome.WfLoadChunk(ground, preload.Chunks[preload.Next++]);

            if (_watch.Elapsed.TotalMilliseconds >= budget)
                break;
        }
    }

    /// <summary>Lays the boundary ring on the ground and the layers below it, marks the ground preloaded and tells the other modules.</summary>
    private void Finish(Preload preload, Entity<BiomeComponent, MapGridComponent> ground, WFPlanetBoundsComponent bounds)
    {
        var walls = LayRing(ground, bounds, preload.Wall);
        EnsureComp<WFPlanetPreloadedComponent>(ground);

        // A cavern stays streamed, so its ring is laid onto bare ground and pinned: the loader fills in round it.
        if (TryComp<WFPlanetLayerComponent>(ground, out var layer)
            && TryGetEntity(layer.Network, out var network)
            && TryComp<WFPlanetNetworkComponent>(network, out var comp))
        {
            foreach (var lower in comp.LowerLayers)
            {
                if (TryComp<BiomeComponent>(lower, out var lowerBiome)
                    && TryComp<MapGridComponent>(lower, out var lowerGrid)
                    && TryComp<WFPlanetBoundsComponent>(lower, out var lowerBounds))
                    walls += LayRing((lower, lowerBiome, lowerGrid), lowerBounds, preload.Wall);
            }
        }

        var took = Stopwatch.GetElapsedTime(preload.Started).TotalSeconds;
        Log.Info($"Preloaded {preload.Chunks!.Count} chunks of {ToPrettyString(ground)} in {took:F1} s, with {walls} boundary walls.");

        var ev = new WFPlanetPreloadedEvent(ground);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>
    /// Walls every tile inside the circle with a neighbour outside it: whatever the biome grew there goes, a tile not
    /// loaded yet is laid from the recipe, and the tile is pinned so nothing grows back or is ever unloaded.
    /// </summary>
    private int LayRing(Entity<BiomeComponent, MapGridComponent> ground, WFPlanetBoundsComponent bounds, EntProtoId? wall)
    {
        if (wall == null)
            return 0;

        var ring = new List<Vector2i>();
        var radius = (int) MathF.Ceiling(bounds.Radius) + 1;
        var centre = new Vector2i((int) MathF.Floor(bounds.Centre.X), (int) MathF.Floor(bounds.Centre.Y));

        for (var x = centre.X - radius; x <= centre.X + radius; x++)
        for (var y = centre.Y - radius; y <= centre.Y + radius; y++)
        {
            var tile = new Vector2i(x, y);

            if (!bounds.ContainsTile(tile))
                continue;

            if (bounds.ContainsTile(tile + Vector2i.Right) && bounds.ContainsTile(tile + Vector2i.Left)
                && bounds.ContainsTile(tile + Vector2i.Up) && bounds.ContainsTile(tile + Vector2i.Down))
                continue;

            ring.Add(tile);
        }

        var laid = 0;
        var anchored = new List<EntityUid>();

        foreach (var tile in ring)
        {
            // A ring tile the biome left empty, such as under water, is filled so the wall has something to stand on.
            if (!_map.TryGetTileRef(ground, ground.Comp2, tile, out var existing) || existing.Tile.IsEmpty)
            {
                if (!_biome.TryGetBiomeTile(tile, ground.Comp1.Layers, ground.Comp1.Seed, (ground.Owner, ground.Comp2), out var natural))
                    continue;

                _map.SetTile(ground, ground.Comp2, tile, natural.Value);
            }

            anchored.Clear();
            var enumerator = _map.GetAnchoredEntitiesEnumerator(ground, ground.Comp2, tile);

            while (enumerator.MoveNext(out var uid))
            {
                anchored.Add(uid.Value);
            }

            foreach (var uid in anchored)
            {
                QueueDel(uid);
            }

            Spawn(wall, _map.GridTileToLocal(ground, ground.Comp2, tile));
            laid++;
        }

        _biome.WfPinTiles((ground.Owner, ground.Comp1), ring);
        return laid;
    }

    /// <summary>One world's preload: its chunks nearest first, and how far it has got.</summary>
    private sealed class Preload
    {
        public readonly EntityUid Ground;
        public readonly EntProtoId? Wall;
        public List<Vector2i>? Chunks;
        public int Next;
        public long Started;

        public Preload(EntityUid ground, EntProtoId? wall)
        {
            Ground = ground;
            Wall = wall;
        }
    }
}
