using Content.Server._Mono.AlertLevel;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.Audio.InternetSound;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server.Administration.Systems;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Administration.GridPower;
using Content.Shared._WF.GodPanel;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipPa;
using Content.Shared.Damage;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.GodPanel;

public sealed partial class WFGodPanelSystem
{
    [Dependency] private AdminSystem _adminSystem = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private GhostOfferSystem _ghostOffer = default!;
    [Dependency] private GridPowerSystem _gridPower = default!;
    [Dependency] private InternetSoundSystem _internetSound = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SecondChanceSystem _secondChance = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WarLevelSystem _war = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFReaverCampaignSystem _reavers = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Builds the full state with the event log for a panel that has seen up to <paramref name="afterEventId"/>.</summary>
    public WFGodPanelState BuildState(int afterEventId = 0)
    {
        return BuildBase() with { Events = EventsAfter(afterEventId), LastEventId = LastEventId };
    }

    /// <summary>The state without any events: round, players and grids.</summary>
    private WFGodPanelState BuildBase()
    {
        return new WFGodPanelState(BuildRound(), BuildPlayers(), BuildGrids(), new List<WFGodEvent>(), LastEventId);
    }

    /// <summary>The round time, 0 before the round starts.</summary>
    private TimeSpan RoundTime()
    {
        return _ticker.RunLevel == GameRunLevel.PreRoundLobby ? TimeSpan.Zero : _ticker.RoundDuration();
    }

    private WFGodRound BuildRound()
    {
        var tier = _reavers.State()?.Tier ?? 0;
        var cells = 0;
        if (_reavers.Campaign is { } campaign && _territory.SectorMap != MapId.Nullspace)
            cells = _territory.Held(_territory.SectorMap, campaign.Faction).Count;

        var sound = _internetSound.GetGlobalTrack();
        return new WFGodRound(
            RoundTime(),
            _ticker.RunLevel == GameRunLevel.InRound,
            _war.GetWarLevel(EntityUid.Invalid),
            tier,
            _reavers.TierName(tier),
            cells,
            _reavers.Paused,
            _encounters.ActiveCount(),
            _scheduler.Paused,
            sound?.Title,
            sound?.Requester,
            _subscribers.Count);
    }

    private List<WFGodPlayer> BuildPlayers()
    {
        var players = new List<WFGodPlayer>();
        var seen = new HashSet<NetUserId>();

        foreach (var session in _players.Sessions)
        {
            seen.Add(session.UserId);
            players.Add(BuildPlayer(session.UserId, session, session.AttachedEntity));
        }

        // Bodies whose player has gone, so the GM can still find, offer or rejuvenate them.
        var minds = EntityQueryEnumerator<MindComponent>();
        while (minds.MoveNext(out var mind))
        {
            if (mind.UserId is not { } user || seen.Contains(user) || mind.OwnedEntity is not { } body
                || TerminatingOrDeleted(body) || HasComp<GhostComponent>(body))
                continue;

            seen.Add(user);
            players.Add(BuildPlayer(user, null, body));
        }

        return players;
    }

    private WFGodPlayer BuildPlayer(NetUserId user, ICommonSession? session, EntityUid? entity)
    {
        var info = _adminSystem.GetCachedPlayerInfo(user);
        var username = session?.Name ?? info?.Username ?? user.ToString();
        var character = info?.CharacterName ?? string.Empty;
        var job = info?.StartingJob ?? string.Empty;
        var antag = info?.Antag ?? false;
        var role = antag && info != null ? Loc.GetString(info.RoleProto.Name) : string.Empty;
        var admin = session != null && _admin.IsAdmin(session);

        if (entity is not { } uid || TerminatingOrDeleted(uid))
        {
            var status = session == null ? WFGodPlayerStatus.Disconnected : WFGodPlayerStatus.Lobby;
            return new WFGodPlayer(user, null, username, character, job, antag, role, null, string.Empty, null, null,
                status, 0f, admin, false);
        }

        if (character == string.Empty)
            character = Name(uid);

        var xform = Transform(uid);
        NetEntity? grid = null;
        NetEntity? map = null;
        NetCoordinates? coordinates = null;
        var gridName = string.Empty;

        if (xform.GridUid is { } gridUid)
        {
            grid = GetNetEntity(gridUid);
            gridName = Name(gridUid);
        }

        if (xform.MapUid is { } mapUid)
        {
            map = GetNetEntity(mapUid);
            // A client always has the grids and maps, but not whatever else the player may be inside.
            coordinates = GetNetCoordinates(_transform.WithEntityId(xform.Coordinates, xform.GridUid ?? mapUid));
        }

        var ghost = HasComp<GhostComponent>(uid);
        var offered = IsOffered(uid);
        if (ghost && _minds.TryGetMind(user, out _, out var mind) && mind.LastMob is { } last)
            offered |= IsOffered(last);

        return new WFGodPlayer(user, GetNetEntity(uid), username, character, job, antag, role, grid, gridName, map,
            coordinates, GetStatus(uid, session != null, ghost), ghost ? 1f : GetHealth(uid), admin, offered);
    }

    private bool IsOffered(EntityUid body)
    {
        return _secondChance.IsOffered(body) || _ghostOffer.IsOffered(body);
    }

    private WFGodPlayerStatus GetStatus(EntityUid uid, bool connected, bool ghost)
    {
        if (!connected)
            return WFGodPlayerStatus.Disconnected;

        if (ghost)
            return WFGodPlayerStatus.Ghost;

        if (!TryComp<MobStateComponent>(uid, out var state))
            return WFGodPlayerStatus.Alive;

        return state.CurrentState switch
        {
            MobState.Dead => WFGodPlayerStatus.Dead,
            MobState.Critical => WFGodPlayerStatus.Critical,
            _ => WFGodPlayerStatus.Alive,
        };
    }

    /// <summary>Health as 1 for undamaged down to 0 at the dead threshold.</summary>
    private float GetHealth(EntityUid uid)
    {
        if (_mobState.IsDead(uid))
            return 0f;

        if (!TryComp<DamageableComponent>(uid, out var damageable)
            || !_thresholds.TryGetThresholdForState(uid, MobState.Dead, out var threshold)
            || threshold <= 0)
            return 1f;

        return Math.Clamp(1f - (float) (damageable.TotalDamage / threshold.Value), 0f, 1f);
    }

    private List<WFGodGrid> BuildGrids()
    {
        var power = new Dictionary<NetEntity, GridPowerInfo>();
        foreach (var info in _gridPower.BuildList())
        {
            power[info.Grid] = info;
        }

        var owners = new Dictionary<EntityUid, string>();
        var deeds = EntityQueryEnumerator<ShuttleDeedComponent>();
        while (deeds.MoveNext(out var deed))
        {
            if (deed.ShuttleUid is { } ship && !string.IsNullOrEmpty(deed.ShuttleOwner))
                owners[ship] = deed.ShuttleOwner;
        }

        var players = new Dictionary<EntityUid, int>();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } body && !HasComp<GhostComponent>(body)
                && Transform(body).GridUid is { } aboard)
                players[aboard] = players.GetValueOrDefault(aboard) + 1;
        }

        var crew = new Dictionary<EntityUid, int>();
        var npcs = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (npcs.MoveNext(out var uid, out _, out var npcXform))
        {
            if (npcXform.GridUid is { } aboard && !_mobState.IsDead(uid))
                crew[aboard] = crew.GetValueOrDefault(aboard) + 1;
        }

        var grids = new List<WFGodGrid>();
        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var grid, out var mapGrid, out var xform))
        {
            if (xform.MapUid is not { } mapUid)
                continue;

            var encounter = CompOrNull<WFEncounterGridComponent>(grid);
            var station = HasComp<StationMemberComponent>(grid);
            var deeded = owners.TryGetValue(grid, out var deedOwner);
            var access = CompOrNull<WFShipAccessComponent>(grid);
            var aboardCount = players.GetValueOrDefault(grid);
            var npcCount = crew.GetValueOrDefault(grid);

            if (encounter == null && !station && !deeded && access == null && aboardCount == 0 && npcCount == 0)
                continue;

            var kind = WFGodGridKind.Ship;
            var encounterName = string.Empty;
            var encounterState = string.Empty;
            NetEntity? encounterId = null;
            if (encounter != null && TryComp<WFEncounterComponent>(encounter.Encounter, out var comp))
            {
                kind = WFGodGridKind.Encounter;
                encounterId = GetNetEntity(encounter.Encounter);
                encounterName = comp.Name;
                encounterState = comp.Resolution is { } resolution
                    ? Loc.GetString($"wf-encounter-resolution-{resolution.ToString().ToLowerInvariant()}")
                    : Loc.GetString("cmd-wf_encounter-state-active");
                if (comp.Ships.TryGetValue(encounter.Key, out var shipState) && shipState.Derelict)
                    kind = WFGodGridKind.Debris;
            }
            else if (station && !deeded)
            {
                kind = WFGodGridKind.Station;
            }

            var owner = !string.IsNullOrEmpty(access?.OwnerName) ? access.OwnerName : deedOwner ?? string.Empty;
            var net = GetNetEntity(grid);
            power.TryGetValue(net, out var powerInfo);
            var alert = CompOrNull<ShipAlertComponent>(grid);
            var centre = _transform.ToMapCoordinates(new EntityCoordinates(grid, mapGrid.LocalAABB.Center));

            grids.Add(new WFGodGrid(
                net,
                Name(grid),
                kind,
                GetNetEntity(mapUid),
                GetNetCoordinates(new EntityCoordinates(mapUid, centre.Position)),
                owner,
                aboardCount,
                npcCount,
                powerInfo != null,
                powerInfo is { MaxCharge: > 0f } ? Math.Clamp(powerInfo.Charge / powerInfo.MaxCharge, 0f, 1f) : 0f,
                powerInfo?.ApcsOn ?? 0,
                powerInfo?.Apcs ?? 0,
                alert?.Code.Id ?? string.Empty,
                alert?.GeneralQuarters ?? false,
                alert?.Distress ?? false,
                encounterId,
                encounterName,
                encounterState,
                DockedTo(grid),
                kind == WFGodGridKind.Ship && _encounters.DockedAtStation(grid)));
        }

        return grids;
    }

    /// <summary>The names of the grids this one is docked with.</summary>
    private string DockedTo(EntityUid grid)
    {
        var names = new List<string>();
        foreach (var dock in _docking.GetDocks(grid))
        {
            if (dock.Comp.DockedWith is { } other && Transform(other).GridUid is { } alongside && alongside != grid)
            {
                var name = Name(alongside);
                if (!names.Contains(name))
                    names.Add(name);
            }
        }

        return string.Join(", ", names);
    }
}
