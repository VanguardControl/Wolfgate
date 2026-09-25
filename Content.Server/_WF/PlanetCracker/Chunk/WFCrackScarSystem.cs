using System.Numerics;
using Content.Server.Parallax;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Chunk;

/// <summary>Keeps holes cut into a ground layer open all round, from scars recorded on the ground grid.</summary>
public sealed partial class WFCrackScarSystem : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>Per-event dedupe of every index walked, scar member or not.</summary>
    private readonly HashSet<Vector2i> _seen = new();

    /// <summary>Indices inside a scar; the only set handed to WfPinTiles.</summary>
    private readonly List<Vector2i> _matched = new();

    /// <summary>The SetTiles payload built alongside <see cref="_matched"/>.</summary>
    private readonly List<(Vector2i Index, Tile Tile)> _buffer = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFChunkExtractedEvent>(OnChunkExtracted);
        SubscribeLocalEvent<ShuttleFlattenEvent>(OnShuttleFlatten);
    }

    private void OnChunkExtracted(ref WFChunkExtractedEvent args)
    {
        RecordScar(args.GroundMap, args.HoleCentre, args.Radius);
    }

    /// <summary>Records one cut circle on a ground grid; the entry point for a caller with no extraction event.</summary>
    public void RecordScar(EntityUid groundMap, Vector2 centre, float radius)
    {
        EnsureComp<WFCrackScarComponent>(groundMap).Scars.Add(new WFCrackScar { Centre = centre, Radius = radius });
        if (TryComp<WFPlanetLayerComponent>(groundMap, out var layer)
            && layer.Network is { } networkId
            && TryGetEntity(networkId, out var network)
            && TryComp<WFPlanetNetworkComponent>(network, out var planet)
            && TryComp<WFOrbitLayerComponent>(planet.OrbitMap, out var orbit))
        {
            orbit.RadarScars.Add(new Vector3(centre, radius));
            Dirty(planet.OrbitMap, orbit);
        }
    }

    /// <summary>Undoes the refill Smimsh's ReserveTiles just made; runs on every FTL arrival.</summary>
    private void OnShuttleFlatten(ref ShuttleFlattenEvent ev)
    {
        if (!TryComp<WFCrackScarComponent>(ev.MapUid, out var scar)
            || !TryComp<MapGridComponent>(ev.MapUid, out var grid)
            || !TryComp<BiomeComponent>(ev.MapUid, out var biome))
        {
            return;
        }

        _seen.Clear();
        _matched.Clear();
        _buffer.Clear();

        foreach (var aabb in ev.AABBs)
        {
            for (var x = Math.Floor(aabb.Left); x <= Math.Ceiling(aabb.Right); x++)
            {
                for (var y = Math.Floor(aabb.Bottom); y <= Math.Ceiling(aabb.Top); y++)
                {
                    Collect(new Vector2i((int) x, (int) y), scar, grid);
                }
            }
        }

        Stamp(ev.MapUid, grid, biome);
    }

    /// <summary>Re-stamps every scar on a ground grid; works after the chunk is gone.</summary>
    public void ReStamp(EntityUid groundMap)
    {
        if (!TryComp<WFCrackScarComponent>(groundMap, out var scar)
            || !TryComp<MapGridComponent>(groundMap, out var grid)
            || !TryComp<BiomeComponent>(groundMap, out var biome))
        {
            return;
        }

        _seen.Clear();
        _matched.Clear();
        _buffer.Clear();

        foreach (var entry in scar.Scars)
        {
            var aabb = Box2.CenteredAround(entry.Centre, new Vector2(entry.Radius * 2f, entry.Radius * 2f));

            for (var x = Math.Floor(aabb.Left); x <= Math.Ceiling(aabb.Right); x++)
            {
                for (var y = Math.Floor(aabb.Bottom); y <= Math.Ceiling(aabb.Top); y++)
                {
                    Collect(new Vector2i((int) x, (int) y), scar, grid);
                }
            }
        }

        Stamp(groundMap, grid, biome);
    }

    /// <summary>Dedupes one index and queues it if it lies inside a scar (the extraction's own test).</summary>
    private void Collect(Vector2i index, WFCrackScarComponent scar, MapGridComponent grid)
    {
        if (!_seen.Add(index))
            return;

        var centre = (Vector2) index + grid.TileSizeHalfVector;

        foreach (var entry in scar.Scars)
        {
            if ((centre - entry.Centre).LengthSquared() > entry.Radius * entry.Radius)
                continue;

            _matched.Add(index);
            _buffer.Add((index, Tile.Empty));
            return;
        }
    }

    /// <summary>One SetTiles and one pin pass; only matched tiles are pinned, since pins are permanent.</summary>
    private void Stamp(EntityUid groundMap, MapGridComponent grid, BiomeComponent biome)
    {
        if (_buffer.Count == 0)
            return;

        _map.SetTiles(groundMap, grid, _buffer);
        _biome.WfPinTiles((groundMap, biome), _matched);
    }
}
