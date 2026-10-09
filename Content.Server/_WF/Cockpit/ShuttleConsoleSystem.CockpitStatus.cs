using System.Linq;
using Content.Server._Mono.NPC.HTN;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.Cockpit;
using Content.Shared.Construction.Components;
using Content.Shared.NPC;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    private float _wfCockpitStatusAccumulator;
    private readonly Dictionary<EntityUid, bool?> _wfCockpitAutopilotStates = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _wfCockpitStatusViewers = new();

    private void InitializeWfCockpitStatus()
    {
        SubscribeLocalEvent<UserInterfaceComponent, BoundUIOpenedEvent>(OnWfCockpitUiOpened);
    }

    private void OnWfCockpitUiOpened(Entity<UserInterfaceComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(ShuttleConsoleUiKey.Key) || !HasComp<ShuttleConsoleComponent>(ent))
            return;
        if (!_wfCockpitStatusViewers.TryGetValue(ent.Owner, out var viewers))
            _wfCockpitStatusViewers[ent.Owner] = viewers = new HashSet<EntityUid>();
        viewers.Add(args.Actor);
        // The shield-only updater must also refresh a newly opened viewer's cached snapshot.
        _wfShieldHelmStates.Remove(ent.Owner);
    }

    /// <summary>Checks active steering rather than an old destination or the destination selection button.</summary>
    public bool? GetWfCockpitAutopilotStatus(EntityUid? console)
    {
        if (console is not { } uid || !TryComp<HTNComponent>(uid, out var htn) ||
            !TryComp<ShuttleConsoleComponent>(uid, out var helm))
            return null;
        return htn.Enabled && HasComp<ActiveNPCComponent>(uid) &&
            TryComp<ShipSteererComponent>(uid, out var steering) && steering.Status == ShipSteeringStatus.Moving &&
            htn.Blackboard.TryGetValue<EntityCoordinates>(helm.AutopilotTargetKey, out var target, EntityManager) &&
            !TerminatingOrDeleted(target.EntityId) &&
            TryComp<TransformComponent>(uid, out var transform) && transform.GridUid is { } grid &&
            (!HasComp<AnchorableComponent>(uid) || transform.Anchored) && this.IsPowered(uid, EntityManager) &&
            HasComp<ShuttleComponent>(grid) && TryComp<PilotedShuttleComponent>(grid, out var piloted) &&
            piloted.InputSources.Contains(uid);
    }

    /// <summary>Refreshes only changed autopilot lamps on open helms, independently of the selected MFD.</summary>
    private void UpdateWfCockpitStatuses(float frameTime)
    {
        _wfCockpitStatusAccumulator += frameTime;
        if (_wfCockpitStatusAccumulator < 0.25f)
            return;
        _wfCockpitStatusAccumulator = 0;
        var open = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key))
                continue;
            open.Add(uid);
            var selected = new ConsoleShuttleEvent { Console = uid };
            RaiseLocalEvent(uid, ref selected);
            var active = GetWfCockpitAutopilotStatus(selected.Console);
            if (!_wfCockpitAutopilotStates.TryGetValue(uid, out var previous) || previous != active)
            {
                _wfCockpitAutopilotStates[uid] = active;
                _ui.ServerSendUiMessage(uid, ShuttleConsoleUiKey.Key, new WFCockpitAutopilotUpdateMessage(active));
            }
            else if (_wfCockpitStatusViewers.TryGetValue(uid, out var viewers))
            {
                // A new viewer can have an older cached BUI snapshot while the lamp itself has not changed.
                foreach (var actor in viewers)
                    _ui.ServerSendUiMessage(uid, ShuttleConsoleUiKey.Key, new WFCockpitAutopilotUpdateMessage(active), actor);
            }
        }
        _wfCockpitStatusViewers.Clear();
        foreach (var uid in _wfCockpitAutopilotStates.Keys.Where(uid => !open.Contains(uid)).ToArray())
            _wfCockpitAutopilotStates.Remove(uid);
    }
}
