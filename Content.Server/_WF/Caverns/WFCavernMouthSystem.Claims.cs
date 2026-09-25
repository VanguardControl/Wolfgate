using System.Diagnostics;
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

    /// <summary>Candidates keep this many tiles from the cell's edges, so a pad never crosses into the next cell.</summary>
    public const int CellInset = 8;

    /// <summary>How far out from the hole's centre the cavern check samples the tunnel.</summary>
    public const int OpennessReach = 3;

    /// <summary>How many of the four samples must be open for the pad to sit in the tunnel web.</summary>
    public const int OpennessNeeded = 3;

    /// <summary>No grid but the map may lie this close to a footprint when it is stamped.</summary>
    public const float GridClearance = 4f;

    /// <summary>A cell whose claim takes longer than this, in milliseconds, is logged.</summary>
    private const double SlowClaimMs = 20;

    private static readonly Vector2i[] Cardinals = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

    private static readonly Entity<MapGridComponent>? NoGrid = null;

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

        if (!CanStamp(ground, context, site))
        {
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
    private List<Vector2i> Candidates(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i cell)
    {
        var spec = context.Spec;
        var candidates = new List<Vector2i>(CandidatesPerCell + 1);
        var gate = GateCandidate(ground, spec);

        if (CellOf(spec, gate) == cell)
            candidates.Add(gate);

        // Plain arithmetic, never HashCode.Combine: that is randomised per process.
        var random = new System.Random(unchecked(context.Ground.Comp1.Seed * 7919 + cell.X * 73856093 + cell.Y * 19349663));
        var min = cell * spec.CellSize + new Vector2i(CellInset, CellInset);
        var span = Math.Max(1, spec.CellSize - 2 * CellInset - spec.HoleSize + 1);

        for (var i = 0; i < CandidatesPerCell; i++)
        {
            candidates.Add(new Vector2i(min.X + random.Next(span), min.Y + random.Next(span)));
        }

        return candidates;
    }

    /// <summary>The first candidate that passes both pure checks, or null. Reads only noise, so it never depends on timing.</summary>
    private Vector2i? FindSite(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i cell)
    {
        foreach (var candidate in Candidates(ground, context, cell))
        {
            if (GroundAllows(context, candidate) && CavernAllows(context, candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>Every footprint tile's natural tile is in the allowlist and its natural entity is not one to avoid.</summary>
    private bool GroundAllows(MouthContext context, Vector2i origin)
    {
        var biome = context.Ground.Comp1;

        foreach (var index in Footprint(origin, context.Spec.HoleSize))
        {
            if (!_biome.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var tile)
                || !context.GroundTiles.Contains(_tileDefs[tile.Value.TypeId].ID))
                return false;

            if (_biome.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, NoGrid, out var entity)
                && context.Avoid.Contains(entity))
                return false;
        }

        return true;
    }

    /// <summary>The cavern is open under the hole's centre and at three of the four points a few tiles out.</summary>
    private bool CavernAllows(MouthContext context, Vector2i origin)
    {
        var half = context.Spec.HoleSize / 2;
        var centre = origin + new Vector2i(half, half);

        if (!IsCavernOpen(context, centre))
            return false;

        var open = 0;
        foreach (var direction in Cardinals)
        {
            if (IsCavernOpen(context, centre + direction * OpennessReach))
                open++;
        }

        return open >= OpennessNeeded;
    }

    /// <summary>Whether the natural cavern has floor and no entity at a tile.</summary>
    private bool IsCavernOpen(MouthContext context, Vector2i index)
    {
        var biome = context.Level.Comp1;

        return _biome.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var tile)
               && !_biome.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, NoGrid, out _);
    }

    /// <summary>Nothing pinned, anchored or loaded under the footprint or pad, and no grid nearby: a stamp can't cut into anything.</summary>
    private bool CanStamp(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i origin)
    {
        var size = context.Spec.HoleSize;
        var groundBiome = (context.Ground.Owner, context.Ground.Comp1);
        var levelBiome = (context.Level.Owner, context.Level.Comp1);

        foreach (var index in Footprint(origin, size))
        {
            if (_biome.WfIsPinned(groundBiome, index)
                || _biome.WfIsChunkLoaded(groundBiome, index)
                || _map.GetAnchoredEntitiesEnumerator(context.Ground.Owner, context.Ground.Comp2, index).MoveNext(out _))
                return false;
        }

        foreach (var index in Pad(origin, size, context.Spec.PadRadius))
        {
            if (_biome.WfIsChunkLoaded(levelBiome, index))
                return false;
        }

        var footprint = new Box2(origin - Vector2i.One, origin + new Vector2i(size + 1, size + 1));
        var grids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(Comp<MapComponent>(ground).MapId, footprint.Enlarged(GridClearance), ref grids,
            approx: true, includeMap: false);

        return grids.Count == 0;
    }

    /// <summary>Cuts the mouth: ring and hole on the ground, the pad in the cavern, then shades, rim and climb point.</summary>
    // One SetTiles per map. Pinned tiles skip tile, entity and decal generation, so this holds on unloaded chunks too.
    private WFCavernMouth Stamp(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2i origin, WFCavernMouthKind kind)
    {
        var spec = context.Spec;
        var size = spec.HoleSize;
        var groundGrid = (context.Ground.Owner, context.Ground.Comp2);
        var levelGrid = (context.Level.Owner, context.Level.Comp2);
        var groundBiome = context.Ground.Comp1;
        var levelBiome = context.Level.Comp1;

        ClearBiomeEntities(context.Ground, Footprint(origin, size));
        ClearBiomeEntities(context.Level, Pad(origin, size, spec.PadRadius));

        var groundTiles = new List<(Vector2i, Tile)>();
        var pinned = new List<Vector2i>();

        foreach (var index in Ring(origin, size))
        {
            pinned.Add(index);

            if (_map.TryGetTileRef(context.Ground.Owner, context.Ground.Comp2, index, out var existing) && !existing.Tile.IsEmpty)
                continue;

            if (_biome.TryGetTile(index, groundBiome.Layers, groundBiome.Seed, NoGrid, out var natural))
                groundTiles.Add((index, natural.Value));
        }

        foreach (var index in Hole(origin, size))
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
        var pad = new List<Vector2i>();
        var outline = new WFCavernMouth(origin, size, origin, kind);

        foreach (var index in Pad(origin, size, spec.PadRadius))
        {
            pad.Add(index);

            if (outline.Contains(index) || !_biome.TryGetTile(index, levelBiome.Layers, levelBiome.Seed, NoGrid, out var natural))
                padTiles.Add((index, landing));
            else
                padTiles.Add((index, natural.Value));
        }

        _map.SetTiles(context.Level.Owner, context.Level.Comp2, padTiles);
        _biome.WfPinTiles((context.Level.Owner, levelBiome), pad);

        var air = WFCavernAirClassifier.Classify(_proto.Index(context.Cavern.Level).Atmosphere);
        var landingMultiplier = ((ContentTileDefinition) _tileDefs[spec.LandingTile]).FallDamageMultiplier;

        foreach (var index in Hole(origin, size))
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

        var climbTile = ClimbTile(origin, size, spec.ClimbSide);

        if (spec.Rim.Count > 0)
        {
            var corners = RimCorners(origin, size, climbTile);
            for (var i = 0; i < corners.Count; i++)
            {
                SpawnAnchored(spec.Rim[i % spec.Rim.Count], groundGrid, corners[i]);
            }
        }

        if (!ground.Comp.ClimbPoints.ContainsKey(climbTile))
        {
            var climb = SpawnAnchored(spec.ClimbPoint, levelGrid, climbTile);
            var climbComp = EnsureComp<WFCavernClimbComponent>(climb);
            climbComp.Delay = spec.ClimbSeconds * Math.Clamp(context.Surface.Gravity, 1f, 2.5f);
            Dirty(climb, climbComp);
            ground.Comp.ClimbPoints[climbTile] = climb;
        }

        var mouth = new WFCavernMouth(origin, size, climbTile, kind);
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

    /// <summary>Spawns an entity on a tile and anchors it there.</summary>
    private EntityUid SpawnAnchored(EntProtoId proto, Entity<MapGridComponent> grid, Vector2i index)
    {
        var uid = Spawn(proto, _map.GridTileToLocal(grid, grid.Comp, index));
        var xform = Transform(uid);

        if (!xform.Anchored && !_transform.AnchorEntity((uid, xform), grid, index))
            Log.Error($"Could not anchor {ToPrettyString(uid)} on {ToPrettyString(grid)} at {index}.");

        return uid;
    }

    /// <summary>The lip tile beside the hole's bottom-left on the climb side: the tile over the climb point.</summary>
    public static Vector2i ClimbTile(Vector2i origin, int size, Direction side)
    {
        var step = side.ToIntVec();
        return origin + new Vector2i(step.X > 0 ? size : step.X, step.Y > 0 ? size : step.Y);
    }

    /// <summary>Up to two ring corners that don't touch the climb tile, farthest from it first.</summary>
    private static List<Vector2i> RimCorners(Vector2i origin, int size, Vector2i climbTile)
    {
        var corners = new List<Vector2i>
        {
            origin + new Vector2i(-1, -1),
            origin + new Vector2i(size, -1),
            origin + new Vector2i(-1, size),
            origin + new Vector2i(size, size),
        };

        corners.RemoveAll(corner => Math.Abs(corner.X - climbTile.X) <= 1 && Math.Abs(corner.Y - climbTile.Y) <= 1);
        corners.Sort((a, b) => (b - climbTile).LengthSquared.CompareTo((a - climbTile).LengthSquared));

        if (corners.Count > 2)
            corners.RemoveRange(2, corners.Count - 2);

        return corners;
    }

    /// <summary>The hole's tiles.</summary>
    public static IEnumerable<Vector2i> Hole(Vector2i origin, int size)
    {
        for (var x = 0; x < size; x++)
        for (var y = 0; y < size; y++)
        {
            yield return origin + new Vector2i(x, y);
        }
    }

    /// <summary>The one-tile lip around the hole.</summary>
    public static IEnumerable<Vector2i> Ring(Vector2i origin, int size)
    {
        for (var x = -1; x <= size; x++)
        for (var y = -1; y <= size; y++)
        {
            if (x >= 0 && x < size && y >= 0 && y < size)
                continue;

            yield return origin + new Vector2i(x, y);
        }
    }

    /// <summary>The hole and its lip.</summary>
    public static IEnumerable<Vector2i> Footprint(Vector2i origin, int size)
    {
        for (var x = -1; x <= size; x++)
        for (var y = -1; y <= size; y++)
        {
            yield return origin + new Vector2i(x, y);
        }
    }

    /// <summary>The cavern pad: the tiles under the hole and <paramref name="radius"/> tiles around them.</summary>
    public static IEnumerable<Vector2i> Pad(Vector2i origin, int size, int radius)
    {
        for (var x = -radius; x < size + radius; x++)
        for (var y = -radius; y < size + radius; y++)
        {
            yield return origin + new Vector2i(x, y);
        }
    }
}
