using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Content.Shared._Mono.Company;
using Content.Shared.NPC.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Runs editable objective queues for each ship and crew group, independently of player proximity.</summary>
public sealed partial class WFCrewObjectiveSystem : EntitySystem
{
    [Dependency] private WFPilotDutySystem _pilots = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WFCrewWorkSystem _work = default!;
    [Dependency] private WFCaptainSystem _captains = default!;
    private readonly Dictionary<(EntityUid Grid, string Group), QueueState> _queues = new();
    private float _timer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPilotDockFailedEvent>(OnDockFailed);
    }

    private void OnDockFailed(ref WFPilotDockFailedEvent ev)
    {
        if (TryComp<WFCrewComponent>(ev.Mob, out var crew)
            && _queues.TryGetValue((ev.Grid, crew.Group), out var state))
        {
            state.Paused = true;
            state.Status = "dock-failed";
        }
    }

    /// <summary>Validates the entire replacement or append before altering the running queue.</summary>
    public bool SetQueue(EntityUid grid, string group, List<WFCrewObjective> objectives, bool append = false)
    {
        if (!HasComp<MapGridComponent>(grid) || Transform(grid).MapUid == null || group.Length > 64 || objectives.Count > 64
            || objectives.Any(item => !Valid(grid, item)))
            return false;
        var key = (grid, group);
        if (!_queues.TryGetValue(key, out var state))
            state = new QueueState();
        if (append && state.Items.Count + objectives.Count > 64)
            return false;
        if (!append)
        {
            HoldCrew(grid, group);
            state = new QueueState();
        }
        state.Items.AddRange(objectives);
        _queues[key] = state;
        return true;
    }

    private bool Valid(EntityUid grid, WFCrewObjective item)
    {
        if (!Enum.IsDefined(item.Kind) || !float.IsFinite(item.Range) || item.Range is < 1 or > 5000
            || !float.IsFinite(item.Duration) || item.Duration is < 0 or > 86400
            || !float.IsFinite(item.Position.X) || !float.IsFinite(item.Position.Y))
            return false;
        if (item.Kind is WFCrewObjectiveKind.Hold or WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Undock or WFCrewObjectiveKind.Repair)
            return true;
        return item.Target is { } net && TryGetEntity(net, out var target) && target is { } uid && uid != grid
            && HasComp<MapGridComponent>(uid) && Transform(uid).MapUid == Transform(grid).MapUid;
    }

    /// <summary>Pauses, resumes or skips the current task without changing later tasks.</summary>
    public void Control(EntityUid grid, string group, WFCrewSetupAction action)
    {
        if (!_queues.TryGetValue((grid, group), out var state))
            return;
        HoldCrew(grid, group);
        state.Started = false;
        state.Pilot = null;
        if (action == WFCrewSetupAction.Skip && state.Items.Count > 0)
        {
            state.Items.RemoveAt(0);
            state.Elapsed = 0;
        }
        state.Paused = action == WFCrewSetupAction.Pause;
        state.Status = state.Paused ? "paused" : "pending";
    }

    /// <summary>Cancels queued orders when an admin issues an immediate mission or clears the crew.</summary>
    public void Cancel(EntityUid grid, string group)
    {
        _work.Cancel(grid, group);
        _queues.Remove((grid, group));
    }

    /// <summary>Whether an admin explicitly ordered this crew to engage a grid.</summary>
    public bool IsAttackTarget(EntityUid grid, string group, EntityUid target)
    {
        return _queues.TryGetValue((grid, group), out var state) && !state.Paused && state.Items.Count > 0
            && state.Items[0].Kind == WFCrewObjectiveKind.Attack && state.Items[0].Target == GetNetEntity(target);
    }

    /// <summary>Returns the explicitly assigned attack target while its task is running.</summary>
    public EntityUid? AttackTarget(EntityUid grid, string group)
    {
        if (_queues.TryGetValue((grid, group), out var state) && !state.Paused && state.Items.Count > 0
            && state.Items[0].Kind == WFCrewObjectiveKind.Attack && state.Items[0].Target is { } net
            && TryGetEntity(net, out var target) && target is { } uid && !TerminatingOrDeleted(uid))
            return uid;
        return null;
    }

    private EntityUid? Pilot(EntityUid grid, string group)
    {
        var query = EntityQueryEnumerator<WFCrewComponent, WFPilotDutyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out _, out var transform))
        {
            if (transform.GridUid == grid && crew.Group == group && crew.Duty == WFCrewDuties.Pilot
                && _mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid))
                return uid;
        }
        return null;
    }

    private void HoldCrew(EntityUid grid, string group)
    {
        _work.Cancel(grid, group);
        if (Pilot(grid, group) is { } pilot)
            _pilots.Hold(pilot);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 0.5f)
            return;
        var elapsed = _timer;
        _timer = 0;
        foreach (var (key, state) in _queues.ToArray())
        {
            var (grid, group) = key;
            if (TerminatingOrDeleted(grid))
            {
                _queues.Remove(key);
                continue;
            }
            if (state.Paused || state.Items.Count == 0)
                continue;
            if (Pilot(grid, group) is not { } pilot)
            {
                state.Status = "no-pilot";
                state.Started = false;
                continue;
            }
            var item = state.Items[0];
            if (!Valid(grid, item))
            {
                HoldCrew(grid, group);
                state.Paused = true;
                state.Status = "target-lost";
                continue;
            }
            var target = item.Target is { } net ? GetEntity(net) : EntityUid.Invalid;
            if (!state.Started || state.Pilot != pilot)
            {
                Start(pilot, grid, target, item);
                state.Started = true;
                state.Pilot = pilot;
                state.Status = "running";
            }
            var duty = Comp<WFPilotDutyComponent>(pilot);
            if (!duty.AtHelm)
            {
                state.Status = "awaiting-helm";
                continue;
            }
            if (_captains.IsCourseSuspended(pilot))
            {
                state.Status = "evading";
                continue;
            }
            state.Status = "running";
            state.Elapsed += elapsed;
            if (item.Kind is WFCrewObjectiveKind.Repair or WFCrewObjectiveKind.Resupply or WFCrewObjectiveKind.Salvage)
            {
                if (item.Kind != WFCrewObjectiveKind.Repair && !duty.OrdersCompleted)
                    continue;
                state.Status = _work.Advance(grid, group, item.Kind, target);
                if (state.Status != "complete")
                    continue;
                state.Items.RemoveAt(0);
                state.Started = false;
                state.Elapsed = 0;
                continue;
            }
            var completesOnArrival = item.Kind is WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Dock or WFCrewObjectiveKind.Undock or WFCrewObjectiveKind.Retreat;
            if (completesOnArrival ? duty.OrdersCompleted : item.Duration > 0 && state.Elapsed >= item.Duration)
            {
                _pilots.Hold(pilot);
                state.Items.RemoveAt(0);
                state.Started = false;
                state.Elapsed = 0;
                state.Status = state.Items.Count == 0 ? "complete" : "pending";
            }
        }
    }

    private void Start(EntityUid pilot, EntityUid grid, EntityUid target, WFCrewObjective item)
    {
        var duty = Comp<WFPilotDutyComponent>(pilot);
        duty.ArrivalRange = item.Range;
        switch (item.Kind)
        {
            case WFCrewObjectiveKind.Hold: _pilots.Hold(pilot); break;
            case WFCrewObjectiveKind.Repair: _pilots.Hold(pilot); break;
            case WFCrewObjectiveKind.Resupply:
            case WFCrewObjectiveKind.Salvage: _pilots.Dock(pilot, target); break;
            case WFCrewObjectiveKind.GoTo:
                _pilots.GoTo(pilot, new List<EntityCoordinates> { new(Transform(grid).MapUid!.Value, item.Position) }); break;
            case WFCrewObjectiveKind.Dock: _pilots.Dock(pilot, target); break;
            case WFCrewObjectiveKind.Undock: _pilots.Undock(pilot); break;
            case WFCrewObjectiveKind.Follow: _pilots.Follow(pilot, target, item.Range); break;
            case WFCrewObjectiveKind.Escort: _pilots.Escort(pilot, target, item.Range); break;
            case WFCrewObjectiveKind.Loiter:
            case WFCrewObjectiveKind.Attack:
                _pilots.Loiter(pilot, new EntityCoordinates(target, Vector2.Zero), item.Range); break;
            case WFCrewObjectiveKind.Retreat:
                _pilots.GoTo(pilot, new List<EntityCoordinates> { new(target, Vector2.Zero) }); break;
        }
    }

    /// <summary>Reports all living and downed crews, including crews without queued objectives.</summary>
    public List<WFCrewSetupCrew> Snapshot()
    {
        var result = new Dictionary<(EntityUid Grid, string Group), WFCrewSetupCrew>();
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var transform))
        {
            if ((_work.HomeGrid(uid) ?? transform.GridUid) is not { } grid)
                continue;
            var key = (grid, crew.Group);
            if (!result.TryGetValue(key, out var row))
            {
                row = new WFCrewSetupCrew { Grid = GetNetEntity(grid), Group = crew.Group, Status = Loc.GetString("wf-crew-objective-status-manual") };
                if (_queues.TryGetValue(key, out var state))
                {
                    row.Objectives = state.Items.ToList();
                    row.Status = Loc.GetString($"wf-crew-objective-status-{state.Status}");
                }
                result.Add(key, row);
            }
            row.Members++;
            row.Settings.Group = crew.Group;
            if (TryComp<CompanyComponent>(uid, out var company))
                row.Settings.Company = company.CompanyName.Id;
            if (TryComp<NpcFactionMemberComponent>(uid, out var faction))
                row.Settings.Faction = faction.Factions.FirstOrDefault().Id ?? "WFCrew";
            if (TryComp<WFCrewSecurityComponent>(uid, out var security))
            {
                row.Settings.BoardingResponse = security.Boarding;
                row.Settings.DockingResponse = security.Docking;
            }
            if (TryComp<WFCaptainComponent>(uid, out var captain))
                row.Settings.HeaveTo = captain.HeaveTo;
            if (TryComp<WFRadioOperatorComponent>(uid, out var radio))
            {
                row.Settings.Callsign = radio.Callsign ?? string.Empty;
                row.Settings.LocalChannel = radio.LocalChannel.Id;
                row.Settings.AlertChannel = radio.AlertChannel.Id;
            }
            if (_mobs.IsAlive(uid))
                row.Alive++;
        }
        return result.Values.ToList();
    }

    private sealed class QueueState
    {
        public readonly List<WFCrewObjective> Items = new();
        public bool Started;
        public bool Paused;
        public float Elapsed;
        public EntityUid? Pilot;
        public string Status = "pending";
    }
}
