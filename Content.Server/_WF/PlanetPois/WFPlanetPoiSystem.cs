using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Parallax;
using Content.Server.Popups;
using Content.Server.Procedural;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetPois;

/// <summary>
/// Rolls a world's points of interest before its ground is preloaded: a procedural dungeon stamped straight onto the
/// ground at each site and pinned so the biome never grows over it, with an unidentified signal on the orbit layer
/// that names itself once someone on the ground walks up to it.
/// </summary>
public sealed partial class WFPlanetPoiSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private DungeonSystem _dungeon = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>The orbit-layer marker over a site.</summary>
    public const string SignalProto = "WFPoiSignal";

    /// <summary>How many origins are tried for each site wanted before giving up on it.</summary>
    private const int AttemptsPerSite = 24;

    private static readonly TimeSpan RevealInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextReveal;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetPreloadStartingEvent>(OnPreloadStarting);
    }

    private void OnPreloadStarting(ref WFPlanetPreloadStartingEvent args)
    {
        if (!TryComp<WFPlanetLayerComponent>(args.Ground, out var layer)
            || !TryGetEntity(layer.Network, out var network)
            || !TryComp<WFPlanetNetworkComponent>(network, out var comp)
            || !_proto.TryIndex(comp.Surface, out var surface))
            return;

        var forced = _cfg.GetCVar(PlanetCVars.PoiTable);
        var tableId = forced.Length > 0 ? new ProtoId<WFPlanetPoiTablePrototype>(forced) : surface.Pois;

        if (tableId is not { } id || !_proto.TryIndex(id, out var table))
            return;

        args.Holds.Add(PlaceAsync(args.Ground, comp.OrbitMap, args.Centre, args.Radius, table));
    }

    /// <summary>Picks the sites, then generates them one after another; the preload waits on the returned task.</summary>
    private async Task PlaceAsync(EntityUid ground, EntityUid orbit, Vector2 centre, float radius, WFPlanetPoiTablePrototype table)
    {
        if (!TryComp<BiomeComponent>(ground, out var biome) || !TryComp<MapGridComponent>(ground, out var grid))
            return;

        var origins = PickOrigins((ground, biome), centre, radius, table);
        var record = EnsureComp<WFPlanetPoiComponent>(ground);

        foreach (var origin in origins)
        {
            var entry = Pick(table);

            if (!_proto.TryIndex(entry.Dungeon, out var config))
            {
                Log.Error($"Point of interest table \"{table.ID}\" names unknown dungeon \"{entry.Dungeon}\".");
                continue;
            }

            var seed = _random.Next();
            var dungeons = await _dungeon.GenerateDungeonAsync(config, config.ID, ground, grid, origin, seed);

            if (TerminatingOrDeleted(ground))
                return;

            var tiles = new HashSet<Vector2i>();
            foreach (var dungeon in dungeons)
            {
                tiles.UnionWith(dungeon.AllTiles);
            }

            if (tiles.Count == 0)
            {
                Log.Warning($"{config.ID} at {origin} on {ToPrettyString(ground)} generated no tiles.");
                continue;
            }

            // The biome never lays its tiles or grows anything on the site, and the unloader never takes it.
            _biome.WfPinTiles((ground, biome), tiles);

            var bounds = BoundsOf(tiles);
            var middle = new Vector2i((bounds.Left + bounds.Right) / 2, (bounds.Bottom + bounds.Top) / 2);
            var signal = SpawnSignal(ground, orbit, middle, entry, table);
            record.Sites.Add(new WFPlanetPoiSite
            {
                Dungeon = entry.Dungeon,
                Name = entry.Name,
                Origin = origin,
                TileCount = tiles.Count,
                Bounds = bounds,
                Signal = signal,
            });

            Log.Info($"Placed {config.ID} ({tiles.Count} tiles) at {origin} on {ToPrettyString(ground)}, seed {seed}.");
        }
    }

    /// <summary>
    /// Random origins inside the circle, clear of the edge, the centre, each other and anything already pinned there,
    /// such as a cavern mouth; fewer than asked for when the world is too small or crowded.
    /// </summary>
    private List<Vector2i> PickOrigins(Entity<BiomeComponent> ground, Vector2 centre, float radius, WFPlanetPoiTablePrototype table)
    {
        var origins = new List<Vector2i>();
        var wanted = _random.Next(table.MinCount, table.MaxCount + 1);
        var reach = radius - table.EdgeMargin;

        if (reach <= table.CentreClear)
            return origins;

        for (var attempt = 0; attempt < wanted * AttemptsPerSite && origins.Count < wanted; attempt++)
        {
            // Uniform over the annulus, not clustered at the centre.
            var inner = table.CentreClear * table.CentreClear;
            var distance = MathF.Sqrt(_random.NextFloat(inner, reach * reach));
            var candidate = centre + _random.NextAngle().ToVec() * distance;
            var origin = new Vector2i((int) MathF.Floor(candidate.X), (int) MathF.Floor(candidate.Y));

            if (origins.Any(other => Vector2.DistanceSquared(other, origin) < table.Spacing * table.Spacing))
                continue;

            if (AnyPinned(ground, origin, table.Footprint))
                continue;

            origins.Add(origin);
        }

        return origins;
    }

    /// <summary>The smallest box holding every tile, inclusive of its far edges.</summary>
    private static Box2i BoundsOf(HashSet<Vector2i> tiles)
    {
        var box = new Box2i(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

        foreach (var tile in tiles)
        {
            box = new Box2i(Math.Min(box.Left, tile.X), Math.Min(box.Bottom, tile.Y), Math.Max(box.Right, tile.X), Math.Max(box.Top, tile.Y));
        }

        return box;
    }

    /// <summary>Whether anything in the square round an origin is already pinned.</summary>
    private bool AnyPinned(Entity<BiomeComponent> ground, Vector2i origin, int half)
    {
        for (var x = -half; x <= half; x++)
        for (var y = -half; y <= half; y++)
        {
            if (_biome.WfIsPinned(ground, origin + new Vector2i(x, y)))
                return true;
        }

        return false;
    }

    private WFPlanetPoiEntry Pick(WFPlanetPoiTablePrototype table)
    {
        var total = 0f;
        foreach (var entry in table.Entries)
        {
            total += Math.Max(entry.Weight, 0f);
        }

        var roll = _random.NextFloat(0f, total);
        foreach (var entry in table.Entries)
        {
            roll -= Math.Max(entry.Weight, 0f);
            if (roll <= 0f)
                return entry;
        }

        return table.Entries[^1];
    }

    /// <summary>The marker on the orbit layer over a site, unidentified until someone on the ground reaches it.</summary>
    private EntityUid SpawnSignal(EntityUid ground, EntityUid orbit, Vector2i origin, WFPlanetPoiEntry entry, WFPlanetPoiTablePrototype table)
    {
        if (TerminatingOrDeleted(orbit))
            return EntityUid.Invalid;

        var signal = Spawn(SignalProto, new EntityCoordinates(orbit, new Vector2(origin.X + 0.5f, origin.Y + 0.5f)));
        _meta.SetEntityName(signal, Loc.GetString("wf-poi-unknown-signal"));

        var comp = EnsureComp<WFPoiSignalComponent>(signal);
        comp.Ground = ground;
        comp.Origin = origin;
        comp.Name = entry.Name;
        comp.RevealRange = table.RevealRange;
        return signal;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextReveal)
            return;

        _nextReveal = _timing.CurTime + RevealInterval;

        var query = EntityQueryEnumerator<WFPoiSignalComponent>();
        while (query.MoveNext(out var uid, out var signal))
        {
            if (signal.Revealed)
                continue;

            var site = new Vector2(signal.Origin.X + 0.5f, signal.Origin.Y + 0.5f);

            foreach (var session in _players.Sessions)
            {
                if (session.AttachedEntity is not { } player
                    || Transform(player).MapUid != signal.Ground
                    || Vector2.DistanceSquared(_transform.GetWorldPosition(player), site) > signal.RevealRange * signal.RevealRange)
                    continue;

                Reveal((uid, signal), player);
                break;
            }
        }
    }

    /// <summary>Names the signal after its site and tells the finder.</summary>
    public void Reveal(Entity<WFPoiSignalComponent> signal, EntityUid? finder)
    {
        var name = Loc.GetString(signal.Comp.Name);
        signal.Comp.Revealed = true;
        _meta.SetEntityName(signal, name);

        if (finder is { } player)
            _popup.PopupEntity(Loc.GetString("wf-poi-signal-identified", ("name", name)), player, player);
    }
}
