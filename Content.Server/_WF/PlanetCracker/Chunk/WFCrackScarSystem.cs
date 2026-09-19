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

/// <summary>
/// Keeps the holes cut out of a ground layer open for the rest of the round.
/// BiomeSystem.ReserveTiles regenerates an EMPTY tile unconditionally, with no ModifiedTiles guard, and every grid
/// landing over the site reaches it through ShuttleSystem.Smimsh - the chunk's own landing included - so the hole is
/// re-stamped from a scar recorded on the GROUND grid, which outlives the chunk that made it.
/// Both subscriptions are BROADCAST and by ref: WFChunkExtractedEvent is otherwise subscribed only by the test
/// recorder at GravityAnchorTest.cs:1211 and ShuttleFlattenEvent only by BiomeSystem.cs:85, which is itself the proof
/// that several broadcast subscribers are legal - the duplicate-subscription crash is a directed-pair rule.
/// </summary>
public sealed partial class WFCrackScarSystem : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>Per-event dedupe across overlapping AABBs; holds EVERY index walked, scar member or not.</summary>
    private readonly HashSet<Vector2i> _seen = new();

    /// <summary>Only the indices that passed the membership test; the one collection ever handed to WfPinTiles.</summary>
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

    /// <summary>A disc was cut, so the ground grid remembers the circle it lost.</summary>
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

    /// <summary>
    /// Undoes the refill Smimsh's ReserveTiles just performed, in the same tick and before the crash gate is reached.
    /// The first statement is the early-out: this runs on every FTL arrival anywhere in the round.
    /// </summary>
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

    /// <summary>
    /// Re-stamps every scar on a ground grid with no AABB filter: the landing pass and the post-cleanup pass.
    /// Driven off the ground grid rather than a chunk, so it still works once the chunk has been deleted.
    /// </summary>
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

    /// <summary>
    /// Dedupes one index and, on a membership hit only, pushes it into both payloads.
    /// Membership is recomputed from Centre/Radius with the extraction's own test verbatim
    /// (WFPlanetChunkSystem.Extraction.cs:126-131), so the two sets are identical by construction.
    /// </summary>
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

    /// <summary>
    /// One SetTiles and one pin pass.
    /// Only _matched is pinned: WfPinTiles adds everything it is given to BiomeComponent.ModifiedTiles unconditionally
    /// and nothing ever un-pins (BiomeSystem.WFChunkPin.cs:22-29), so passing _seen would permanently freeze the whole
    /// square footprint of every grid that ever lands on the planet.
    /// </summary>
    private void Stamp(EntityUid groundMap, MapGridComponent grid, BiomeComponent biome)
    {
        if (_buffer.Count == 0)
            return;

        _map.SetTiles(groundMap, grid, _buffer);
        _biome.WfPinTiles((groundMap, biome), _matched);
    }
}
