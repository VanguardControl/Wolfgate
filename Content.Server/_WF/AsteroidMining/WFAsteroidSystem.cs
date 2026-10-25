using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.Spawning;
using Content.Server._WF.DroneRings;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Gatherable;
using Content.Server.Worldgen.Systems.Debris;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Mining;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.AsteroidMining;

/// <summary>How a rock broke, which sets how much of it survives.</summary>
public enum WFAsteroidCrack : byte
{
    Clean,
    Overcharge,
    Shatter,
}

/// <summary>
/// Builds asteroid grids, rolls what each rock holds, and breaks rocks into smaller asteroids and ore.
/// Cracks are queued and run in <see cref="Update"/>, since one deletes the rock's whole grid.
/// </summary>
public sealed partial class WFAsteroidSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SpawnCountSystem _spawnCount = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private DebrisFeaturePlacerSystem _debris = default!;

    private static readonly SoundSpecifier CrackSound = new SoundPathSpecifier("/Audio/Effects/break_stone.ogg",
        AudioParams.Default.WithVolume(4f).WithMaxDistance(40f));

    private readonly List<(EntityUid Rock, WFAsteroidCrack Kind)> _cracks = new();
    private readonly List<(EntityUid Rock, WFAsteroidCrack Kind)> _cracking = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private readonly List<EntityUid> _clusters = new();
    // Swapped debris: the mass budget for the cluster about to spawn at each point.
    private readonly Dictionary<MapCoordinates, float> _budgets = new();
    private Dictionary<string, (WFAsteroidReplacementKind Kind, float Mass)>? _replacements;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFAsteroidGridComponent, MapInitEvent>(OnGridMapInit);
        SubscribeLocalEvent<WFAsteroidComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<WFAsteroidComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFAsteroidComponent, GatheredEvent>(OnGathered);
        SubscribeLocalEvent<WFDebrisPlacingEvent>(OnDebrisPlacing, after: new[] { typeof(WFDroneHeldSpaceSystem) });
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _replacements = null);
    }

    /// <summary>Swaps an ore tile asteroid for a cluster of single rocks with the same kind and amount of ore.</summary>
    private void OnDebrisPlacing(ref WFDebrisPlacingEvent args)
    {
        if (args.Cancelled)
            return;

        _replacements ??= BuildReplacements();
        if (!_replacements.TryGetValue(args.Proto, out var replacement) ||
            replacement.Kind.KeepChance > 0f && _random.Prob(replacement.Kind.KeepChance))
            return;

        args.Proto = replacement.Kind.Cluster;
        _budgets[args.At] = replacement.Mass;
    }

    private Dictionary<string, (WFAsteroidReplacementKind, float)> BuildReplacements()
    {
        var map = new Dictionary<string, (WFAsteroidReplacementKind, float)>();
        foreach (var proto in _proto.EnumeratePrototypes<WFAsteroidReplacementPrototype>())
        {
            foreach (var (prefix, kind) in proto.Kinds)
            {
                foreach (var (size, mass) in proto.Sizes)
                {
                    if (_proto.HasIndex<EntityPrototype>(prefix + size))
                        map[prefix + size] = (kind, mass);
                }
            }
        }

        return map;
    }

    private void OnGridMapInit(Entity<WFAsteroidGridComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<MapGridComponent>(ent, out var grid) ||
            !_proto.Index(ent.Comp.Rock).TryComp<WFAsteroidComponent>(out var rockProto, _factory))
        {
            Log.Error($"Asteroid grid {ToPrettyString(ent)} has no grid or its rock {ent.Comp.Rock} is not an asteroid.");
            return;
        }

        // Tile centres within the radius of the rock, which sits on the centre of tile (0, 0).
        var tile = new Tile(_tileDefs[ent.Comp.Tile].TileId);
        var radius = rockProto.Radius;
        var bound = (int) MathF.Ceiling(radius);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -bound; x <= bound; x++)
        {
            for (var y = -bound; y <= bound; y++)
            {
                if (x * x + y * y <= radius * radius)
                    tiles.Add((new Vector2i(x, y), tile));
            }
        }

        _map.SetTiles(ent, grid, tiles);
        var rock = SpawnAttachedTo(ent.Comp.Rock, new EntityCoordinates(ent, 0.5f, 0.5f));
        ent.Comp.RockUid = rock;
        if (ent.Comp.Composition is { } composition)
            Roll(rock, composition);
    }

    /// <summary>Rolls a rock's mass, ores, resistance and cracking window from a composition.</summary>
    public void Roll(EntityUid rock, ProtoId<WFAsteroidCompositionPrototype> id, WFAsteroidComponent? comp = null)
    {
        if (!Resolve(rock, ref comp) || !_proto.TryIndex(id, out var proto))
            return;

        comp.CompositionId = id;
        comp.Mass = _random.NextFloat(comp.MinMass, comp.MaxMass);
        var inert = _random.NextFloat(proto.MinInert, proto.MaxInert);
        var pool = proto.Ores.ToList();
        var count = Math.Clamp(_random.Next(proto.MinOres, proto.MaxOres + 1), 1, Math.Max(1, pool.Count));
        var picked = new List<(WFAsteroidCompositionEntry Entry, float Share)>();
        for (var i = 0; i < count && pool.Count > 0; i++)
        {
            var entry = PickWeighted(pool);
            pool.Remove(entry);
            // The first pick leads the rock, so a rare ore can still dominate one.
            picked.Add((entry, _random.NextFloat(0.3f, 1f) * (i == 0 ? 2f : 1f)));
        }

        var total = picked.Sum(p => p.Share);
        comp.Ores.Clear();
        foreach (var (entry, share) in picked)
        {
            comp.Ores.Add(new WFAsteroidOre(entry.Ore, (1f - inert) * share / total, entry.Color, entry.Volatility));
        }

        comp.Ores.Sort((a, b) => b.Fraction.CompareTo(a.Fraction));
        comp.Resistance = Math.Clamp(_random.NextFloat(proto.MinResistance, proto.MaxResistance) + comp.ResistanceBonus, 0f, 0.9f);
        comp.Instability = _random.NextFloat(proto.MinInstability, proto.MaxInstability);
        RollWindow(comp);
        Dirty(rock, comp);
    }

    private WFAsteroidCompositionEntry PickWeighted(List<WFAsteroidCompositionEntry> pool)
    {
        var roll = _random.NextFloat(pool.Sum(e => e.Weight));
        foreach (var entry in pool)
        {
            roll -= entry.Weight;
            if (roll <= 0f)
                return entry;
        }

        return pool[^1];
    }

    private void RollWindow(WFAsteroidComponent comp)
    {
        comp.WindowCenter = _random.NextFloat(0.45f, 0.65f);
        comp.WindowHalfWidth = 0.12f * (1f - 0.5f * comp.Instability);
        comp.NoiseSeed = _random.NextFloat(0f, 100f);
        comp.Variant = _random.Next(Math.Max(1, comp.Variants));
        comp.VeinColor = comp.Ores.Count > 0 ? comp.Ores[0].Color : Color.Gray;
    }

    /// <summary>Gives a fragment its parent's ores and stability with a new mass and window.</summary>
    private void Inherit(WFAsteroidComponent parent, EntityUid child, float mass)
    {
        if (!TryComp<WFAsteroidComponent>(child, out var comp))
            return;

        comp.CompositionId = parent.CompositionId;
        comp.Mass = mass;
        comp.Ores = new List<WFAsteroidOre>(parent.Ores);
        comp.Resistance = Math.Clamp(parent.Resistance - parent.ResistanceBonus + comp.ResistanceBonus, 0f, 0.9f);
        comp.Instability = parent.Instability;
        RollWindow(comp);
        Dirty(child, comp);
    }

    private void OnDamaged(Entity<WFAsteroidComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased && args.Damageable.TotalDamage >= ent.Comp.ShatterDamage)
            QueueCrack(ent, WFAsteroidCrack.Shatter, ent.Comp);
    }

    private void OnExamined(Entity<WFAsteroidComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("wf-asteroid-examine-scan"));
    }

    /// <summary>The share of the window time a rock needs to crack.</summary>
    public static float CrackTime(WFAsteroidComponent comp)
    {
        return comp.CrackTimeBase + comp.Mass * comp.CrackTimePerMass;
    }

    /// <summary>Breaks the rock on the next update.</summary>
    public void QueueCrack(EntityUid rock, WFAsteroidCrack kind, WFAsteroidComponent? comp = null)
    {
        if (!Resolve(rock, ref comp) || comp.Cracking)
            return;

        comp.Cracking = true;
        _cracks.Add((rock, kind));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        SpawnClusters();

        if (_cracks.Count == 0)
            return;

        _cracking.Clear();
        _cracking.AddRange(_cracks);
        _cracks.Clear();
        foreach (var (rock, kind) in _cracking)
        {
            if (!TerminatingOrDeleted(rock) && TryComp<WFAsteroidComponent>(rock, out var comp))
                Crack(rock, comp, kind);
        }
    }

    private void Crack(EntityUid rock, WFAsteroidComponent comp, WFAsteroidCrack kind)
    {
        var xform = Transform(rock);
        if (xform.MapUid is not { } mapUid)
            return;

        var center = _transform.GetMapCoordinates(rock, xform);
        var rotation = _transform.GetWorldRotation(xform);
        // A boulder hauled onto a ship sits on the ship's grid; only ever delete a grid built for the rock.
        var grid = xform.GridUid is { } onGrid && HasComp<WFAsteroidGridComponent>(onGrid) ? onGrid : (EntityUid?) null;
        var velocity = _physics.GetMapLinearVelocity(grid ?? rock);
        var controller = grid is { } owner ? _debris.GetDebrisOwner(owner) : null;
        var ores = comp.Ores;
        var yield = kind switch
        {
            WFAsteroidCrack.Clean => 1f,
            WFAsteroidCrack.Overcharge => comp.OverchargeYield,
            _ => comp.ShatterYield,
        };

        var fragmentMass = kind == WFAsteroidCrack.Shatter || comp.FragmentPrototype == null
            ? 0f
            : comp.Mass * yield * comp.FragmentShare;
        var oreMass = comp.Mass * yield - fragmentMass;
        var volatility = ores.Sum(o => o.Fraction * o.Volatility);
        var radius = comp.Radius;
        var mass = comp.Mass;

        // Remove the rock and its grid first, so nothing spawned below lands on a grid that is going away.
        // Anyone or anything else on the grid stays behind in space.
        if (grid is { } asteroidGrid)
            ReleasePassengers(asteroidGrid, rock, mapUid);
        Del(grid ?? rock);
        _audio.PlayPvs(CrackSound, new EntityCoordinates(mapUid, center.Position));

        if (kind == WFAsteroidCrack.Overcharge)
        {
            // Reaches a little past the rock's surface; plasma-rich rocks reach much further.
            _explosion.QueueExplosion(center, "Default", 150f + mass * 3f + volatility * 1500f, 1.2f, 6f, null,
                maxTileBreak: 0, canCreateVacuum: false);
        }

        if (fragmentMass > 0f && comp.FragmentPrototype is { } fragment)
            oreMass += SpawnFragments(comp, fragment, fragmentMass, center, rotation, velocity, mapUid, controller);

        if (comp.BoulderPrototype is { } boulder)
            oreMass -= SpawnBoulders(comp, boulder, oreMass * comp.BoulderShare, center, mapUid);

        SpawnOre(ores, oreMass, center, radius);
    }

    /// <summary>Drops part of the ore as boulders for hand miners and returns the mass they took.</summary>
    private float SpawnBoulders(WFAsteroidComponent parent, EntProtoId boulder, float mass, MapCoordinates center,
        EntityUid mapUid)
    {
        if (!_proto.TryIndex(boulder, out var proto) || !proto.TryComp<WFAsteroidComponent>(out var boulderProto, _factory))
            return 0f;

        var typical = (boulderProto.MinMass + boulderProto.MaxMass) / 2f;
        var count = Math.Min(parent.MaxBoulders, (int) MathF.Floor(mass / MathF.Max(typical, 0.1f)));
        if (count <= 0)
            return 0f;

        for (var i = 0; i < count; i++)
        {
            // Prefer open space over a fragment's grid, so the boulder isn't lost when that fragment cracks.
            var position = center.Position;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                position = center.Position + _random.NextVector2(parent.Radius * 1.2f);
                if (!Blocked(center.MapId, position, 0.6f))
                    break;
            }

            var spawned = Spawn(boulder, new EntityCoordinates(mapUid, position));
            _transform.SetWorldRotation(spawned, _random.NextAngle());
            Inherit(parent, spawned, typical);
        }

        return count * typical;
    }

    /// <summary>Moves everything on an asteroid grid but its rock onto the map, so deleting the grid takes nothing else.</summary>
    private void ReleasePassengers(EntityUid grid, EntityUid rock, EntityUid mapUid)
    {
        var passengers = new List<EntityUid>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (child != rock)
                passengers.Add(child);
        }

        foreach (var passenger in passengers)
        {
            var xform = Transform(passenger);
            if (xform.Anchored)
                _transform.Unanchor(passenger, xform);
            _transform.SetParent(passenger, xform, mapUid);
        }
    }

    /// <summary>A pickaxe, drill or kinetic accelerator breaks a boulder into all of its ore.</summary>
    private void OnGathered(EntityUid uid, WFAsteroidComponent comp, GatheredEvent args)
    {
        if (comp.Cracking)
            return;

        comp.Cracking = true;
        SpawnOre(comp.Ores, comp.Mass, _transform.GetMapCoordinates(uid), MathF.Max(comp.Radius, 0.3f));
    }

    /// <summary>Spawns fragments around the old centre and returns the mass of any that had no room.</summary>
    private float SpawnFragments(WFAsteroidComponent parent, EntProtoId fragment, float mass, MapCoordinates center,
        Angle rotation, Vector2 velocity, EntityUid mapUid, EntityUid? controller)
    {
        if (!TryGetRock(fragment, out var child))
            return mass;

        var count = Math.Max(1, parent.FragmentCount);
        var each = mass / count;
        // Far enough apart that neighbouring fragments' tile discs, which reach past the radius by a tile corner, don't touch.
        var distance = count == 1 ? 0f : (child.Radius + 0.8f) / MathF.Sin(MathF.PI / count) * 1.05f;
        var start = _random.NextFloat(0f, MathF.Tau);
        var spots = new List<(Vector2 Position, Vector2 Direction)>();
        var lost = 0f;
        // Check every spot before spawning any, so a fragment never blocks its sibling.
        for (var i = 0; i < count; i++)
        {
            var angle = start + MathF.Tau * i / count;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var position = center.Position + direction * distance;
            if (Blocked(center.MapId, position, child.Radius + 0.5f))
                lost += each;
            else
                spots.Add((position, direction));
        }

        foreach (var (position, direction) in spots)
        {
            var spawned = Spawn(fragment, new EntityCoordinates(mapUid, position));
            _transform.SetWorldRotation(spawned, rotation + _random.NextFloat(-0.5f, 0.5f));
            _physics.SetLinearVelocity(spawned, velocity + direction * _random.NextFloat(0.3f, 0.7f));
            _physics.SetAngularVelocity(spawned, _random.NextFloat(-0.15f, 0.15f));
            if (TryComp<WFAsteroidGridComponent>(spawned, out var spawnedGrid) && spawnedGrid.RockUid is { } rock)
                Inherit(parent, rock, each);
            if (controller is { } owner)
                _debris.AdoptDebris(owner, spawned);
        }

        return lost;
    }

    /// <summary>The rock an asteroid grid prototype spawns.</summary>
    private bool TryGetRock(EntProtoId gridProto, [NotNullWhen(true)] out WFAsteroidComponent? rock)
    {
        rock = null;
        return _proto.TryIndex(gridProto, out var grid) &&
               grid.TryComp<WFAsteroidGridComponent>(out var builder, _factory) &&
               _proto.TryIndex(builder.Rock, out var rockProto) &&
               rockProto.TryComp(out rock, _factory);
    }

    private bool Blocked(MapId map, Vector2 position, float radius)
    {
        _grids.Clear();
        _map.FindGridsIntersecting(map, Box2.CenteredAround(position, new Vector2(radius * 2f)), ref _grids);
        return _grids.Count > 0;
    }

    /// <summary>Drops each ore as a few piles across where the rock was. Each unit of mass is one vein.</summary>
    private void SpawnOre(List<WFAsteroidOre> ores, float mass, MapCoordinates center, float radius)
    {
        foreach (var ore in ores)
        {
            if (!_proto.TryIndex(ore.Ore, out var proto) || proto.OreEntity is not { } entity)
                continue;

            var veins = mass * ore.Fraction;
            var average = (proto.MinOreYield + proto.MaxOreYield) / 2f;
            var total = (int) MathF.Round(veins * average * _random.NextFloat(0.9f, 1.1f));
            if (total <= 0)
                continue;

            var piles = Math.Clamp((int) MathF.Ceiling(veins / 4f), 1, Math.Min(6, total));
            for (var i = 0; i < piles; i++)
            {
                var amount = total / piles + (i < total % piles ? 1 : 0);
                var offset = _random.NextVector2(radius * 0.7f);
                var coords = _transform.ToCoordinates(new MapCoordinates(center.Position + offset, center.MapId));
                _spawnCount.SpawnCount(entity, coords, amount);
            }
        }
    }

    /// <summary>Fills each new cluster marker's budget with asteroids once worldgen has handed it to a chunk.</summary>
    private void SpawnClusters()
    {
        _clusters.Clear();
        var query = EntityQueryEnumerator<WFAsteroidClusterComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            _clusters.Add(uid);
        }

        foreach (var uid in _clusters)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            FillCluster(uid, Comp<WFAsteroidClusterComponent>(uid));
            RemComp<WFAsteroidClusterComponent>(uid);
            QueueDel(uid);
        }
    }

    /// <summary>Places the biggest rock the budget allows at the centre, then smaller ones around it until it runs out.</summary>
    private void FillCluster(EntityUid marker, WFAsteroidClusterComponent cluster)
    {
        var xform = Transform(marker);
        if (xform.MapUid is not { } mapUid)
            return;

        var center = _transform.GetMapCoordinates(xform);
        var budget = _budgets.Remove(center, out var swapped) ? swapped : cluster.Mass;
        var controller = _debris.GetDebrisOwner(marker);
        var sizes = new List<ClusterSize>();
        foreach (var (grid, weight) in cluster.Sizes)
        {
            if (TryGetRock(grid, out var rock))
                sizes.Add(new ClusterSize(grid, weight, (rock.MinMass + rock.MaxMass) / 2f, rock.Radius));
        }

        if (sizes.Count == 0)
            return;

        sizes.Sort((a, b) => a.Mass.CompareTo(b.Mass));
        var smallest = sizes[0].Mass;
        var spread = 10f + 2.6f * MathF.Sqrt(budget);
        var remaining = budget;
        var first = true;
        for (var attempt = 0; attempt < 60 && remaining >= smallest * 0.5f; attempt++)
        {
            var size = first ? Largest(sizes, MathF.Max(budget * 0.5f, smallest)) : PickSize(sizes, remaining);
            var position = first
                ? center.Position
                : center.Position + _random.NextAngle().ToVec() * spread * MathF.Sqrt(_random.NextFloat());
            first = false;
            if (Blocked(center.MapId, position, size.Radius + 1.5f))
                continue;

            var spawned = Spawn(size.Grid, new EntityCoordinates(mapUid, position));
            _transform.SetWorldRotation(spawned, _random.NextAngle());
            if (TryComp<WFAsteroidGridComponent>(spawned, out var spawnedGrid) && spawnedGrid.RockUid is { } rock &&
                TryComp<WFAsteroidComponent>(rock, out var rockComp))
            {
                Roll(rock, cluster.Composition, rockComp);
                // The last rock takes what is left rather than overshooting the budget.
                rockComp.Mass = MathF.Min(rockComp.Mass, MathF.Max(remaining, smallest * 0.5f));
                remaining -= rockComp.Mass;
            }

            if (controller is { } owner)
                _debris.AdoptDebris(owner, spawned);
        }
    }

    private readonly record struct ClusterSize(EntProtoId Grid, float Weight, float Mass, float Radius);

    private static ClusterSize Largest(List<ClusterSize> sizes, float mass)
    {
        var best = sizes[0];
        foreach (var size in sizes)
        {
            if (size.Mass <= mass)
                best = size;
        }

        return best;
    }

    private ClusterSize PickSize(List<ClusterSize> sizes, float remaining)
    {
        var fitting = sizes.Where(s => s.Mass <= remaining * 1.2f).ToList();
        if (fitting.Count == 0)
            return sizes[0];

        var roll = _random.NextFloat(fitting.Sum(s => s.Weight));
        foreach (var size in fitting)
        {
            roll -= size.Weight;
            if (roll <= 0f)
                return size;
        }

        return fitting[^1];
    }
}
