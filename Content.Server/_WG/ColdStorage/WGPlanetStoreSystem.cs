
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.Contracts;
using Content.Server._WG.Dormancy;
using Content.Server._WG.Planets;
using Content.Server._WG.Planets.Hop;
using Content.Server._WG.Shipyard;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._WG.ColdStorage;

/// <summary>
/// Unloads a planet nobody is on. The world's layers, its terrain and the hulls standing on it stay; what stands on
/// them is saved into one file for the world and deleted, and built back, a few milliseconds a tick, when someone
/// heads for it. Anything that will not save (a mob and what it carries, and whatever holds it) stays where it is,
/// asleep. One world is worked on at a time.
/// </summary>
public sealed partial class WGPlanetStoreSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logs = default!;
    [Dependency] private IDependencyCollection _dependencies = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private UsedShipReinitSystem _reinit = default!;
    [Dependency] private WGColdStoreSystem _coldStore = default!;
    [Dependency] private WGGridStasisSystem _stasis = default!;
    [Dependency] private WGSiteStashSystem _stash = default!;
    [Dependency] private WGCatchUpSystem _catchUp = default!;
    [Dependency] private WGContractSystem _contracts = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private StationJobsSystem _stationJobs = default!;

    private static readonly TimeSpan LookEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ArrivalHold = TimeSpan.FromSeconds(120);

    /// <summary>Loads tried before an unloaded world is left for an admin.</summary>
    public const int MaxFailures = 3;

    private readonly Dictionary<EntityUid, WGStoredPlanet> _planets = new();
    private readonly Dictionary<EntityUid, TimeSpan> _arriving = new();
    private readonly List<EntityUid> _walk = new();
    private readonly System.Diagnostics.Stopwatch _timer = new();
    private ErrorCounter _serializerErrors = default!;

    private EntityQuery<MetaDataComponent> _metaQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<MobStateComponent> _mobQuery;

    private bool _enabled;
    private float _idle;
    private float _budgetMs;
    private float _urgentMs;
    private TimeSpan _nextLook;
    private WGStoredPlanet? _saving;

    /// <summary>The save whose slice is running on this thread, for the error counter.</summary>
    private WGStoredPlanet? _slicing;

    public IReadOnlyDictionary<EntityUid, WGStoredPlanet> Planets => _planets;

    private readonly HashSet<EntityUid> _away = new();

    /// <summary>
    /// Whether a layer map or a hull belongs to a world that is being unloaded, is unloaded or is being loaded.
    /// What stood on it is in the world's file, not gone: a system that would replace missing furniture waits.
    /// </summary>
    public bool Away(EntityUid mapOrHull) => _away.Contains(mapOrHull);

    /// <summary>Entities out of the world in unloaded planets.</summary>
    public int StoredEntities { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        _metaQuery = GetEntityQuery<MetaDataComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();

        Subs.CVar(_cfg, WGPlanetStoreCVars.Enabled, value => _enabled = value, true);
        Subs.CVar(_cfg, WGPlanetStoreCVars.Idle, value => _idle = value, true);
        Subs.CVar(_cfg, WGPlanetStoreCVars.BudgetMs, value => _budgetMs = value, true);
        Subs.CVar(_cfg, WGPlanetStoreCVars.UrgentMs, value => _urgentMs = value, true);

        _serializerErrors = new ErrorCounter(this);
        _logs.GetSawmill("entity_serializer").AddHandler(_serializerErrors);

        SubscribeLocalEvent<WGPlanetHopStartedEvent>(OnHopStarted);
        SubscribeLocalEvent<WGPlanetNetworkComponent, EntityTerminatingEvent>(OnNetworkTerminating);
        SubscribeLocalEvent<Content.Shared.GameTicking.RoundRestartCleanupEvent>(_ => Reset());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _logs.GetSawmill("entity_serializer").RemoveHandler(_serializerErrors);
    }

    private void Reset()
    {
        foreach (var planet in _planets.Values)
        {
            if (planet.Load != null)
                _loader.AbortLoad(planet.Load);
        }

        _planets.Clear();
        _away.Clear();
        _arriving.Clear();
        _saving = null;
        StoredEntities = 0;
    }

    private void OnNetworkTerminating(Entity<WGPlanetNetworkComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!_planets.Remove(ent.Owner, out var planet))
            return;

        if (planet.Load != null)
            _loader.AbortLoad(planet.Load);

        if (_saving == planet)
            _saving = null;

        if (planet.State is WGPlanetStoreState.Unloaded or WGPlanetStoreState.Loading)
            StoredEntities -= planet.StoredEntities;
    }

    /// <summary>A hull hopping to a world is someone on their way: the world counts as occupied from now.</summary>
    private void OnHopStarted(ref WGPlanetHopStartedEvent args)
    {
        var target = args.Target.EntityId;
        var map = HasComp<MapComponent>(target) ? target : Transform(target).MapUid;
        var networks = AllEntityQuery<WGPlanetNetworkComponent>();
        while (networks.MoveNext(out var uid, out var net))
        {
            if (map != net.OrbitMap && !net.Layers.Contains(map ?? EntityUid.Invalid) && map != net.GroundMap)
                continue;

            _arriving[uid] = _timing.CurTime + ArrivalHold;
            _nextLook = TimeSpan.Zero;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now >= _nextLook)
        {
            _nextLook = now + LookEvery;
            Look(now);
        }

        // Loads first: someone is waiting on them.
        var loading = false;
        foreach (var planet in _planets.Values)
        {
            if (planet.State != WGPlanetStoreState.Loading)
                continue;

            loading = true;
            StepLoading(planet);
            break;
        }

        if (!loading && _saving is { } saving)
            StepUnloading(saving);
    }

    /// <summary>Decides for every world whether it should be coming back, going away or staying as it is.</summary>
    private void Look(TimeSpan now)
    {
        var networks = AllEntityQuery<WGPlanetNetworkComponent>();
        while (networks.MoveNext(out var uid, out var net))
        {
            if (!_planets.TryGetValue(uid, out var planet))
                _planets[uid] = planet = new WGStoredPlanet { Network = uid, Name = net.Surface.Id };

            var wanted = !_enabled || Occupied(uid, net, out _);
            switch (planet.State)
            {
                case WGPlanetStoreState.Loaded:
                    if (wanted)
                    {
                        planet.EmptySince = null;
                        break;
                    }

                    planet.EmptySince ??= now;
                    if (_saving == null && !planet.Never && (now - planet.EmptySince.Value).TotalSeconds >= _idle)
                        TryUnload(uid, out _);
                    break;

                case WGPlanetStoreState.Saving:
                case WGPlanetStoreState.Deleting:
                case WGPlanetStoreState.Unloaded:
                    if (wanted && planet.Failures < MaxFailures)
                        Load(uid);
                    break;
            }
        }
    }

    /// <summary>
    /// Whether anyone is on a world or on the way: a player's body, ghost or camera on any layer or in orbit, a hull
    /// hopping in, a body with a mind, or something that points into it from outside (a spawn point a joining player
    /// may be sent to, a contract's wreck or cargo). Gives the Fluent id of the reason.
    /// </summary>
    public bool Occupied(EntityUid network, WGPlanetNetworkComponent net, out string reason)
    {
        var maps = Maps(net, true);
        if (_arriving.TryGetValue(network, out var until))
        {
            if (until > _timing.CurTime)
            {
                reason = "wg-planetstore-why-arriving";
                return true;
            }

            _arriving.Remove(network);
        }

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            if (session.AttachedEntity is { } body && OnAny(body, maps))
            {
                reason = "wg-planetstore-why-player";
                return true;
            }

            foreach (var eye in session.ViewSubscriptions)
            {
                if (OnAny(eye, maps))
                {
                    reason = "wg-planetstore-why-watching";
                    return true;
                }
            }
        }

        var minds = AllEntityQuery<MindComponent>();
        while (minds.MoveNext(out _, out var mind))
        {
            if (mind.OwnedEntity is { } owned && OnAny(owned, maps))
            {
                reason = "wg-planetstore-why-mind";
                return true;
            }
        }

        // A spawn point only matters if a joining player can be sent to it: one of a station with a job open.
        // The crew spawn points of a wreck's hull belong to no station.
        var spawns = AllEntityQuery<SpawnPointComponent, TransformComponent>();
        while (spawns.MoveNext(out var spawn, out _, out var xform))
        {
            if (xform.MapUid is not { } map || !maps.Contains(map))
                continue;

            if (_station.GetOwningStation(spawn, xform) is { } station && HasOpenJobs(station))
            {
                reason = "wg-planetstore-why-jobs";
                return true;
            }
        }

        foreach (var contract in _contracts.All)
        {
            if (contract.WreckGrid is { } wreck && OnAny(wreck, maps)
                || AnyOn(contract.ExtraWrecks, maps) || AnyOn(contract.Items, maps)
                || AnyOn(contract.Targets, maps) || AnyOn(contract.Anomalies, maps))
            {
                reason = "wg-planetstore-why-contract";
                return true;
            }
        }

        var ev = new WGPlanetStoreCheckEvent(network);
        RaiseLocalEvent(ref ev);
        reason = ev.Reason ?? string.Empty;
        return ev.Reason != null;
    }

    private bool HasOpenJobs(EntityUid station)
    {
        if (!HasComp<StationJobsComponent>(station))
            return false;

        foreach (var (_, slots) in _stationJobs.GetJobs(station))
        {
            if (slots is null or > 0)
                return true;
        }

        return false;
    }

    private bool OnAny(EntityUid uid, HashSet<EntityUid> maps)
    {
        return TryComp<TransformComponent>(uid, out var xform) && xform.MapUid is { } map && maps.Contains(map);
    }

    private bool AnyOn(IEnumerable<EntityUid> entities, HashSet<EntityUid> maps)
    {
        foreach (var uid in entities)
        {
            if (OnAny(uid, maps))
                return true;
        }

        return false;
    }

    /// <summary>A world's layer maps, with or without its orbit layer, which is never unloaded.</summary>
    private static HashSet<EntityUid> Maps(WGPlanetNetworkComponent net, bool orbit)
    {
        var maps = new HashSet<EntityUid>(net.Layers);
        maps.UnionWith(net.LowerLayers);
        maps.Add(net.GroundMap);
        if (orbit)
            maps.Add(net.OrbitMap);
        else
            maps.Remove(net.OrbitMap);

        return maps;
    }

    /// <summary>
    /// Starts unloading a world now, whoever is or isn't there; for admins and tests. False with the Fluent id of the reason if
    /// another world is being saved, or this one is not loaded.
    /// </summary>
    public bool TryUnload(EntityUid network, out string? reason)
    {
        reason = null;
        if (!TryComp<WGPlanetNetworkComponent>(network, out var net))
        {
            reason = "wg-planetstore-why-not-planet";
            return false;
        }

        if (!_planets.TryGetValue(network, out var planet))
            _planets[network] = planet = new WGStoredPlanet { Network = network, Name = net.Surface.Id };

        if (_saving != null)
            reason = "wg-planetstore-why-busy";
        else if (planet.State != WGPlanetStoreState.Loaded)
            reason = "wg-planetstore-why-not-loaded";

        if (reason != null)
            return false;

        BeginUnload(planet, net);
        return true;
    }

    /// <summary>Brings an unloaded world back, or stops one that is being unloaded and puts back what was taken.</summary>
    public void Load(EntityUid network)
    {
        if (!_planets.TryGetValue(network, out var planet))
            return;

        switch (planet.State)
        {
            case WGPlanetStoreState.Saving:
                // Nothing has been deleted yet: forget the save.
                CancelSave(planet);
                break;

            case WGPlanetStoreState.Deleting:
                // The file is whole, so the rest goes at once and it all comes back from the file.
                StepDeleting(planet, true);
                BeginLoad(planet);
                break;

            case WGPlanetStoreState.Unloaded:
                BeginLoad(planet);
                break;
        }
    }

    /// <summary>An unsaved entity a stored tree may lose: a sound, or an effect about to despawn that isn't a mob.</summary>
    private bool Harmless(EntityUid uid)
    {
        return HasComp<AudioComponent>(uid) || HasComp<TimedDespawnComponent>(uid) && !_mobQuery.HasComp(uid);
    }

    /// <summary>
    /// Counts errors the entity serializer logs while one of this system's save slices runs on the main thread, which
    /// fails the unload before anything is deleted.
    /// </summary>
    private sealed class ErrorCounter(WGPlanetStoreSystem system) : ILogHandler
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;

        public void Log(string sawmillName, Serilog.Events.LogEvent message)
        {
            if (message.Level >= Serilog.Events.LogEventLevel.Error
                && Environment.CurrentManagedThreadId == _thread
                && system._slicing is { } planet)
                planet.Errors++;
        }
    }

    private static byte[]? Compress(string? text)
    {
        if (text == null)
            return null;

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        {
            var raw = Encoding.UTF8.GetBytes(text);
            gzip.Write(raw, 0, raw.Length);
        }

        return output.ToArray();
    }

    private static string Decompress(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

public enum WGPlanetStoreState : byte
{
    Loaded,

    /// <summary>Being written to its file; everything is still there.</summary>
    Saving,

    /// <summary>The file is whole and what it holds is being deleted.</summary>
    Deleting,
    Unloaded,
    Loading,
}

/// <summary>Raised to ask whether a world may be unloaded; set a Fluent id as the reason to keep it loaded.</summary>
[ByRefEvent]
public record struct WGPlanetStoreCheckEvent(EntityUid Network)
{
    public string? Reason;
}

/// <summary>
/// Raised when an unloaded world is whole again, with what each stored entity became. Whatever stayed on the world
/// and pointed at something stored holds a dead id until it looks it up here.
/// </summary>
[ByRefEvent]
public readonly record struct WGPlanetRestoredEvent(EntityUid Network, IReadOnlyDictionary<EntityUid, EntityUid> Became);

/// <summary>What the planet store knows about one world.</summary>
public sealed class WGStoredPlanet
{
    public EntityUid Network;
    public string Name = string.Empty;
    public WGPlanetStoreState State;

    /// <summary>Since when nobody has been on it; null while someone is.</summary>
    public TimeSpan? EmptySince;

    /// <summary>A save of it failed, so it is left loaded for the round.</summary>
    public bool Never;

    public int Failures;
    public string? LastError;

    /// <summary>Layer maps and hulls marked hollow for atmos while their walls are away.</summary>
    public readonly List<EntityUid> Hollow = new();

    /// <summary>Children of a layer or a hull that go into the file, each with everything in it.</summary>
    public readonly List<EntityUid> Tops = new();

    /// <summary>Everything that stayed and that this system put to sleep.</summary>
    public readonly List<EntityUid> Slept = new();

    /// <summary>Every entity that is written, in order.</summary>
    public readonly List<EntityUid> Entities = new();

    /// <summary>The file's id of each stored entity, to say what it became when it is back.</summary>
    public Dictionary<EntityUid, int>? Ids;

    public int Next;
    public int Errors;
    public int StoredEntities;
    public int KeptEntities;
    public int Bytes;
    public TimeSpan UnloadedAt;
    public double SaveMs;
    public double DeleteMs;
    public double LoadMs;
    public int LoadTicks;
    public double WorstMs;

    internal EntitySerializer? Serializer;
    internal Task<byte[]?>? Zip;
    internal byte[]? File;
    internal Task? Parse;
    internal MappingDataNode? Data;
    internal StagedGridLoad? Load;
}
