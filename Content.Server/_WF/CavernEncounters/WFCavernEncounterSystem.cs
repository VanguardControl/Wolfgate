using System.Linq;
using System.Numerics;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server._WF.WildSlimes;
using Content.Server.Light.EntitySystems;
using Content.Server.Parallax;
using Content.Shared._WF.CavernEncounters;
using Content.Shared._WF.Caverns;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// Plans a few encounter spots in each cavern from its seed, spaced well apart, and lays one out when a player first
/// comes near: a room cleared of rock in the cavern's own floor, its props and its people or creatures.
/// </summary>
public sealed partial class WFCavernEncounterSystem : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private HandheldLightSystem _handheld = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCavernSampler _sampler = default!;
    [Dependency] private WFPlanetBoundsSystem _bounds = default!;

    /// <summary>A player this close to a planned spot, on its cavern, has it laid out.</summary>
    public const float ActivateRange = 48f;

    /// <summary>The least distance between two spots of one cavern.</summary>
    public const float MinSpacing = 80f;

    /// <summary>No spot within this of the planet centre, where the gate mouth comes down.</summary>
    public const float CentreClearance = 40f;

    /// <summary>Tiles between a room's edge and any mouth's hole or climb point.</summary>
    public const float MouthClearance = 12f;

    /// <summary>Tiles between a room's edge and a slime grotto's patch, which the grotto spreads past.</summary>
    public const float GrottoClearance = 10f;

    /// <summary>Tiles a spot keeps in from the world's edge.</summary>
    public const float EdgeMargin = 32f;

    /// <summary>Tiles past the room's edge read for the tunnel it must open onto.</summary>
    public const int TunnelReach = 3;

    /// <summary>How many open natural tiles must ring a room for it to sit on the tunnel web.</summary>
    public const int TunnelTilesNeeded = 6;

    /// <summary>Rock a collapse rings its centre with when the cavern's own walls can't be read.</summary>
    public const string FallbackRock = "WallRock";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>Offsets tried round a planned spot that doesn't fit, nearest first.</summary>
    private static readonly Vector2i[] Shifts = BuildShifts();

    private TimeSpan _nextCheck;
    private readonly List<(EntityUid Map, Vector2 Position)> _players = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // After the cavern system, which links the ground to its cavern, and the slimes, which find their grottos.
        SubscribeLocalEvent<WFPlanetNetworkBuiltEvent>(OnNetworkBuilt,
            after: new[] { typeof(WFCavernSystem), typeof(WFWildSlimeSystem) });
    }

    private void OnNetworkBuilt(ref WFPlanetNetworkBuiltEvent args)
    {
        var surface = args.Surface.ID;
        var table = _proto.EnumeratePrototypes<WFCavernEncounterTablePrototype>().FirstOrDefault(t => t.Surface == surface);
        if (table == null)
            return;

        var centre = CompOrNull<WFPlanetNetworkComponent>(args.Network)?.Centre ?? Vector2.Zero;
        foreach (var map in args.Lower)
        {
            if (HasComp<WFCavernLayerComponent>(map))
                Plan(map, table, centre, args.Surface.Radius);
        }
    }

    /// <summary>
    /// Picks the cavern's encounters by weight without repeats and a spot for each from the cavern's seed: in the ring
    /// between the centre's clearance and the world's edge, each <see cref="MinSpacing"/> from the rest.
    /// </summary>
    public void Plan(EntityUid cavern, WFCavernEncounterTablePrototype table, Vector2 centre, int surfaceRadius)
    {
        var layer = EnsureComp<WFCavernEncounterLayerComponent>(cavern);
        layer.Sites.Clear();

        var seed = CompOrNull<BiomeComponent>(cavern)?.Seed ?? 0;
        var random = new System.Random(unchecked(seed * 31 + 0x5ca7e));

        var radius = _bounds.TryGetBounds(cavern, out var boundsCentre, out var boundsRadius)
            ? boundsRadius
            : surfaceRadius > 0 ? surfaceRadius : 256f;
        if (boundsRadius > 0f)
            centre = boundsCentre;

        var outer = radius - EdgeMargin;
        if (outer <= CentreClearance)
            return;

        var pool = table.Encounters.Where(e => e.Value > 0f).ToList();
        var placed = new List<Vector2>();

        for (var i = 0; i < table.Count && pool.Count > 0; i++)
        {
            var pick = PickWeighted(random, pool);
            pool.RemoveAll(e => e.Key == pick);

            for (var attempt = 0; attempt < 64; attempt++)
            {
                var angle = random.NextDouble() * Math.PI * 2;
                // Uniform over the ring's area.
                var inner2 = CentreClearance * CentreClearance;
                var distance = MathF.Sqrt((float) (inner2 + random.NextDouble() * (outer * outer - inner2)));
                var spot = centre + new Vector2(MathF.Cos((float) angle), MathF.Sin((float) angle)) * distance;

                if (placed.Any(other => Vector2.Distance(other, spot) < MinSpacing))
                    continue;

                placed.Add(spot);
                layer.Sites.Add(new WFCavernEncounterSite
                {
                    Encounter = pick,
                    Planned = new Vector2i((int) MathF.Floor(spot.X), (int) MathF.Floor(spot.Y)),
                });
                break;
            }
        }
    }

    private static ProtoId<WFCavernEncounterPrototype> PickWeighted(System.Random random, List<KeyValuePair<ProtoId<WFCavernEncounterPrototype>, float>> pool)
    {
        var total = pool.Sum(e => e.Value);
        var roll = random.NextDouble() * total;
        foreach (var (id, weight) in pool)
        {
            roll -= weight;
            if (roll <= 0)
                return id;
        }

        return pool[^1].Key;
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        _players.Clear();
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid is { } map)
                _players.Add((map, _transform.GetWorldPosition(xform)));
        }

        if (_players.Count == 0)
            return;

        var layers = EntityQueryEnumerator<WFCavernEncounterLayerComponent>();
        while (layers.MoveNext(out var cavern, out var layer))
        {
            foreach (var site in layer.Sites)
            {
                if (site.State != WFCavernEncounterState.Planned)
                    continue;

                var spot = new Vector2(site.Planned.X + 0.5f, site.Planned.Y + 0.5f);
                if (_players.Any(p => p.Map == cavern && Vector2.Distance(p.Position, spot) <= ActivateRange))
                    TryActivate(cavern, site);
            }
        }
    }

    /// <summary>The spots planned for a cavern, if any.</summary>
    public IReadOnlyList<WFCavernEncounterSite> GetSites(EntityUid cavern)
    {
        return CompOrNull<WFCavernEncounterLayerComponent>(cavern)?.Sites ?? (IReadOnlyList<WFCavernEncounterSite>) Array.Empty<WFCavernEncounterSite>();
    }

    /// <summary>Lays out a planned encounter on its spot, or the nearest shift of it that fits; skips it if none does.</summary>
    public bool TryActivate(EntityUid cavern, WFCavernEncounterSite site)
    {
        if (site.State != WFCavernEncounterState.Planned || !_proto.TryIndex(site.Encounter, out var encounter))
            return false;

        foreach (var shift in Shifts)
        {
            var centre = site.Planned + shift;
            if (!Fits(cavern, centre, encounter.Radius))
                continue;

            site.Centre = centre;
            site.Spawned = SpawnEncounter(cavern, encounter, centre);
            site.State = WFCavernEncounterState.Spawned;
            return true;
        }

        site.State = WFCavernEncounterState.Skipped;
        Log.Debug($"No room for {encounter.ID} near {site.Planned} in {ToPrettyString(cavern)}.");
        return false;
    }

    /// <summary>
    /// Whether a room of this radius may go here: inside the world, clear of every mouth, climb point, slime grotto and
    /// anything pinned on either map (other rooms, mouth pads, stairs, sites and their cellars), with nothing built in
    /// it, and opening onto the tunnels.
    /// </summary>
    public bool Fits(EntityUid cavern, Vector2i centre, int radius)
    {
        if (!TryComp<WFCavernLayerComponent>(cavern, out var layer)
            || !TryComp<BiomeComponent>(cavern, out var biome)
            || !TryComp<MapGridComponent>(cavern, out var grid))
            return false;

        var middle = new Vector2(centre.X + 0.5f, centre.Y + 0.5f);

        if (_bounds.TryGetBounds(cavern, out var boundsCentre, out var boundsRadius)
            && Vector2.Distance(middle, boundsCentre) > boundsRadius - radius - 4)
            return false;

        var ground = layer.Ground;
        var groundBiome = CompOrNull<BiomeComponent>(ground);

        if (TryComp<WFCavernGroundComponent>(ground, out var cavernGround))
        {
            foreach (var mouth in cavernGround.Mouths)
            {
                if (mouth.Footprint.Any(tile => Vector2.Distance(middle, new Vector2(tile.X + 0.5f, tile.Y + 0.5f)) < radius + MouthClearance))
                    return false;
            }

            foreach (var climb in cavernGround.ClimbPoints.Keys)
            {
                if (Vector2.Distance(middle, new Vector2(climb.X + 0.5f, climb.Y + 0.5f)) < radius + MouthClearance)
                    return false;
            }
        }

        if (TryComp<WFWildSlimeGroundComponent>(ground, out var slimes))
        {
            foreach (var patch in slimes.Patches)
            {
                if (Vector2.Distance(middle, patch.Middle) < patch.Radius + radius + GrottoClearance
                    || patch.Grotto.Any(tile => Vector2.Distance(middle, new Vector2(tile.X + 0.5f, tile.Y + 0.5f)) < radius + 3))
                    return false;
            }
        }

        foreach (var tile in Disc(centre, radius + 1))
        {
            if (_biome.WfIsPinned((cavern, biome), tile)
                || groundBiome != null && _biome.WfIsPinned((ground, groundBiome), tile))
                return false;

            foreach (var uid in _map.GetAnchoredEntities(cavern, grid, tile))
            {
                if (!_biome.WfIsBiomeSpawned((cavern, biome), uid, tile))
                    return false;
            }
        }

        // The room must open onto the tunnels: enough open natural floor just past its edge.
        var reach = radius + TunnelReach;
        var sample = _sampler.Sample(biome, centre, reach * 2 + 1);
        var open = 0;
        for (var i = 0; i < sample.Count; i++)
        {
            var offset = sample.IndexOf(i) - centre;
            var distance = MathF.Sqrt(offset.X * offset.X + offset.Y * offset.Y);
            if (distance > radius + 0.5f && distance <= reach && sample.IsWalkable(i))
                open++;
        }

        return open >= TunnelTilesNeeded;
    }

    /// <summary>
    /// Clears and pins a room at a centre without any checks, then lays the encounter out in it; returns what it
    /// spawned. Admins and tests call it directly.
    /// </summary>
    public List<EntityUid> SpawnEncounter(EntityUid cavern, WFCavernEncounterPrototype encounter, Vector2i centre, int? seed = null)
    {
        var spawned = new List<EntityUid>();
        if (!TryComp<BiomeComponent>(cavern, out var biome) || !TryComp<MapGridComponent>(cavern, out var grid))
            return spawned;

        var random = new System.Random(seed ?? unchecked(biome.Seed ^ (centre.X * 73856093) ^ (centre.Y * 19349663)));
        var rock = WallRockOf((cavern, biome), centre, encounter.Radius + TunnelReach);
        var room = Disc(centre, encounter.Radius).ToList();

        ClearRoom((cavern, biome, grid), room);

        var free = room.Where(t => (t - centre).Length <= encounter.Radius - 1).ToHashSet();
        var collapse = new List<EntityUid>();

        if (encounter.Collapse)
        {
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                var tile = centre + new Vector2i(x, y);
                var stone = Spawn(rock, _map.GridTileToLocal(cavern, grid, tile));
                EnsureComp<WFCavernCollapseRockComponent>(stone);
                collapse.Add(stone);
                spawned.Add(stone);
                free.Remove(tile);
            }
        }

        foreach (var entry in encounter.Entries)
        {
            var count = random.Next(entry.Min, entry.Max + 1);
            for (var i = 0; i < count; i++)
            {
                string? id = null;
                if (entry.Pending != null && _proto.HasIndex<EntityPrototype>(entry.Pending))
                    id = entry.Pending;
                else if (entry.Ids.Count > 0)
                    id = entry.Ids[random.Next(entry.Ids.Count)];

                if (id == null || !TryPickTile(random, centre, entry.Distance, free, out var tile))
                    continue;

                free.Remove(tile);
                var uid = Spawn(id, _map.GridTileToLocal(cavern, grid, tile));
                spawned.Add(uid);

                if (entry.Lit && TryComp<HandheldLightComponent>(uid, out var light))
                    _handheld.TurnOn(uid, (uid, light));

                if (entry.Table is { } table)
                {
                    foreach (var dir in new[] { Direction.South, Direction.East, Direction.West, Direction.North })
                    {
                        var ahead = tile + dir.ToIntVec();
                        if (!free.Remove(ahead))
                            continue;

                        spawned.Add(Spawn(table, _map.GridTileToLocal(cavern, grid, ahead)));
                        _transform.SetLocalRotation(uid, dir.ToAngle());
                        break;
                    }
                }
            }
        }

        foreach (var uid in spawned)
        {
            if (!TryComp<WFTrappedMinerComponent>(uid, out var miner))
                continue;

            foreach (var stone in collapse)
            {
                miner.Rocks.Add(stone);
                Comp<WFCavernCollapseRockComponent>(stone).Miner = uid;
            }
        }

        return spawned;
    }

    /// <summary>Finds a free tile for an entry: the centre itself for a distance of 0, else any free tile within the distance.</summary>
    private static bool TryPickTile(System.Random random, Vector2i centre, float distance, HashSet<Vector2i> free, out Vector2i tile)
    {
        if (distance <= 0f && free.Contains(centre))
        {
            tile = centre;
            return true;
        }

        var candidates = free.Where(t => t != centre && (t - centre).Length <= Math.Max(distance, 1.5f)).ToList();
        if (candidates.Count == 0)
            candidates = free.ToList();

        if (candidates.Count == 0)
        {
            tile = default;
            return false;
        }

        // A sort first, so the pick doesn't depend on the set's order.
        candidates.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        tile = candidates[random.Next(candidates.Count)];
        return true;
    }

    /// <summary>Lays the natural floor on every room tile, removes what the biome grew there and pins the tiles.</summary>
    private void ClearRoom(Entity<BiomeComponent, MapGridComponent> cavern, List<Vector2i> room)
    {
        var fallback = WFCavernSampler.TunnelFloor(cavern.Comp1.Layers);
        var tiles = new List<(Vector2i, Tile)>();
        var doomed = new List<EntityUid>();

        foreach (var index in room)
        {
            if (_biome.TryGetTile(index, cavern.Comp1.Layers, cavern.Comp1.Seed, (Entity<MapGridComponent>?) null, out var natural))
                tiles.Add((index, natural.Value));
            else if (fallback != null)
                tiles.Add((index, new Tile(_tileDefs[fallback].TileId)));

            foreach (var anchored in _map.GetAnchoredEntities(cavern.Owner, cavern.Comp2, index))
            {
                if (_biome.WfIsBiomeSpawned((cavern.Owner, cavern.Comp1), anchored, index))
                    doomed.Add(anchored);
            }
        }

        foreach (var uid in doomed)
        {
            Del(uid);
        }

        _map.SetTiles(cavern.Owner, cavern.Comp2, tiles);
        _biome.WfPinTiles((cavern.Owner, cavern.Comp1), room);
    }

    /// <summary>The cavern's commonest plain rock wall round a spot, read from its noise: the walls a collapse is made of.</summary>
    private string WallRockOf(Entity<BiomeComponent> cavern, Vector2i centre, int reach)
    {
        var sample = _sampler.Sample(cavern.Comp, centre, reach * 2 + 1);
        var counts = new Dictionary<string, int>();

        for (var i = 0; i < sample.Count; i++)
        {
            if (sample.Entities[i] is { } entity && sample.Solid[i] && !sample.Vein[i])
                counts[entity] = counts.GetValueOrDefault(entity) + 1;
        }

        return counts.Count == 0 ? FallbackRock : counts.MaxBy(c => c.Value).Key;
    }

    /// <summary>Every tile whose centre lies within a radius of a centre tile's.</summary>
    public static IEnumerable<Vector2i> Disc(Vector2i centre, int radius)
    {
        for (var x = -radius; x <= radius; x++)
        for (var y = -radius; y <= radius; y++)
        {
            if (x * x + y * y <= radius * radius + radius)
                yield return centre + new Vector2i(x, y);
        }
    }

    private static Vector2i[] BuildShifts()
    {
        var shifts = new List<Vector2i> { Vector2i.Zero };
        foreach (var step in new[] { 10, 20, 30 })
        {
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (x != 0 || y != 0)
                    shifts.Add(new Vector2i(x, y) * step);
            }
        }

        return shifts.ToArray();
    }
}
