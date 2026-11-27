using System.Linq;
using System.Numerics;
using Content.Server._NF.GameRule;
using Content.Server._NF.Smuggling;
using Content.Server._NF.Smuggling.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._WG.PoiArrivals;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WG.PoiArrivals;

/// <summary>Holds arriving points of interest back at round start, then jumps each into the sector at its time.</summary>
public sealed partial class WGPoiArrivalSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DeadDropSystem _deadDrops = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private PointOfInterestSystem _poi = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private StationSystem _station = default!;

    /// <summary>Spool-up on the holding map, which nobody sees.</summary>
    private const float StartupTime = 1f;

    /// <summary>A held-back point of interest: its arrival, the map it arrives on and how far into the shift.</summary>
    private readonly record struct Pending(PointOfInterestPrototype Poi, WGPoiArrivalPrototype Arrival, MapId Sector, TimeSpan Due);

    /// <summary>The held-back points of interest, by POI ID.</summary>
    private readonly Dictionary<string, Pending> _pending = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<WGPoiArrivalComponent, FTLCompletedEvent>(OnFtlCompleted);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _pending.Clear();
    }

    /// <summary>The arrival for a point of interest, if it has one.</summary>
    public WGPoiArrivalPrototype? ArrivalOf(string poi)
    {
        return _proto.EnumeratePrototypes<WGPoiArrivalPrototype>().FirstOrDefault(arrival => arrival.Poi == poi);
    }

    /// <summary>Holds a point of interest back from round-start generation if it arrives on the sector map later; false if it doesn't.</summary>
    public bool Defer(PointOfInterestPrototype poi, MapId sector)
    {
        if (ArrivalOf(poi.ID) is not { } arrival)
            return false;

        var seconds = _random.NextFloat((float) arrival.MinDelay.TotalSeconds, (float) arrival.MaxDelay.TotalSeconds);
        var due = TimeSpan.FromSeconds(seconds);
        _pending[poi.ID] = new Pending(poi, arrival, sector, due);
        Log.Info($"{poi.ID} will arrive {due:hh\\:mm\\:ss} into the round.");
        return true;
    }

    /// <summary>How far into the shift a held-back point of interest arrives, if it is still to come.</summary>
    public TimeSpan? Due(string poi)
    {
        return _pending.TryGetValue(poi, out var entry) ? entry.Due : null;
    }

    /// <summary>The held-back points of interest still to come, soonest first.</summary>
    public List<(string Id, TimeSpan Due)> PendingArrivals()
    {
        return _pending.Select(pair => (pair.Key, pair.Value.Due)).OrderBy(entry => entry.Due).ToList();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Nothing arrives once the shift is over; restart cleanup drops what is left.
        if (_pending.Count == 0 || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        var now = _ticker.RoundDuration();
        foreach (var (id, entry) in _pending)
        {
            if (now >= entry.Due)
            {
                WarpIn(id);
                return;
            }
        }
    }

    /// <summary>Jumps a held-back point of interest into the sector now; false if none is pending or it failed to load.</summary>
    public bool WarpIn(string id)
    {
        if (!_pending.Remove(id, out var entry))
            return false;

        if (!_map.TryGetMap(entry.Sector, out var sector))
        {
            Log.Error($"{id} has no sector map to arrive on.");
            return false;
        }

        var holding = _map.CreateMap(out var holdingId);
        if (!_poi.WgSpawnArrival(holdingId, entry.Poi, out var grid, out var destination))
        {
            Log.Error($"{id} failed to load for its arrival.");
            Del(holding);
            return false;
        }

        // Setting up the station listed the holding map as an FTL destination; no one else may jump there.
        RemComp<FTLDestinationComponent>(holding);

        var arrival = EnsureComp<WGPoiArrivalComponent>(grid);
        arrival.Arrival = entry.Arrival.ID;
        arrival.Holding = holding;

        var target = new EntityCoordinates(sector.Value, destination);
        if (TryComp<ShuttleComponent>(grid, out var shuttle)
            && _shuttle.WgTryGetFTLProximity(grid, target, out var coordinates, out var angle))
        {
            _shuttle.FTLToCoordinates(grid, shuttle, coordinates, angle, startupTime: StartupTime);
            if (HasComp<FTLComponent>(grid))
                return true;
        }

        // A grid that can't jump is set down in place.
        Log.Warning($"{id} could not jump in and was placed directly.");
        if (!_shuttle.TryFTLProximity(grid, target))
            _transform.SetCoordinates(grid, target);
        Arrive((grid, arrival));
        return true;
    }

    private void OnFtlCompleted(Entity<WGPoiArrivalComponent> ent, ref FTLCompletedEvent args)
    {
        Arrive(ent);
    }

    /// <summary>Finishes an arrival: clears the holding map, gives the station its dead drops and, mid-shift, tells the sector.</summary>
    private void Arrive(Entity<WGPoiArrivalComponent> ent)
    {
        if (ent.Comp.Holding is { } holding && !TerminatingOrDeleted(holding))
            QueueDel(holding);

        var station = _station.GetOwningStation(ent);
        if (station != null && TryComp<StationDeadDropComponent>(station, out var drops))
            PlaceDeadDrops(station.Value, drops);

        if (_ticker.RunLevel == GameRunLevel.InRound && _proto.TryIndex<WGPoiArrivalPrototype>(ent.Comp.Arrival, out var arrival))
        {
            var position = _transform.GetWorldPosition(ent);
            var message = Loc.GetString(arrival.Announcement,
                ("station", Name(ent)),
                ("range", WGReaverCampaignSystem.Kilometres(Vector2.Zero, position)),
                ("bearing", WGReaverCampaignSystem.Bearing(Vector2.Zero, position)));
            _chat.DispatchGlobalAnnouncement(message, Loc.GetString(arrival.Sender), colorOverride: arrival.Color);
        }

        Log.Info($"{ToPrettyString(ent)} arrived at {_transform.GetMapCoordinates(ent)}.");
        RemComp<WGPoiArrivalComponent>(ent);
    }

    /// <summary>Rolls the station's dead drops as round start would have; the sector's drops were shared out before it came.</summary>
    private void PlaceDeadDrops(EntityUid station, StationDeadDropComponent drops)
    {
        var count = _random.Next(0, drops.MaxDeadDrops + 1);
        var candidates = new List<EntityUid>();
        var query = EntityQueryEnumerator<PotentialDeadDropComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.Anchored && !HasComp<DeadDropComponent>(uid) && _station.GetOwningStation(uid, xform) == station)
                candidates.Add(uid);
        }

        _random.Shuffle(candidates);
        for (var i = 0; i < count && i < candidates.Count; i++)
        {
            _deadDrops.AddDeadDrop(candidates[i]);
        }
    }
}
