using System.Linq;
using System.Numerics;
using Content.Server._Mono.AlertLevel;
using Content.Server._Mono.GridClaimer;
using Content.Server._NF.SectorServices;
using Content.Server.Chat.Managers;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared._WG.Wilderness;
using Content.Shared.Alert;
using Content.Shared.Chat;
using Content.Shared.Ghost;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Wilderness;

/// <summary>
/// Works out which zone each player is in, once a second, and tells them on the way in and out: the Safe Zone round
/// Central, Wildernesses round event sites, faction outposts and named not-safe stations. Zones come from
/// <see cref="WGZoneSourceComponent"/> on grids and markers.
/// </summary>
public sealed partial class WGZoneSystem : EntitySystem
{
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private INetConfigurationManager _netCfg = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SectorServiceSystem _sector = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RingInterval = TimeSpan.FromSeconds(5);
    private static readonly ProtoId<AlertPrototype> MarkedAlert = "WGMarked";
    private static readonly ProtoId<AlertCategoryPrototype> ZoneCategory = "WGZone";

    private TimeSpan _next;
    private TimeSpan _nextRings;

    /// <summary>The tick <see cref="_sources"/> was last gathered on.</summary>
    private GameTick _sourcesTick;

    private readonly List<Source> _sources = new();
    private readonly Dictionary<EntityUid, List<ICommonSession>> _aboard = new();
    private readonly Dictionary<EntityUid, List<WGZoneRing>> _rings = new();
    private readonly List<(EntityUid Mob, EntityUid Subject, WGZonePrototype? Zone)> _alertsDue = new();

    /// <summary>How long a repeatable zone message stays quiet for a player after it is sent.</summary>
    private static readonly TimeSpan MessageCooldown = TimeSpan.FromSeconds(60);

    /// <summary>When each player was last sent each repeatable message.</summary>
    private readonly Dictionary<(NetUserId User, string Key), TimeSpan> _told = new();
    private readonly List<(NetUserId User, string Key)> _toldDrop = new();

    /// <summary>A zone's centre this tick: where it is, how wide its source is, and how far past that it reaches.</summary>
    private readonly record struct Source(EntityUid Uid, MapId Map, Vector2 Centre, float Size, float Reach, WGZonePrototype Zone, bool Rings);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGInZoneComponent, PlayerDetachedEvent>(OnDetached);
        InitializeSkull();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;
        GatherSources();
        UpdatePlayers();
        UpdateSkulls(now);
        UpdateGunners(now);
        UpdateAlerts();

        if (now < _nextRings)
            return;

        _nextRings = now + RingInterval;
        UpdateRings();
        PruneTold(now);
    }

    private void OnDetached(Entity<WGInZoneComponent> ent, ref PlayerDetachedEvent args)
    {
        _alerts.ClearAlertCategory(ent, ZoneCategory);
        RemComp<WGInZoneComponent>(ent);
    }

    private void GatherSources()
    {
        _sourcesTick = _timing.CurTick;
        _sources.Clear();
        var query = EntityQueryEnumerator<WGZoneSourceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var source, out var xform))
        {
            // A claimed wreck is someone's ship now, not a site.
            if (xform.MapID == MapId.Nullspace || source.EndsWhenClaimed && Claimed(uid) || !_proto.TryIndex(source.Zone, out var zone))
                continue;

            var (centre, size) = Bounds(uid, xform);
            _sources.Add(new Source(uid, xform.MapID, centre, size, source.Radius ?? zone.Radius, zone, source.Rings && zone.Rings));
        }
    }

    /// <summary>Whether the war is HOT.</summary>
    public bool IsHot()
    {
        return TryComp<WarLevelComponent>(_sector.GetServiceEntity(), out var war) && war.PostWar;
    }

    private void UpdatePlayers()
    {
        _aboard.Clear();
        var hot = IsHot();
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } mob || HasComp<GhostComponent>(mob) || TerminatingOrDeleted(mob))
                continue;

            var subject = Subject(mob);
            if (!_aboard.TryGetValue(subject, out var list))
                _aboard[subject] = list = new List<ICommonSession>();
            list.Add(session);

            var found = FindZone(mob, subject);
            Apply(session, mob, found, hot);
            _alertsDue.Add((mob, subject, found?.Zone));
        }
    }

    /// <summary>Shows each player's zone alert, after the marks have been updated so it matches them.</summary>
    private void UpdateAlerts()
    {
        foreach (var (mob, subject, zone) in _alertsDue)
        {
            if (!TerminatingOrDeleted(mob))
                UpdateAlert(mob, subject, zone);
        }

        _alertsDue.Clear();
    }

    /// <summary>
    /// The one zone alert a player sees: a mark, their own over their ship's, running down its time once out of the
    /// Wilderness, over the zone's own, which in a Wilderness counts down to the mark.
    /// </summary>
    private void UpdateAlert(EntityUid mob, EntityUid subject, WGZonePrototype? zone)
    {
        var skull = CompOrNull<WGSkullComponent>(mob);
        if (skull == null && subject != mob)
            skull = CompOrNull<WGSkullComponent>(subject);

        if (skull != null)
        {
            (TimeSpan, TimeSpan)? left = skull.ExpiresAt is { } expires ? (expires - _skullLength, expires) : null;
            _alerts.ShowAlert(mob, MarkedAlert, cooldown: left);
            return;
        }

        if (zone?.Alert is not { } alert)
        {
            _alerts.ClearAlertCategory(mob, ZoneCategory);
            return;
        }

        var dwell = CompOrNull<WGWildernessDwellComponent>(mob);
        if (dwell == null && subject != mob)
            dwell = CompOrNull<WGWildernessDwellComponent>(subject);

        (TimeSpan, TimeSpan)? marking = zone.Skulls && dwell != null
            ? (dwell.Since, dwell.Since + _dwell)
            : null;
        _alerts.ShowAlert(mob, alert, cooldown: marking);
    }

    /// <summary>Sets a player's zone, and tells them when it changes.</summary>
    private void Apply(ICommonSession session, EntityUid mob, Source? found, bool hot)
    {
        TryComp<WGInZoneComponent>(mob, out var current);
        WGZonePrototype? old = null;
        if (current != null)
            _proto.TryIndex(current.Zone, out old);

        if (found is not { } source)
        {
            if (current == null)
                return;

            if (old != null && WantsZoneChat(session) && Due(session, $"left/{old.ID}/{current.Place}"))
                Tell(session, Loc.GetString(old.Left, ("place", current.Place)), old.Color);

            RemComp<WGInZoneComponent>(mob);
            return;
        }

        var zone = source.Zone;
        var place = Name(source.Uid);
        if (current != null && current.Zone == zone.ID && current.Place == place)
        {
            if (current.Hot != hot)
            {
                current.Hot = hot;
                Dirty(mob, current);
            }

            return;
        }

        current ??= AddComp<WGInZoneComponent>(mob);
        current.Zone = zone.ID;
        current.Place = place;
        current.Hot = hot;
        Dirty(mob, current);

        if (WantsZoneChat(session) && Due(session, $"{zone.ID}/{place}"))
            Tell(session, Loc.GetString(zone.Entered, ("place", place), ("summary", WGZoneText.Summary(zone, hot, _cfg.GetCVar(WildernessCVars.OpenToAll), _onFoot))), zone.Color);
    }

    /// <summary>Whether the player wants a chat line on entering or leaving a zone.</summary>
    private bool WantsZoneChat(ICommonSession session)
    {
        return _netCfg.GetClientCVar(session.Channel, WildernessCVars.ZoneChat);
    }

    /// <summary>
    /// Whether a repeatable message is past its cooldown for this player, and if so starts it again, so hopping back and
    /// forth over a zone's edge doesn't repeat it.
    /// </summary>
    private bool Due(ICommonSession session, string key)
    {
        var now = _timing.CurTime;
        if (_told.TryGetValue((session.UserId, key), out var last) && now - last < MessageCooldown)
            return false;

        _told[(session.UserId, key)] = now;
        return true;
    }

    /// <summary>Forgets cooldowns that have run out.</summary>
    private void PruneTold(TimeSpan now)
    {
        _toldDrop.Clear();
        foreach (var (key, last) in _told)
        {
            if (now - last >= MessageCooldown)
                _toldDrop.Add(key);
        }

        foreach (var key in _toldDrop)
            _told.Remove(key);
    }

    private void Tell(ICommonSession session, string message, Color color)
    {
        var wrapped = Loc.GetString("wg-zone-chat-wrap", ("message", message), ("color", color.ToHex()));
        _chat.ChatMessageToOne(ChatChannel.Server, message, wrapped, default, false, session.Channel, colorOverride: color);
    }

    /// <summary>The zone a person is in: the highest-priority zone that reaches them, or what they're aboard.</summary>
    private Source? FindZone(EntityUid mob, EntityUid subject)
    {
        var xform = Transform(subject);
        var (centre, size) = Bounds(subject, xform);
        return FindZone(xform.MapID, centre, size, Transform(mob).GridUid, HasComp<MapGridComponent>(subject) ? subject : null);
    }

    /// <summary>
    /// The highest-priority zone reaching a circle on a map, nearest first among equals. A ship docked at a station
    /// whose zone is aboard only is in that zone too.
    /// </summary>
    private Source? FindZone(MapId map, Vector2 centre, float size, EntityUid? standingOn, EntityUid? ship = null)
    {
        Source? best = null;
        var bestGap = float.MaxValue;
        foreach (var source in _sources)
        {
            if (source.Map != map)
                continue;

            float gap;
            if (source.Uid == standingOn
                || source.Reach <= 0f && ship is { } docker && docker != source.Uid && _docking.AreGridsDocked(docker, source.Uid))
                gap = -1f;
            else if (source.Reach <= 0f)
                continue; // Aboard only.
            else
                gap = (source.Centre - centre).Length() - size - source.Size;

            if (gap > source.Reach)
                continue;

            if (best is { } current && (current.Zone.Priority > source.Zone.Priority || current.Zone.Priority == source.Zone.Priority && bestGap <= gap))
                continue;

            best = source;
            bestGap = gap;
        }

        return best;
    }

    /// <summary>The ship or vehicle a person is aboard, which carries a Wilderness mark of its own, if any.</summary>
    public EntityUid? ShipOf(EntityUid mob)
    {
        var subject = Subject(mob);
        return subject == mob ? null : subject;
    }

    /// <summary>
    /// What carries a person through zones: the ship they're aboard, or themselves on foot, on a station or on a
    /// planet. A vehicle's crew go with the vehicle.
    /// </summary>
    private EntityUid Subject(EntityUid mob, int depth = 0)
    {
        var xform = Transform(mob);
        if (depth < 4
            && TryComp<VehicleInteriorLinkComponent>(xform.MapUid, out var link)
            && link.Vehicle != mob
            && !TerminatingOrDeleted(link.Vehicle))
            return Subject(link.Vehicle, depth + 1);

        if (xform.GridUid is { } grid && IsShip(grid))
            return grid;

        return mob;
    }

    /// <summary>
    /// Whether a grid is a ship that can be marked: someone's own, bought or a claimed wreck. Every grid has a shuttle
    /// component, so stations, asteroids and sites don't count; people on them are marked themselves.
    /// </summary>
    private bool IsShip(EntityUid grid)
    {
        if (HasComp<MapComponent>(grid) || !HasComp<MapGridComponent>(grid))
            return false;

        if (TryComp<WGZoneSourceComponent>(grid, out var source))
            return source.EndsWhenClaimed && Claimed(grid);

        return HasComp<ShuttleDeedComponent>(grid) || Claimed(grid);
    }

    private bool Claimed(EntityUid grid)
    {
        return TryComp<ClaimableGridComponent>(grid, out var claimable) && claimable.Claimed;
    }

    /// <summary>Where a thing is on its map, and the radius of the circle round it: a grid's box, or nothing.</summary>
    private (Vector2 Centre, float Size) Bounds(EntityUid uid, TransformComponent xform)
    {
        if (TryComp<MapGridComponent>(uid, out var grid) && !HasComp<MapComponent>(uid))
        {
            var box = grid.LocalAABB;
            return (Vector2.Transform(box.Center, _transform.GetWorldMatrix(xform)), box.Size.Length() / 2f);
        }

        return (_transform.GetWorldPosition(xform), 0f);
    }

    private string Name(EntityUid uid)
    {
        return MetaData(uid).EntityName;
    }

    private void UpdateRings()
    {
        foreach (var list in _rings.Values)
            list.Clear();

        foreach (var source in _sources)
        {
            if (!source.Rings || source.Reach <= 0f || _transform.GetMap(source.Uid) is not { } map)
                continue;

            if (!_rings.TryGetValue(map, out var list))
                _rings[map] = list = new List<WGZoneRing>();

            list.Add(new WGZoneRing(source.Centre, source.Size + source.Reach, source.Zone.ID));
        }

        foreach (var (map, list) in _rings.ToList())
        {
            if (TerminatingOrDeleted(map))
            {
                _rings.Remove(map);
                continue;
            }

            if (list.Count == 0)
            {
                RemComp<WGZoneRingsComponent>(map);
                _rings.Remove(map);
                continue;
            }

            var rings = EnsureComp<WGZoneRingsComponent>(map);
            if (rings.Rings.SequenceEqual(list))
                continue;

            rings.Rings = new List<WGZoneRing>(list);
            Dirty(map, rings);
        }
    }

    /// <summary>Puts a zone's centre at a point, such as a site on a planet that has no grid of its own.</summary>
    public EntityUid SpawnMarker(EntityCoordinates at, ProtoId<WGZonePrototype> zone, string name)
    {
        var marker = Spawn("WGZoneMarker", at);
        var source = EnsureComp<WGZoneSourceComponent>(marker);
        source.Zone = zone;
        _metaData.SetEntityName(marker, name);
        return marker;
    }
}
