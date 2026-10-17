using Content.Server._Mono.AlertLevel;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.SectorControl;
using Content.Shared._WF.Administration.Ert;
using Content.Shared._WF.Administration.GhostOffer;
using Content.Shared._WF.Administration.SecondChance;
using Content.Shared._WF.GodPanel;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.ShipPa;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.GodPanel;

public sealed partial class WFGodPanelSystem
{
    /// <summary>How many timeline events are kept.</summary>
    public const int MaxEvents = 500;

    private readonly List<WFGodEvent> _events = new();

    /// <summary>The id of the newest event; 0 before any.</summary>
    public int LastEventId { get; private set; }

    private void InitializeLog()
    {
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundEndedEvent>(OnRoundEnded);
        SubscribeLocalEvent<WFEncounterStartedEvent>(OnEncounterStarted);
        SubscribeLocalEvent<WFEncounterResolvedEvent>(OnEncounterResolved);
        SubscribeLocalEvent<WFSectorCellChangedEvent>(OnSectorChanged);
        SubscribeLocalEvent<WFSectorCellContestedEvent>(OnSectorContested);
        SubscribeLocalEvent<WFSectorCellRetakenEvent>(OnSectorRetaken);
        SubscribeLocalEvent<WFSectorCellDisputedEvent>(OnSectorDisputed);
        SubscribeLocalEvent<WarLevelChangedEvent>(OnWarLevelChanged);
        SubscribeLocalEvent<WFShipAlertChangedEvent>(OnShipAlertChanged);
        SubscribeLocalEvent<WFSecondChanceAnsweredEvent>(OnSecondChanceAnswered);
        SubscribeLocalEvent<WFGhostOfferTakenEvent>(OnGhostOfferTaken);
        SubscribeLocalEvent<WFErtSpawnedEvent>(OnErtSpawned);
    }

    /// <summary>Adds a line to the timeline at the current round time.</summary>
    public void AddEvent(WFGodEventCategory category, string text, EntityUid? subject = null,
        NetCoordinates? coordinates = null)
    {
        _events.Add(new WFGodEvent(++LastEventId, RoundTime(), category, text,
            subject is { } uid && Exists(uid) ? GetNetEntity(uid) : null, coordinates));

        if (_events.Count > MaxEvents)
            _events.RemoveRange(0, _events.Count - MaxEvents);
    }

    /// <summary>The events newer than <paramref name="id"/>, oldest first.</summary>
    public List<WFGodEvent> EventsAfter(int id)
    {
        var events = new List<WFGodEvent>();
        for (var i = _events.Count - 1; i >= 0 && _events[i].Id > id; i--)
        {
            events.Add(_events[i]);
        }

        events.Reverse();
        return events;
    }

    private void ClearLog()
    {
        _events.Clear();
    }

    private NetCoordinates? CoordinatesOf(EntityUid uid)
    {
        if (!Exists(uid))
            return null;

        var xform = Transform(uid);
        if (xform.MapUid is not { } map)
            return null;

        // A client always has the grids and maps, but not whatever else the entity may be inside.
        return GetNetCoordinates(_transform.WithEntityId(xform.Coordinates, xform.GridUid ?? map));
    }

    private NetCoordinates? CoordinatesOf(MapCoordinates coordinates)
    {
        return _map.TryGetMap(coordinates.MapId, out var map)
            ? GetNetCoordinates(new EntityCoordinates(map.Value, coordinates.Position))
            : null;
    }

    private string GridNameOf(EntityUid uid)
    {
        return Transform(uid).GridUid is { } grid ? Name(grid) : Loc.GetString("wf-godpanel-space");
    }

    /// <summary>The name of the station nearest a point, or deep space if the map has none.</summary>
    private string NearestStation(MapCoordinates origin)
    {
        string? best = null;
        var distance = float.MaxValue;
        foreach (var station in _territory.Stations(origin.MapId))
        {
            var apart = (_transform.GetWorldPosition(station) - origin.Position).LengthSquared();
            if (apart >= distance)
                continue;

            distance = apart;
            best = Name(station);
        }

        return best ?? Loc.GetString("wf-godpanel-space");
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        // Only a player's body.
        if (!_minds.TryGetMind(args.Target, out _, out var mind) || mind.UserId == null)
            return;

        var key = args.NewMobState switch
        {
            MobState.Dead => "wf-godpanel-event-death",
            MobState.Critical => "wf-godpanel-event-crit",
            MobState.Alive when args.OldMobState != MobState.Invalid => "wf-godpanel-event-revived",
            _ => null,
        };

        if (key == null)
            return;

        AddEvent(WFGodEventCategory.Death,
            Loc.GetString(key, ("name", Name(args.Target)), ("grid", GridNameOf(args.Target))),
            args.Target, CoordinatesOf(args.Target));
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        var ghost = HasComp<GhostComponent>(args.Entity);

        // A gamemaster's own ghost moving about is not news.
        if (ghost && _admin.IsAdmin(args.Player))
            return;

        AddEvent(WFGodEventCategory.Player,
            Loc.GetString(ghost ? "wf-godpanel-event-ghosted" : "wf-godpanel-event-joined-body",
                ("user", args.Player.Name), ("name", Name(args.Entity))),
            args.Entity, CoordinatesOf(args.Entity));
    }

    private void OnLogPlayerStatus(SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame && args.OldStatus != SessionStatus.InGame)
            AddEvent(WFGodEventCategory.Player, Loc.GetString("wf-godpanel-event-connected", ("user", args.Session.Name)));
        else if (args.NewStatus == SessionStatus.Disconnected && args.OldStatus != SessionStatus.Disconnected)
            AddEvent(WFGodEventCategory.Player, Loc.GetString("wf-godpanel-event-left", ("user", args.Session.Name)));
    }

    private void OnRoundStarted(RoundStartedEvent args)
    {
        AddEvent(WFGodEventCategory.Round, Loc.GetString("wf-godpanel-event-round-started", ("id", args.RoundId)));
    }

    private void OnRoundEnded(RoundEndedEvent args)
    {
        AddEvent(WFGodEventCategory.Round, Loc.GetString("wf-godpanel-event-round-ended", ("id", args.RoundId)));
    }

    private void OnEncounterStarted(ref WFEncounterStartedEvent args)
    {
        if (!TryComp<WFEncounterComponent>(args.Encounter, out var encounter))
            return;

        AddEvent(WFGodEventCategory.Encounter,
            Loc.GetString("wf-godpanel-event-encounter-started",
                ("name", encounter.Name), ("place", NearestStation(encounter.Origin))),
            args.Encounter, CoordinatesOf(encounter.Origin));
    }

    private void OnEncounterResolved(ref WFEncounterResolvedEvent args)
    {
        if (!TryComp<WFEncounterComponent>(args.Encounter, out var encounter))
            return;

        AddEvent(WFGodEventCategory.Encounter,
            Loc.GetString("wf-godpanel-event-encounter-resolved",
                ("name", encounter.Name),
                ("place", NearestStation(encounter.Origin)),
                ("result", Loc.GetString($"wf-encounter-resolution-{args.Resolution.ToString().ToLowerInvariant()}"))),
            args.Encounter, CoordinatesOf(encounter.Origin));
    }

    private string FactionName(ProtoId<WFSectorFactionPrototype> faction)
    {
        return _proto.TryIndex(faction, out var proto) ? Loc.GetString(proto.Name) : faction.Id;
    }

    private void OnSectorChanged(ref WFSectorCellChangedEvent args)
    {
        var cell = WFSectorHex.Callsign(args.Cell);
        var text = args.Owner is { } owner
            ? args.Previous is { } previous
                ? Loc.GetString("wf-godpanel-event-sector-changed",
                    ("cell", cell), ("from", FactionName(previous)), ("to", FactionName(owner)))
                : Loc.GetString("wf-godpanel-event-sector-claimed", ("cell", cell), ("faction", FactionName(owner)))
            : Loc.GetString("wf-godpanel-event-sector-freed", ("cell", cell));

        AddEvent(WFGodEventCategory.Sector, text, null, CoordinatesOf(_territory.Centre(args.Map, args.Cell)));
    }

    private void OnSectorContested(ref WFSectorCellContestedEvent args)
    {
        AddEvent(WFGodEventCategory.Sector,
            Loc.GetString("wf-godpanel-event-sector-contested",
                ("cell", WFSectorHex.Callsign(args.Cell)), ("faction", FactionName(args.Faction))),
            null, CoordinatesOf(_territory.Centre(args.Map, args.Cell)));
    }

    private void OnSectorRetaken(ref WFSectorCellRetakenEvent args)
    {
        AddEvent(WFGodEventCategory.Sector,
            Loc.GetString("wf-godpanel-event-sector-retaken",
                ("cell", WFSectorHex.Callsign(args.Cell)), ("faction", FactionName(args.Faction))),
            null, CoordinatesOf(_territory.Centre(args.Map, args.Cell)));
    }

    private void OnSectorDisputed(ref WFSectorCellDisputedEvent args)
    {
        AddEvent(WFGodEventCategory.Sector,
            Loc.GetString(args.Disputed ? "wf-godpanel-event-sector-disputed" : "wf-godpanel-event-sector-undisputed",
                ("cell", WFSectorHex.Callsign(args.Cell)), ("faction", FactionName(args.Faction))),
            null, CoordinatesOf(_territory.Centre(args.Map, args.Cell)));
    }

    private void OnWarLevelChanged(WarLevelChangedEvent args)
    {
        AddEvent(WFGodEventCategory.Round,
            Loc.GetString(args.WarLevel ? "wf-godpanel-event-war-hot" : "wf-godpanel-event-war-cold"));
    }

    private void OnShipAlertChanged(ref WFShipAlertChangedEvent args)
    {
        var ship = Name(args.Grid);
        var text = args.Change switch
        {
            WFShipAlertChange.Code => Loc.GetString("wf-godpanel-event-ship-code",
                ("ship", ship), ("code", CodeName(args.Text))),
            WFShipAlertChange.GeneralQuartersSounded => Loc.GetString("wf-godpanel-event-ship-gq-on", ("ship", ship)),
            WFShipAlertChange.GeneralQuartersSecured => Loc.GetString("wf-godpanel-event-ship-gq-off", ("ship", ship)),
            WFShipAlertChange.DistressSent => Loc.GetString("wf-godpanel-event-ship-distress-on", ("ship", ship)),
            WFShipAlertChange.DistressCancelled => Loc.GetString("wf-godpanel-event-ship-distress-off", ("ship", ship)),
            _ => Loc.GetString("wf-godpanel-event-ship-announce", ("ship", ship), ("text", args.Text ?? string.Empty)),
        };

        AddEvent(WFGodEventCategory.Ship, text, args.Grid, CoordinatesOf(args.Grid));
    }

    private string CodeName(string? id)
    {
        return id != null && _proto.TryIndex<ShipAlertCodePrototype>(id, out var code) ? Loc.GetString(code.Name) : id ?? string.Empty;
    }

    private void OnSecondChanceAnswered(ref WFSecondChanceAnsweredEvent args)
    {
        AddEvent(WFGodEventCategory.Player,
            Loc.GetString(args.Accepted ? "wf-godpanel-event-second-chance-yes" : "wf-godpanel-event-second-chance-no",
                ("player", args.Player), ("name", Name(args.Body))),
            args.Body, CoordinatesOf(args.Body));
    }

    private void OnGhostOfferTaken(ref WFGhostOfferTakenEvent args)
    {
        AddEvent(WFGodEventCategory.Player,
            Loc.GetString("wf-godpanel-event-ghost-offer-taken", ("player", args.Player), ("name", Name(args.Entity))),
            args.Entity, CoordinatesOf(args.Entity));
    }

    private void OnErtSpawned(ref WFErtSpawnedEvent args)
    {
        AddEvent(WFGodEventCategory.Encounter,
            Loc.GetString("wf-godpanel-event-ert", ("team", args.Team), ("count", args.Members)),
            args.Ship, args.Ship is { } ship ? CoordinatesOf(ship) : null);
    }
}
