using System.Diagnostics;
using System.Linq;
using Content.Shared._WF.Caverns;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Caverns;

public sealed partial class WFCavernMouthSystem
{
    /// <summary>Random candidates each cell tries after the gate candidate, if it has one.</summary>
    public const int CandidatesPerCell = 8;

    /// <summary>A candidate's pad keeps this many tiles from its cell's edges, so it never crosses into the next cell.</summary>
    public const int CellMargin = 4;

    /// <summary>How far past the hole's extent, along each axis from its anchor, the cavern check samples the tunnel.</summary>
    public const int OpennessMargin = 2;

    /// <summary>How many of the four samples must be open for the pad to sit in the tunnel web.</summary>
    public const int OpennessNeeded = 3;

    /// <summary>No grid but the map may lie this close to a footprint when it is stamped.</summary>
    public const float GridClearance = 4f;

    /// <summary>A cell whose claim takes longer than this, in milliseconds, is logged.</summary>
    private const double SlowClaimMs = 20;

    private static readonly Vector2i[] Cardinals = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

    private static readonly Entity<MapGridComponent>? NoGrid = null;

    /// <summary>One candidate before its shape is grown: the shape's seed and where in the cell it goes, as fractions.</summary>
    private readonly record struct Candidate(int Seed, double X, double Y, Vector2i? Fixed);

    /// <summary>What stops a site being stamped: nothing, something that may clear, or a pin, which never does.</summary>
    private enum Block : byte
    {
        None,
        Waiting,
        Pinned,
    }

    /// <summary>Claims a cell: its site is found once and cached, then stamped as soon as nothing blocks it.</summary>
    private WFCavernClaim ClaimCell(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i cell, WFCavernMouthKind kind)
    {
        if (!ground.Comp.Cells.TryGetValue(cell, out var state))
        {
            state = new WFCavernCell();
            ground.Comp.Cells[cell] = state;
        }

        if (state.State is WFCavernClaim.Claimed or WFCavernClaim.Empty)
            return state.State;

        if (!state.Evaluated)
        {
            var watch = Stopwatch.StartNew();
            state.Site = FindSite(ground, context, cell);
            state.Evaluated = true;

            if (watch.Elapsed.TotalMilliseconds > SlowClaimMs)
                Log.Warning($"Evaluating cavern mouth cell {cell} on {ToPrettyString(ground)} took {watch.Elapsed.TotalMilliseconds:F1} ms.");
        }

        if (state.Site is not { } site)
        {
            state.State = WFCavernClaim.Empty;
            return state.State;
        }

        switch (Blocked(context, site))
        {
            case Block.Pinned:
                state.State = WFCavernClaim.Empty;
                return state.State;
            case Block.Waiting:
                state.State = WFCavernClaim.Deferred;
                return state.State;
        }

        Stamp(ground, context, site, kind);
        state.State = WFCavernClaim.Claimed;
        return state.State;
    }

    /// <summary>The gate's first candidate: the planet centre plus the gate offset, as a tile.</summary>
    private static Vector2i GateCandidate(Entity<WFCavernGroundComponent> ground, WFCavernMouthSpec spec)
    {
        var centre = new Vector2i((int) MathF.Floor(ground.Comp.Centre.X), (int) MathF.Floor(ground.Comp.Centre.Y));
        return centre + spec.GateOffset;
    }

    /// <summary>A cell's candidates in order: the gate candidate if it lies in the cell, then eight seeded ones.</summary>
    private List<Candidate> Candidates(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i cell)
    {
        var spec = context.Spec;
        var seed = context.Ground.Comp1.Seed;
        var candidates = new List<Candidate>(CandidatesPerCell + 1);
        var gate = GateCandidate(ground, spec);

        if (CellOf(spec, gate) == cell)
            candidates.Add(new Candidate(WFCavernMouthShape.SeedAt(seed, gate), 0, 0, gate));

        // Plain arithmetic, never HashCode.Combine: that is randomised per process.
        var random = new System.Random(unchecked(seed * 7919 + cell.X * 73856093 + cell.Y * 19349663));

        for (var i = 0; i < CandidatesPerCell; i++)
        {
            candidates.Add(new Candidate(random.Next(), random.NextDouble(), random.NextDouble(), null));
        }

        return candidates;
    }

    /// <summary>The first candidate whose grown shape fits the cell and passes both pure checks, or null. Reads only noise, so it never depends on timing.</summary>
    private WFCavernSite? FindSite(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i cell)
    {
        foreach (var candidate in Candidates(ground, context, cell))
        {
            var shape = WFCavernMouthShape.Generate(context.Spec, candidate.Seed);
            if (!TryPlace(context.Spec, cell, shape, candidate, out var origin))
                continue;

            var site = new WFCavernSite(origin, shape);
            if (CavernAllows(context, site) && GroundAllows(context, site))
                return site;
        }

        return null;
    }

    /// <summary>Anchors a candidate: the gate candidate where it is, the others spread by their fractions over the spots whose whole pad stays <see cref="CellMargin"/> inside the cell.</summary>
    private static bool TryPlace(WFCavernMouthSpec spec, Vector2i cell, WFCavernMouthShape shape, Candidate candidate, out Vector2i origin)
    {
        if (candidate.Fixed is { } fixedAt)
        {
            origin = fixedAt;
            return true;
        }

        var reach = spec.PadRadius + CellMargin;
        var cellMin = cell * spec.CellSize;
        var low = cellMin - shape.Min + new Vector2i(reach, reach);
        var high = cellMin + new Vector2i(spec.CellSize - 1, spec.CellSize - 1) - shape.Max - new Vector2i(reach, reach);

        origin = low;
        if (high.X < low.X || high.Y < low.Y)
            return false;

        origin = new Vector2i(
            low.X + Math.Min((int) (candidate.X * (high.X - low.X + 1)), high.X - low.X),
            low.Y + Math.Min((int) (candidate.Y * (high.Y - low.Y + 1)), high.Y - low.Y));
        return true;
    }

    /// <summary>Every footprint tile's natural tile is in the allowlist and its natural entity is not one to avoid.</summary>
    private bool GroundAllows(MouthContext context, WFCavernSite site)
    {
        var biome = context.Ground.Comp1;

        foreach (var offset in site.Shape.Hole.Concat(site.Shape.Ring))
        {
            var index = site.Origin + offset;

            if (!_biome.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var tile)
                || !context.GroundTiles.Contains(_tileDefs[tile.Value.TypeId].ID))
                return false;

            if (_biome.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, NoGrid, out var entity)
                && context.Avoid.Contains(entity))
                return false;
        }

        return true;
    }

    /// <summary>The cavern is open under the anchor and at three of the four points just past the hole's extent along each axis.</summary>
    private bool CavernAllows(MouthContext context, WFCavernSite site)
    {
        var shape = site.Shape;

        if (!IsCavernOpen(context, site.Origin))
            return false;

        var reaches = new[]
        {
            shape.Max.Y + OpennessMargin,
            shape.Max.X + OpennessMargin,
            -shape.Min.Y + OpennessMargin,
            -shape.Min.X + OpennessMargin,
        };

        var open = 0;
        for (var i = 0; i < Cardinals.Length; i++)
        {
            if (IsCavernOpen(context, site.Origin + Cardinals[i] * reaches[i]))
                open++;
        }

        return open >= OpennessNeeded;
    }

    /// <summary>Whether the natural cavern has floor and no entity at a tile, the landing entity aside: a pool is open floor.</summary>
    private bool IsCavernOpen(MouthContext context, Vector2i index)
    {
        var biome = context.Level.Comp1;

        return _biome.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var tile)
               && (!_biome.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, NoGrid, out var entity)
                   || entity == context.Spec.LandingEntity?.Id);
    }

    /// <summary>
    /// Whether a stamp would cut into something. A pinned footprint or pad tile (another mouth, or ground something
    /// else changed for good) blocks it for ever; loaded terrain, something anchored or a grid nearby only for now.
    /// </summary>
    private Block Blocked(MouthContext context, WFCavernSite site)
    {
        var groundBiome = (context.Ground.Owner, context.Ground.Comp1);
        var levelBiome = (context.Level.Owner, context.Level.Comp1);
        var pad = site.Shape.Pad(context.Spec.PadRadius);

        if (site.Shape.Hole.Concat(site.Shape.Ring).Any(offset => _biome.WfIsPinned(groundBiome, site.Origin + offset))
            || pad.Any(offset => _biome.WfIsPinned(levelBiome, site.Origin + offset)))
            return Block.Pinned;

        foreach (var offset in site.Shape.Hole.Concat(site.Shape.Ring))
        {
            var index = site.Origin + offset;

            if (_biome.WfIsChunkLoaded(groundBiome, index)
                || _map.GetAnchoredEntitiesEnumerator(context.Ground.Owner, context.Ground.Comp2, index).MoveNext(out _))
                return Block.Waiting;
        }

        if (pad.Any(offset => _biome.WfIsChunkLoaded(levelBiome, site.Origin + offset)))
            return Block.Waiting;

        var footprint = new Box2(site.Origin + site.Shape.Min - Vector2i.One, site.Origin + site.Shape.Max + new Vector2i(2, 2));
        var grids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(Comp<MapComponent>(context.Ground).MapId, footprint.Enlarged(GridClearance), ref grids,
            approx: true, includeMap: false);

        return grids.Count == 0 ? Block.None : Block.Waiting;
    }

    /// <summary>Cuts the mouth: ring and hole on the ground, the pad in the cavern, then shades, rim and climb point.</summary>
    // One SetTiles per map. Pinned tiles skip tile, entity and decal generation, so this holds on unloaded chunks too.
    private WFCavernMouth Stamp(Entity<WFCavernGroundComponent> ground, MouthContext context, WFCavernSite site, WFCavernMouthKind kind)
    {
        var spec = context.Spec;
        var mouth = new WFCavernMouth(site.Origin, site.Shape, kind);
        var groundGrid = (context.Ground.Owner, context.Ground.Comp2);
        var levelGrid = (context.Level.Owner, context.Level.Comp2);
        var groundBiome = context.Ground.Comp1;
        var levelBiome = context.Level.Comp1;
        var pad = mouth.Pad(spec.PadRadius);

        ClearBiomeEntities(context.Ground, mouth.Footprint);
        ClearBiomeEntities(context.Level, pad);

        var groundTiles = new List<(Vector2i, Tile)>();
        var pinned = new List<Vector2i>();

        foreach (var index in mouth.Ring)
        {
            pinned.Add(index);

            if (_map.TryGetTileRef(context.Ground.Owner, context.Ground.Comp2, index, out var existing) && !existing.Tile.IsEmpty)
                continue;

            if (_biome.TryGetTile(index, groundBiome.Layers, groundBiome.Seed, NoGrid, out var natural))
                groundTiles.Add((index, natural.Value));
        }

        foreach (var index in mouth.Hole)
        {
            pinned.Add(index);

            if (_map.TryGetTileRef(context.Ground.Owner, context.Ground.Comp2, index, out var existing) && !existing.Tile.IsEmpty)
                groundTiles.Add((index, Tile.Empty));
        }

        if (groundTiles.Count > 0)
            _map.SetTiles(context.Ground.Owner, context.Ground.Comp2, groundTiles);
        _biome.WfPinTiles((context.Ground.Owner, groundBiome), pinned);

        var landing = new Tile(_tileDefs[spec.LandingTile].TileId);
        var padTiles = new List<(Vector2i, Tile)>();

        foreach (var index in pad)
        {
            if (mouth.Contains(index) || !_biome.TryGetTile(index, levelBiome.Layers, levelBiome.Seed, NoGrid, out var natural))
                padTiles.Add((index, landing));
            else
                padTiles.Add((index, natural.Value));
        }

        _map.SetTiles(context.Level.Owner, context.Level.Comp2, padTiles);
        _biome.WfPinTiles((context.Level.Owner, levelBiome), pad.ToList());

        // Pinned tiles grow no biome entities, so the landing's own entity is laid here: on the landing tiles and
        // wherever the cavern would grow it, so a pool the pad crosses stays wet.
        if (spec.LandingEntity is { } landingEntity)
        {
            foreach (var (index, tile) in padTiles)
            {
                if (tile.TypeId != landing.TypeId
                    && (!_biome.TryGetEntity(index, levelBiome.Layers, tile, levelBiome.Seed, NoGrid, out var natural)
                        || natural != landingEntity.Id))
                    continue;

                if (!HasAnchored(levelGrid, index, landingEntity))
                    SpawnAnchored(landingEntity, levelGrid, index);
            }
        }

        var air = WFCavernAirClassifier.Classify(_proto.Index(context.Cavern.Level).Atmosphere);
        var landingMultiplier = ((ContentTileDefinition) _tileDefs[spec.LandingTile]).FallDamageMultiplier;

        foreach (var index in mouth.Hole)
        {
            if (ground.Comp.Shades.ContainsKey(index))
                continue;

            var shade = Spawn(spec.Shade, _map.GridTileToLocal(ground.Owner, context.Ground.Comp2, index));
            var shaft = EnsureComp<WFCavernShaftComponent>(shade);
            shaft.Cavern = context.Cavern.ID;
            shaft.Air = air;
            shaft.LandingMultiplier = landingMultiplier;
            Dirty(shade, shaft);
            ground.Comp.Shades[index] = shade;
        }

        if (spec.Rim.Count > 0)
        {
            for (var i = 0; i < site.Shape.Rim.Count; i++)
            {
                SpawnAnchored(spec.Rim[i % spec.Rim.Count], groundGrid, site.Origin + site.Shape.Rim[i]);
            }
        }

        if (!ground.Comp.ClimbPoints.ContainsKey(mouth.ClimbTile))
        {
            var climb = SpawnAnchored(spec.ClimbPoint, levelGrid, mouth.ClimbTile);
            var climbComp = EnsureComp<WFCavernClimbComponent>(climb);
            climbComp.Delay = spec.ClimbSeconds * Math.Clamp(context.Surface.Gravity, 1f, 2.5f);
            Dirty(climb, climbComp);
            ground.Comp.ClimbPoints[mouth.ClimbTile] = climb;
        }

        ground.Comp.Mouths.Add(mouth);
        return mouth;
    }

    /// <summary>Deletes the entities the biome spawned on these tiles of a loaded chunk; nothing a player built.</summary>
    private void ClearBiomeEntities(Entity<BiomeComponent, MapGridComponent> map, IEnumerable<Vector2i> indices)
    {
        var doomed = new List<EntityUid>();

        foreach (var index in indices)
        {
            if (!_biome.WfIsChunkLoaded((map.Owner, map.Comp1), index))
                continue;

            foreach (var anchored in _map.GetAnchoredEntities(map.Owner, map.Comp2, index))
            {
                if (_biome.WfIsBiomeSpawned((map.Owner, map.Comp1), anchored, index))
                    doomed.Add(anchored);
            }
        }

        foreach (var uid in doomed)
        {
            Del(uid);
        }
    }

    /// <summary>Whether an entity of this prototype is already anchored on a tile.</summary>
    private bool HasAnchored(Entity<MapGridComponent> grid, Vector2i index, EntProtoId proto)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, index);

        while (anchored.MoveNext(out var uid))
        {
            if (MetaData(uid.Value).EntityPrototype?.ID == proto.Id)
                return true;
        }

        return false;
    }

    /// <summary>Spawns an entity on a tile and anchors it there.</summary>
    private EntityUid SpawnAnchored(EntProtoId proto, Entity<MapGridComponent> grid, Vector2i index)
    {
        var uid = Spawn(proto, _map.GridTileToLocal(grid, grid.Comp, index));
        var xform = Transform(uid);

        if (!xform.Anchored && !_transform.AnchorEntity((uid, xform), grid, index))
            Log.Error($"Could not anchor {ToPrettyString(uid)} on {ToPrettyString(grid)} at {index}.");

        return uid;
    }
}
