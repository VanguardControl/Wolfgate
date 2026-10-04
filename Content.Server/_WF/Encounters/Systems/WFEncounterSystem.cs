using System.Linq;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Server.StationEvents.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Runs encounters: spawns each prototype's ships, loads their cargo, crews them and gives them their orders
/// through the NpcCrew module, decides when the encounter is over and removes its ships once players have left them.
/// </summary>
public sealed partial class WFEncounterSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private AdminVesselSpawnSystem _vessels = default!;
    [Dependency] private WFCrewSetupSystem _setup = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private GridPowerSystem _power = default!;
    [Dependency] private LinkedLifecycleGridSystem _lifecycle = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long after its orders are flown a transient encounter jumps out.</summary>
    private static readonly TimeSpan JumpDelay = TimeSpan.FromSeconds(20);

    private static readonly ProtoId<RadioChannelPrototype> AnnounceChannel = "Common";

    /// <summary>How long a ship must be without thrust, and out of any fight, before it calls for help.</summary>
    private static readonly TimeSpan AdriftDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DistressRepeat = TimeSpan.FromMinutes(5);

    /// <summary>How often a living crew top up their ship's batteries. Encounter ships don't burn fuel.</summary>
    private static readonly TimeSpan PowerInterval = TimeSpan.FromMinutes(2);

    private TimeSpan _nextPoll;
    private float _cleanupRange;
    private TimeSpan _cleanupDelay;
    private readonly List<MapCoordinates> _players = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, EncountersCVars.CleanupRange, value => _cleanupRange = value, true);
        Subs.CVar(_config, EncountersCVars.CleanupDelay, value => _cleanupDelay = TimeSpan.FromSeconds(value), true);
        SubscribeLocalEvent<WFCrewAlertEvent>(OnCrewAlert);
    }

    /// <summary>A hidden encounter shows itself once one of its crews raises the alarm.</summary>
    private void OnCrewAlert(ref WFCrewAlertEvent args)
    {
        if (TryComp<WFEncounterGridComponent>(args.Grid, out var marker))
            Reveal(marker.Encounter);
    }

    /// <summary>Puts a hidden encounter on the sector markers and lets it make its announcement.</summary>
    public void Reveal(Entity<WFEncounterComponent?> encounter)
    {
        if (!Resolve(encounter, ref encounter.Comp, false) || !encounter.Comp.Hidden)
            return;

        encounter.Comp.Hidden = false;
        if (encounter.Comp.Resolution == null)
            Announce(encounter.Comp);
    }

    private void Announce(WFEncounterComponent encounter)
    {
        if (encounter.Announcement is not { } text)
            return;

        encounter.Announcement = null;
        if (encounter.AnnounceOnRadio)
        {
            foreach (var ship in encounter.Ships.Values)
            {
                if (TrySay(ship, AnnounceChannel, text))
                    return;
            }
        }

        _chat.DispatchGlobalAnnouncement(text, encounter.AnnouncementSender);
    }

    /// <summary>Has a ship's radio officer, or failing that its captain, say something on a channel.</summary>
    public bool TrySay(WFEncounterShipState ship, ProtoId<RadioChannelPrototype> channel, string text)
    {
        if (TerminatingOrDeleted(ship.Grid))
            return false;

        EntityUid? speaker = null;
        var operators = EntityQueryEnumerator<WFRadioOperatorComponent, WFCrewComponent, TransformComponent>();
        while (operators.MoveNext(out var uid, out _, out var crew, out var xform))
        {
            if (crew.Group != ship.Group || xform.GridUid != ship.Grid || !_mobs.IsAlive(uid))
                continue;

            // The radio officer speaks for the ship while he lives.
            if (speaker == null || crew.Role == WFCrewRoles.RadioOperator)
                speaker = uid;
        }

        if (speaker is not { } voice)
            return false;

        _radio.SendRadioMessage(voice, text, channel, voice);
        return true;
    }

    /// <summary>Encounters that have not resolved yet.</summary>
    public int ActiveCount()
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            if (encounter.Resolution == null)
                count++;
        }

        return count;
    }

    /// <summary>The summed cost of the encounters that have not resolved yet.</summary>
    public int ActiveCost()
    {
        var cost = 0;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            if (encounter.Resolution == null)
                cost += encounter.Cost;
        }

        return cost;
    }

    /// <summary>Whether an encounter of this prototype has not resolved yet.</summary>
    public bool IsRunning(string prototype)
    {
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            if (encounter.Resolution == null && encounter.Prototype.Id == prototype)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Spawns an encounter with its origin at a point in space. <paramref name="stops"/> are the stations its
    /// placement chose: orders can aim at the first and last, and a route calls at each in turn. Fails, leaving
    /// nothing behind, when a ship cannot be loaded or crewed or its orders are invalid.
    /// </summary>
    public bool TrySpawn(WFEncounterPrototype prototype, MapCoordinates origin, out EntityUid encounter, EntityUid? spawner = null,
        IReadOnlyList<EntityUid>? stops = null)
    {
        encounter = default;
        if (origin.MapId == MapId.Nullspace || prototype.Ships.Count == 0)
            return false;

        var uid = Spawn(null, origin);
        var comp = AddComp<WFEncounterComponent>(uid);
        var designation = $"{(char) ('A' + _random.Next(26))}{(char) ('A' + _random.Next(26))}-{_random.Next(100, 1000)}";
        comp.Prototype = prototype.ID;
        comp.Name = Loc.GetString(prototype.Name, ("designation", designation));
        comp.Origin = origin;
        comp.Started = _timing.CurTime;
        comp.Expires = prototype.Duration > 0f && prototype.Lifetime == WFEncounterLifetime.Transient
            ? _timing.CurTime + TimeSpan.FromSeconds(prototype.Duration)
            : null;
        comp.Category = prototype.Category;
        comp.Cost = prototype.Cost;
        comp.Lifetime = prototype.Lifetime;
        comp.Hidden = prototype.Hidden;
        comp.AnnounceOnRadio = prototype.AnnounceOnRadio;
        if (stops != null)
            comp.Stops.AddRange(stops);
        _meta.SetEntityName(uid, comp.Name);

        WFEncounterManifestPrototype? manifest = null;
        if (prototype.Manifests.Count > 0)
            _prototypes.TryIndex(_random.Pick(prototype.Manifests), out manifest);
        foreach (var ship in prototype.Ships)
        {
            if (comp.Ships.ContainsKey(ship.Key) || !TrySpawnShip((uid, comp), ship, designation, manifest, spawner))
            {
                Log.Error($"Encounter {prototype.ID} could not spawn ship '{ship.Key}'.");
                Remove((uid, comp));
                return false;
            }
        }

        foreach (var ship in prototype.Ships)
        {
            var state = comp.Ships[ship.Key];
            var queue = ship.FlyRoute && prototype.Route is { } route && comp.Stops.Count > 0
                ? BuildRoute(comp, route)
                : BuildQueue(comp, ship);
            if (queue.Count == 0)
                continue;

            if (!_objectives.SetQueue(state.Grid, state.Group, queue))
            {
                Log.Error($"Encounter {prototype.ID} has invalid orders for ship '{ship.Key}'.");
                Remove((uid, comp));
                return false;
            }

            state.HasOrders = true;
        }

        if (prototype.Announcement is { } announcement)
        {
            comp.Announcement = Loc.GetString(announcement,
                ("name", comp.Name),
                ("origin", PlaceName(comp.Stops.Count > 0 ? comp.Stops[0] : null)),
                ("destination", PlaceName(prototype.Route != null && comp.Stops.Count > 0 ? comp.Stops[0] : comp.Stops.Count > 0 ? comp.Stops[^1] : null)),
                ("stops", comp.Stops.Count),
                ("cargo", Loc.GetString(manifest?.Name ?? "wf-encounter-cargo-none")));
            comp.AnnouncementSender = prototype.AnnouncementSender is { } sender ? Loc.GetString(sender) : null;
            // A hidden encounter announces itself when it is revealed.
            if (!comp.Hidden)
                Announce(comp);
        }

        Log.Info($"Encounter {prototype.ID} started as {ToPrettyString(uid)} at {origin} with {comp.Ships.Count} ships and {comp.Stops.Count} stops.");
        encounter = uid;
        var ev = new WFEncounterStartedEvent(uid);
        RaiseLocalEvent(uid, ref ev, true);
        return true;
    }

    /// <summary>The orders a ship's prototype gives it.</summary>
    private List<WFCrewObjective> BuildQueue(WFEncounterComponent comp, WFEncounterShip ship)
    {
        var queue = new List<WFCrewObjective>();
        foreach (var objective in ship.Objectives)
        {
            queue.Add(new WFCrewObjective
            {
                Kind = objective.Kind,
                Target = objective.Target switch
                {
                    "@origin" => comp.Stops.Count > 0 ? GetNetEntity(comp.Stops[0]) : null,
                    "@destination" => comp.Stops.Count > 0 ? GetNetEntity(comp.Stops[^1]) : null,
                    { } key when comp.Ships.TryGetValue(key, out var target) => GetNetEntity(target.Grid),
                    _ => null,
                },
                Position = comp.Origin.Position + objective.Offset,
                Range = objective.Range,
                Duration = objective.Duration,
            });
        }

        return queue;
    }

    /// <summary>A haul: dock at each stop, wait there, cast off, and after the last fly clear of it.</summary>
    private List<WFCrewObjective> BuildRoute(WFEncounterComponent comp, WFEncounterRoute route)
    {
        var queue = new List<WFCrewObjective>();
        foreach (var stop in comp.Stops)
        {
            queue.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.Dock, Target = GetNetEntity(stop) });
            queue.Add(new WFCrewObjective
            {
                Kind = WFCrewObjectiveKind.Hold,
                Duration = _random.NextFloat(route.DwellMin, MathF.Max(route.DwellMin, route.DwellMax)),
            });
            queue.Add(new WFCrewObjective { Kind = WFCrewObjectiveKind.Undock });
        }

        var last = _transform.GetMapCoordinates(comp.Stops[^1]).Position;
        queue.Add(new WFCrewObjective
        {
            Kind = WFCrewObjectiveKind.GoTo,
            Position = last + _random.NextAngle().ToVec() * route.ExitDistance,
            Range = 150f,
        });
        return queue;
    }

    private bool TrySpawnShip(Entity<WFEncounterComponent> encounter, WFEncounterShip ship, string designation,
        WFEncounterManifestPrototype? manifest, EntityUid? spawner)
    {
        ProtoId<VesselPrototype>? vesselId = ship.Vessels.Count > 0 ? _random.Pick(ship.Vessels) : ship.Vessel;
        if (vesselId is not { } id || !_prototypes.TryIndex(id, out var vessel)
            || !_vessels.TrySpawnVessel(vessel, encounter.Comp.Origin.MapId, encounter.Comp.Origin.Position + ship.Offset, spawner, out var spawned))
            return false;

        var grid = spawned.Value;
        var marker = AddComp<WFEncounterGridComponent>(grid);
        marker.Encounter = encounter;
        marker.Key = ship.Key;

        var name = Loc.GetString("wf-encounter-ship-name", ("vessel", vessel.Name), ("designation", designation));
        _meta.SetEntityName(grid, name);

        var state = new WFEncounterShipState
        {
            Grid = grid,
            Group = Capped($"enc{encounter.Owner.Id}-{ship.Key}", WFCrewLimits.MaxGroup),
            Side = ship.Side.Length > 0 ? ship.Side : ship.Key,
            WarnRange = ship.WarnRange,
            AttackRange = ship.AttackRange,
        };
        encounter.Comp.Ships.Add(ship.Key, state);

        // Cargo goes aboard first, so the crew are posted around it and not on it.
        if (manifest != null && manifest.Crates.Count > 0)
        {
            foreach (var tile in _planner.HoldTiles(grid, ship.Cargo))
            {
                Spawn(_random.Pick(manifest.Crates), tile);
            }
        }

        var mission = new WFCrewMission
        {
            Group = state.Group,
            Callsign = Capped(name, WFCrewLimits.MaxCallsign),
            Battlegroup = ship.Side.Length > 0 ? Capped($"enc{encounter.Owner.Id}-{ship.Side}", WFCrewLimits.MaxBattlegroup) : string.Empty,
            Profile = ship.Profile?.Id ?? string.Empty,
            Company = ship.Company?.Id ?? string.Empty,
            Faction = ship.Faction.Id,
            BoardingResponse = ship.Boarding,
            DockingResponse = ship.Docking,
            HeaveTo = ship.Evades,
            Disengage = ship.Disengage,
            DisengageRange = ship.DisengageRange,
            Skill = ship.Skill,
        };
        if (ship.Navigation is { } navigation && _prototypes.TryIndex(navigation, out var profile))
            mission.Navigation = profile.Settings.Clone();

        var posts = _setup.Plan(grid, ship.Deckhands + ship.Guards, ship.Captain);
        // The last deckhands of the plan are the ship's guards.
        var guards = ship.Guards;
        for (var i = posts.Count - 1; i >= 0 && guards > 0; i--)
        {
            if (posts[i].Role != WFCrewRoles.Deckhand.Id)
                continue;

            posts[i].Role = WFCrewRoles.Marine.Id;
            guards--;
        }

        if (posts.Count == 0 || !_setup.TrySpawn(grid, posts, mission, out _))
            return false;

        // Shipyard hulls come with empty reactors and flat batteries; the crew arrive with theirs charged.
        _power.SetPower(true, grid, false);
        state.NextPower = _timing.CurTime + PowerInterval;
        return true;
    }

    private string PlaceName(EntityUid? place)
    {
        return place is { } uid && !TerminatingOrDeleted(uid) ? MetaData(uid).EntityName : Loc.GetString("wf-encounter-open-space");
    }

    private static string Capped(string text, int length)
    {
        return text.Length <= length ? text : text[..length];
    }

    /// <summary>Marks an encounter as over. Its ships stay until players have left them, or jump out if it is transient.</summary>
    public void Resolve(Entity<WFEncounterComponent?> encounter, WFEncounterResolution resolution)
    {
        if (!Resolve(encounter, ref encounter.Comp, false) || encounter.Comp.Resolution != null)
            return;

        encounter.Comp.Resolution = resolution;
        if (resolution is WFEncounterResolution.Completed or WFEncounterResolution.Expired
            && encounter.Comp.Lifetime == WFEncounterLifetime.Transient)
            encounter.Comp.JumpAt = _timing.CurTime + JumpDelay;
        Log.Info($"Encounter {encounter.Comp.Prototype} {ToPrettyString(encounter)} resolved: {resolution}.");
        var ev = new WFEncounterResolvedEvent(encounter, resolution);
        RaiseLocalEvent(encounter, ref ev, true);
    }

    /// <summary>Ends an encounter now and removes its ships and crews, wherever players are.</summary>
    public void End(Entity<WFEncounterComponent?> encounter)
    {
        if (!Resolve(encounter, ref encounter.Comp, false))
            return;

        Resolve(encounter, WFEncounterResolution.Ended);
        Remove((encounter, encounter.Comp));
    }

    private void Remove(Entity<WFEncounterComponent> encounter)
    {
        foreach (var ship in encounter.Comp.Ships.Values)
        {
            RemoveShip(ship);
        }

        QueueDel(encounter);
    }

    private void RemoveShip(WFEncounterShipState ship)
    {
        if (TerminatingOrDeleted(ship.Grid))
            return;

        _setup.ClearCrew(ship.Grid, ship.Group);
        // Anyone still aboard is left in space, not deleted with the hull.
        _lifecycle.UnparentPlayersFromGrid(ship.Grid, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextPoll)
            return;

        _nextPoll = _timing.CurTime + PollInterval;
        List<WFCrewSetupCrew>? crews = null;
        var playersKnown = false;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Resolution == null)
            {
                SkipBlockedOrders(encounter);
                Tend(encounter);
                crews ??= _objectives.Snapshot();
                if (Judge(encounter, crews) is { } resolution)
                    Resolve((uid, encounter), resolution);
                continue;
            }

            if (!playersKnown)
            {
                FindPlayers();
                playersKnown = true;
            }

            if (!CleanUp(encounter))
                QueueDel(uid);
        }
    }

    /// <summary>Nobody is there to unstick an encounter ship: a stop it cannot dock at, or a target that is gone, is passed over.</summary>
    private void SkipBlockedOrders(WFEncounterComponent encounter)
    {
        foreach (var ship in encounter.Ships.Values)
        {
            if (!ship.HasOrders || TerminatingOrDeleted(ship.Grid)
                || _objectives.QueueStatus(ship.Grid, ship.Group) is not ("dock-failed" or "target-lost"))
                continue;

            Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} skips an order it cannot carry out.");
            _objectives.Control(ship.Grid, ship.Group, WFCrewSetupAction.Skip);
        }
    }

    /// <summary>
    /// What a living crew does for its ship between orders: keeps the batteries charged, and once the ship has been
    /// without thrust for a while with no fight going on, calls for help and marks the encounter as a distress.
    /// </summary>
    private void Tend(WFEncounterComponent encounter)
    {
        var now = _timing.CurTime;
        foreach (var ship in encounter.Ships.Values)
        {
            if (TerminatingOrDeleted(ship.Grid))
                continue;

            var fighting = _alerts.IsAlerted(ship.Grid, ship.Group);
            if (!_status.IsAdrift(ship.Grid) || fighting)
            {
                ship.AdriftSince = null;
                if (!fighting && now >= ship.NextPower && HasLivingCrew(ship))
                {
                    ship.NextPower = now + PowerInterval;
                    _power.SetPower(true, ship.Grid, false);
                }

                continue;
            }

            ship.AdriftSince ??= now;
            if (now - ship.AdriftSince < AdriftDelay || now < ship.NextDistress)
                continue;

            var position = _transform.GetMapCoordinates(ship.Grid).Position;
            if (!TrySay(ship, AnnounceChannel, Loc.GetString("wf-encounter-distress-adrift",
                    ("name", MetaData(ship.Grid).EntityName), ("x", (int) position.X), ("y", (int) position.Y))))
                continue;

            ship.NextDistress = now + DistressRepeat;
            encounter.Category = WFEncounterCategory.Distress;
            encounter.Hidden = false;
        }
    }

    private bool HasLivingCrew(WFEncounterShipState ship)
    {
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group == ship.Group && xform.GridUid == ship.Grid && _mobs.IsAlive(uid))
                return true;
        }

        return false;
    }

    /// <summary>How the encounter has ended, if it has.</summary>
    private WFEncounterResolution? Judge(WFEncounterComponent encounter, List<WFCrewSetupCrew> crews)
    {
        var sides = new HashSet<string>();
        var fighting = new HashSet<string>();
        var ordered = 0;
        var done = 0;
        foreach (var ship in encounter.Ships.Values)
        {
            sides.Add(ship.Side);
            var alive = !TerminatingOrDeleted(ship.Grid);
            if (alive && !_status.IsDisabled(ship.Grid))
                fighting.Add(ship.Side);
            if (!ship.HasOrders)
                continue;

            ordered++;
            var net = alive ? GetNetEntity(ship.Grid) : NetEntity.Invalid;
            if (crews.Any(crew => crew.Grid == net && crew.Group == ship.Group && crew.Objectives.Count == 0))
                done++;
        }

        if (fighting.Count == 0)
            return WFEncounterResolution.Destroyed;
        if (sides.Count > 1 && fighting.Count == 1)
            return WFEncounterResolution.Decided;
        // A persistent encounter stays for the round: finished orders and the clock don't end it.
        if (encounter.Lifetime == WFEncounterLifetime.Persistent)
            return null;
        if (ordered > 0 && done == ordered)
            return WFEncounterResolution.Completed;
        if (encounter.Expires is { } expires && _timing.CurTime >= expires)
            return WFEncounterResolution.Expired;
        return null;
    }

    private void FindPlayers()
    {
        _players.Clear();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (_mobs.IsAlive(uid))
                _players.Add(_transform.GetMapCoordinates(uid, xform));
        }
    }

    /// <summary>Removes the ships of a resolved encounter that players have left alone. False once none is left.</summary>
    private bool CleanUp(WFEncounterComponent encounter)
    {
        var remaining = false;
        foreach (var ship in encounter.Ships.Values)
        {
            if (TerminatingOrDeleted(ship.Grid))
                continue;

            if (encounter.JumpAt is { } jump)
            {
                if (_timing.CurTime >= jump)
                    RemoveShip(ship);
                else
                    remaining = true;
                continue;
            }

            var here = _transform.GetMapCoordinates(ship.Grid);
            var near = false;
            foreach (var player in _players)
            {
                if (player.MapId == here.MapId && (player.Position - here.Position).LengthSquared() <= _cleanupRange * _cleanupRange)
                {
                    near = true;
                    break;
                }
            }

            if (near)
                ship.Quiet = null;
            else
                ship.Quiet ??= _timing.CurTime;

            if (ship.Quiet is { } quiet && _timing.CurTime - quiet >= _cleanupDelay)
                RemoveShip(ship);
            else
                remaining = true;
        }

        return remaining;
    }
}
