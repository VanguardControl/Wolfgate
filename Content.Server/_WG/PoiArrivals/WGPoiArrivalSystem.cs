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

    /// <summary>Each held-back point of interest and how far into the shift it arrives.</summary>
    private readonly Dictionary<string, (PointOfInterestPrototype Poi, TimeSpan Due)> _pending = new();

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

    /// <summary>Holds a point of interest back from round-start generation if it arrives later; false if it doesn't.</summary>
    public bool Defer(PointOfInterestPrototype poi)
    {
        if (!_proto.TryIndex<WGPoiArrivalPrototype>(poi.ID, out var arrival))
            return false;

        var seconds = _random.NextFloat((float) arrival.MinDelay.TotalSeconds, (float) arrival.MaxDelay.TotalSeconds);
        var due = TimeSpan.FromSeconds(seconds);
        _pending[poi.ID] = (poi, due);
        Log.Info($"{poi.ID} will arrive {due:hh\\:mm\\:ss} into the round.");
        return true;
    }

    /// <summary>How far into the shift a held-back point of interest arrives, if it is still to come.</summary>
    public TimeSpan? Due(string poi)
    {
        return _pending.TryGetValue(poi, out var entry) ? entry.Due : null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        var now = _ticker.RoundDuration();
        foreach (var (id, (_, due)) in _pending)
        {
            if (now >= due)
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

        var poi = entry.Poi;
        var holding = _map.CreateMap(out var holdingId);
        if (!_poi.WgSpawnArrival(holdingId, poi, out var grid, out var destination))
        {
            Log.Error($"{poi.ID} failed to load for its arrival.");
            Del(holding);
            return false;
        }

        var arrival = EnsureComp<WGPoiArrivalComponent>(grid);
        arrival.Arrival = poi.ID;
        arrival.Holding = holding;

        var target = new EntityCoordinates(_map.GetMap(_ticker.DefaultMap), destination);
        if (TryComp<ShuttleComponent>(grid, out var shuttle)
            && _shuttle.WgTryGetFTLProximity(grid, target, out var coordinates, out var angle))
        {
            _shuttle.FTLToCoordinates(grid, shuttle, coordinates, angle, startupTime: StartupTime);
            if (HasComp<FTLComponent>(grid))
                return true;
        }

        // A grid that can't jump is set down in place.
        Log.Warning($"{poi.ID} could not jump in and was placed directly.");
        if (!_shuttle.TryFTLProximity(grid, target))
            _transform.SetCoordinates(grid, target);
        Arrive((grid, arrival));
        return true;
    }

    private void OnFtlCompleted(Entity<WGPoiArrivalComponent> ent, ref FTLCompletedEvent args)
    {
        Arrive(ent);
    }

    /// <summary>Finishes an arrival: clears the holding map, gives the station its dead drops and tells the sector.</summary>
    private void Arrive(Entity<WGPoiArrivalComponent> ent)
    {
        if (ent.Comp.Holding is { } holding && !TerminatingOrDeleted(holding))
            QueueDel(holding);

        var station = _station.GetOwningStation(ent);
        if (station != null && TryComp<StationDeadDropComponent>(station, out var drops))
            PlaceDeadDrops(station.Value, drops);

        if (_proto.TryIndex<WGPoiArrivalPrototype>(ent.Comp.Arrival, out var arrival))
        {
            var position = _transform.GetWorldPosition(ent);
            var message = Loc.GetString(arrival.Announcement,
                ("station", Name(ent)),
                ("range", WGReaverCampaignSystem.Kilometres(System.Numerics.Vector2.Zero, position)),
                ("bearing", WGReaverCampaignSystem.Bearing(System.Numerics.Vector2.Zero, position)));
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
