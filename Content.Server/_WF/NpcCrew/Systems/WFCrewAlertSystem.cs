using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Shares live targets among on-sight crew aboard the same ship and restores awareness after an alert.</summary>
public sealed partial class WFCrewAlertSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const string Vision = "VisionRadius";
    private const string AggroVision = "AggroVisionRadius";
    private static readonly TimeSpan Decay = TimeSpan.FromSeconds(60);
    private readonly Dictionary<(EntityUid Grid, string Group), Alert> _alerts = new();
    private TimeSpan _nextPoll;

    /// <summary>Whether a crew aboard a particular grid has an active shared alert.</summary>
    public bool IsAlerted(EntityUid grid, string group) => _alerts.ContainsKey((grid, group));

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextPoll)
            return;

        _nextPoll = _timing.CurTime + TimeSpan.FromSeconds(1);
        var groups = new Dictionary<(EntityUid Grid, string Group), List<Entity<HTNComponent>>>();
        var query = EntityQueryEnumerator<WFCrewComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var htn, out var xform))
        {
            if (!crew.ShareAlerts || crew.Engagement != WFCrewEngagement.OnSight || !htn.Enabled
                || !_mobState.IsAlive(uid) || HasComp<ActorComponent>(uid) || xform.GridUid is not { } grid)
                continue;

            var key = (grid, crew.Group);
            if (!groups.TryGetValue(key, out var members))
                groups[key] = members = new List<Entity<HTNComponent>>();
            members.Add((uid, htn));
        }

        foreach (var (key, alert) in _alerts.ToArray())
        {
            if (!groups.TryGetValue(key, out var members))
            {
                Clear(key, alert);
                continue;
            }

            foreach (var (uid, memory) in alert.Members.ToArray())
            {
                if (members.Any(member => member.Owner == uid))
                    continue;
                Restore(uid, memory);
                alert.Members.Remove(uid);
            }
        }

        foreach (var (key, members) in groups)
        {
            var targets = new HashSet<EntityUid>();
            foreach (var member in members)
            {
                if (member.Comp.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager)
                    && ValidTarget(target, key.Grid) && target != member.Owner
                    && !_factions.IsEntityFriendly(member.Owner, target) && !_factions.IsIgnored(member.Owner, target))
                    targets.Add(target);
            }

            if (!_alerts.TryGetValue(key, out var alert))
            {
                if (targets.Count == 0)
                    continue;
                _alerts[key] = alert = new Alert();
            }

            if (targets.Count > 0)
                alert.LastTarget = _timing.CurTime;
            else if (_timing.CurTime >= alert.LastTarget + Decay)
            {
                Clear(key, alert);
                continue;
            }

            var fresh = targets.Except(alert.Hostiles).ToArray();
            alert.Hostiles.UnionWith(targets);
            alert.Hostiles.RemoveWhere(target => !ValidTarget(target, key.Grid));
            var range = TryComp<MapGridComponent>(key.Grid, out var gridComp) ? gridComp.LocalAABB.Size.Length() : 10f;
            foreach (var member in members)
                Share(member, alert, range);

            if (fresh.Length > 0)
            {
                var ev = new WFCrewAlertEvent(key.Grid, key.Group, fresh);
                RaiseLocalEvent(key.Grid, ref ev, true);
            }
        }
    }

    /// <summary>Only living targets still aboard the alerted ship are shared.</summary>
    private bool ValidTarget(EntityUid target, EntityUid grid)
    {
        return !TerminatingOrDeleted(target) && _mobState.IsAlive(target)
               && TryComp(target, out TransformComponent? xform) && xform.GridUid == grid;
    }

    /// <summary>Extends awareness without replacing combat targets or the crewman's duty.</summary>
    private void Share(Entity<HTNComponent> member, Alert alert, float range)
    {
        var board = member.Comp.Blackboard;
        var changed = false;
        if (!alert.Members.TryGetValue(member.Owner, out var memory))
        {
            memory = new Awareness
            {
                Vision = board.ContainsKey(Vision) ? board.GetValue<float>(Vision) : null,
                AggroVision = board.ContainsKey(AggroVision) ? board.GetValue<float>(AggroVision) : null,
            };
            alert.Members[member.Owner] = memory;
            changed = true;
        }

        foreach (var target in memory.AddedHostiles.ToArray())
        {
            if (alert.Hostiles.Contains(target) && !_factions.IsEntityFriendly(member.Owner, target)
                && !_factions.IsIgnored(member.Owner, target))
                continue;
            RemoveSharedHostile(member.Owner, target);
            memory.AddedHostiles.Remove(target);
        }

        foreach (var target in alert.Hostiles)
        {
            if (target == member.Owner || _factions.IsEntityFriendly(member.Owner, target)
                || _factions.IsIgnored(member.Owner, target) || _factions.GetHostiles(member.Owner).Contains(target))
                continue;
            _factions.AggroEntity(member.Owner, target);
            memory.AddedHostiles.Add(target);
            changed = true;
        }

        foreach (var name in new[] { Vision, AggroVision })
        {
            if (board.GetValueOrDefault<float>(name, EntityManager) >= range)
                continue;
            board.SetValue(name, range);
            changed = true;
        }
        if (changed)
            _htn.Replan(member.Comp);
    }

    /// <summary>Removes only hostility introduced by sharing, preserving an unexpired personal retaliation.</summary>
    private void RemoveSharedHostile(EntityUid uid, EntityUid target)
    {
        if (TryComp<NPCRetaliationComponent>(uid, out var retaliation))
        {
            foreach (var (attacker, expiry) in retaliation.AttackMemories)
            {
                if (attacker == target && expiry > _timing.CurTime)
                    return;
            }
        }
        _factions.DeAggroEntity(uid, target);
    }

    /// <summary>Restores the member's original local vision overrides and removes shared hostility.</summary>
    private void Restore(EntityUid uid, Awareness memory)
    {
        if (TerminatingOrDeleted(uid))
            return;
        foreach (var target in memory.AddedHostiles)
            RemoveSharedHostile(uid, target);
        if (!TryComp<HTNComponent>(uid, out var htn))
            return;
        RestoreRange(htn.Blackboard, Vision, memory.Vision);
        RestoreRange(htn.Blackboard, AggroVision, memory.AggroVision);
        _htn.Replan(htn);
    }

    /// <summary>Restores a local value or falls back to the blackboard default.</summary>
    private static void RestoreRange(NPCBlackboard board, string key, float? value)
    {
        if (value is { } range)
            board.SetValue(key, range);
        else
            board.Remove<float>(key);
    }

    /// <summary>Ends one ship's alert and broadcasts one all-clear for that group.</summary>
    private void Clear((EntityUid Grid, string Group) key, Alert alert)
    {
        _alerts.Remove(key);
        foreach (var (uid, memory) in alert.Members)
            Restore(uid, memory);
        if (TerminatingOrDeleted(key.Grid))
            return;
        var ev = new WFCrewAlertClearedEvent(key.Grid, key.Group);
        RaiseLocalEvent(key.Grid, ref ev, true);
    }

    /// <summary>Targets and affected crew for one ship and group.</summary>
    private sealed class Alert
    {
        public TimeSpan LastTarget;
        public readonly HashSet<EntityUid> Hostiles = new();
        public readonly Dictionary<EntityUid, Awareness> Members = new();
    }

    /// <summary>The awareness and hostility changes owned by this alert.</summary>
    private sealed class Awareness
    {
        public float? Vision;
        public float? AggroVision;
        public readonly HashSet<EntityUid> AddedHostiles = new();
    }
}
