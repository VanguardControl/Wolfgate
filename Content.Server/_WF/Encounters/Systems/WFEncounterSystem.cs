using System.Linq;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Chat.Systems;
using Content.Server.StationEvents.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Runs encounters: spawns each prototype's ships, crews them and gives them their orders through the NpcCrew
/// module, decides when the encounter is over and removes its ships once players have left them.
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
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private LinkedLifecycleGridSystem _lifecycle = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextPoll;
    private float _cleanupRange;
    private TimeSpan _cleanupDelay;
    private readonly List<MapCoordinates> _players = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, EncountersCVars.CleanupRange, value => _cleanupRange = value, true);
        Subs.CVar(_config, EncountersCVars.CleanupDelay, value => _cleanupDelay = TimeSpan.FromSeconds(value), true);
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

    /// <summary>
    /// Spawns an encounter with its origin at a point in space. Fails, leaving nothing behind, when a ship cannot
    /// be loaded or crewed.
    /// </summary>
    public bool TrySpawn(WFEncounterPrototype prototype, MapCoordinates origin, out EntityUid encounter, EntityUid? spawner = null)
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
        comp.Expires = prototype.Duration > 0f ? _timing.CurTime + TimeSpan.FromSeconds(prototype.Duration) : null;
        _meta.SetEntityName(uid, comp.Name);

        foreach (var ship in prototype.Ships)
        {
            if (comp.Ships.ContainsKey(ship.Key) || !TrySpawnShip((uid, comp), ship, designation, spawner))
            {
                Log.Error($"Encounter {prototype.ID} could not spawn ship '{ship.Key}'.");
                Remove((uid, comp));
                return false;
            }
        }

        foreach (var ship in prototype.Ships)
        {
            if (ship.Objectives.Count == 0)
                continue;

            var state = comp.Ships[ship.Key];
            var queue = new List<WFCrewObjective>();
            foreach (var objective in ship.Objectives)
            {
                queue.Add(new WFCrewObjective
                {
                    Kind = objective.Kind,
                    Target = objective.Target != null && comp.Ships.TryGetValue(objective.Target, out var target)
                        ? GetNetEntity(target.Grid)
                        : null,
                    Position = origin.Position + objective.Offset,
                    Range = objective.Range,
                    Duration = objective.Duration,
                });
            }

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
            _chat.DispatchGlobalAnnouncement(Loc.GetString(announcement, ("name", comp.Name)),
                prototype.AnnouncementSender is { } sender ? Loc.GetString(sender) : null);
        }

        Log.Info($"Encounter {prototype.ID} started as {ToPrettyString(uid)} at {origin} with {comp.Ships.Count} ships.");
        encounter = uid;
        var ev = new WFEncounterStartedEvent(uid);
        RaiseLocalEvent(uid, ref ev, true);
        return true;
    }

    private bool TrySpawnShip(Entity<WFEncounterComponent> encounter, WFEncounterShip ship, string designation, EntityUid? spawner)
    {
        if (!_prototypes.TryIndex(ship.Vessel, out var vessel)
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
        };
        encounter.Comp.Ships.Add(ship.Key, state);

        var mission = new WFCrewMission
        {
            Group = state.Group,
            Callsign = Capped(name, WFCrewLimits.MaxCallsign),
            Battlegroup = ship.Side.Length > 0 ? Capped($"enc{encounter.Owner.Id}-{ship.Side}", WFCrewLimits.MaxBattlegroup) : string.Empty,
            Company = ship.Company?.Id ?? string.Empty,
            Faction = ship.Faction.Id,
            BoardingResponse = ship.Boarding,
            DockingResponse = ship.Docking,
            HeaveTo = ship.Evades,
            Disengage = ship.Disengage,
            DisengageRange = ship.DisengageRange,
        };
        if (ship.Navigation is { } navigation && _prototypes.TryIndex(navigation, out var profile))
            mission.Navigation = profile.Settings.Clone();

        var posts = _setup.Plan(grid, ship.Deckhands, ship.Captain);
        return posts.Count > 0 && _setup.TrySpawn(grid, posts, mission, out _);
    }

    private static string Capped(string text, int length)
    {
        return text.Length <= length ? text : text[..length];
    }

    /// <summary>Marks an encounter as over. Its ships stay until players have left them.</summary>
    public void Resolve(Entity<WFEncounterComponent?> encounter, WFEncounterResolution resolution)
    {
        if (!Resolve(encounter, ref encounter.Comp, false) || encounter.Comp.Resolution != null)
            return;

        encounter.Comp.Resolution = resolution;
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
