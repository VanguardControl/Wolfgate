using System.Linq;
using Content.Server._Mono.FireControl;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.ShipShields;
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
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private WFCrewSystem _crew = default!;

    private const string Vision = "VisionRadius";
    private const string AggroVision = "AggroVisionRadius";
    private static readonly TimeSpan Decay = TimeSpan.FromSeconds(60);
    private readonly Dictionary<(EntityUid Grid, string Group), Alert> _alerts = new();
    private readonly List<EntityUid> _expired = new();
    private TimeSpan _nextPoll;

    private bool _zoneReport;

    /// <summary>Whether a crew aboard a particular grid has an active shared alert.</summary>
    public bool IsAlerted(EntityUid grid, string group) => _alerts.ContainsKey((grid, group));

    /// <summary>True while the alert of a patrol zone report is being raised: a warning, not an attack on the ship.</summary>
    public bool InZoneReport => _zoneReport;

    /// <summary>Whether a vessel is hostile only because a patrol zone reported it, and has not fired on the ship.</summary>
    public bool IsZoneThreat(EntityUid grid, string group, EntityUid ship) =>
        _alerts.TryGetValue((grid, group), out var alert) && alert.Zone.Contains(ship);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewHullHitEvent>(OnHullHit);
        SubscribeLocalEvent<WFShipShieldAttackedEvent>(OnShieldHit);
    }

    /// <summary>Returns vessels with unexpired incoming fire or an active hostile docking response.</summary>
    public EntityUid[] GetHostileShips(EntityUid grid, string group)
    {
        if (TerminatingOrDeleted(grid) || !_alerts.TryGetValue((grid, group), out var alert))
            return Array.Empty<EntityUid>();
        var now = _timing.CurTime;
        var ships = new List<EntityUid>();
        foreach (var (ship, until) in alert.Vessels)
        {
            if (now < until && ValidShip(grid, ship, true))
                ships.Add(ship);
        }
        foreach (var ship in alert.DockingVessels)
        {
            if (!ships.Contains(ship) && EntityManager.System<WFCrewSecuritySystem>().IsHostileDockingTarget(grid, group, ship)
                && ValidShip(grid, ship, false))
                ships.Add(ship);
        }
        return ships.ToArray();
    }

    /// <summary>Explicit incoming fire permits retaliation even against a normally friendly company.</summary>
    public bool IsHostileShip(EntityUid grid, string group, EntityUid target)
    {
        if (TerminatingOrDeleted(grid) || !_alerts.TryGetValue((grid, group), out var alert))
            return false;
        var attacked = alert.Vessels.TryGetValue(target, out var until) && _timing.CurTime < until;
        if (!attacked && (!alert.DockingVessels.Contains(target)
                || !EntityManager.System<WFCrewSecuritySystem>().IsHostileDockingTarget(grid, group, target)))
            return false;
        return ValidShip(grid, target, attacked);
    }

    /// <summary>A formation partner stays an ally unless it fired on this ship within the attack window.</summary>
    private bool ValidShip(EntityUid grid, EntityUid ship, bool attacked)
    {
        return !TerminatingOrDeleted(ship) && !_status.ShouldDisengage(grid, ship)
            && Transform(ship).MapID == Transform(grid).MapID && (attacked || !_escorts.AreInFormation(grid, ship));
    }

    /// <summary>Whether a mob is one of a crew's shared alert targets.</summary>
    public bool IsSharedHostile(EntityUid grid, string group, EntityUid target) =>
        _alerts.TryGetValue((grid, group), out var alert) && alert.Hostiles.Contains(target);

    /// <summary>The mobs a crew's shared alert currently targets.</summary>
    public IReadOnlyCollection<EntityUid> GetSharedHostiles(EntityUid grid, string group) =>
        _alerts.TryGetValue((grid, group), out var alert) ? alert.Hostiles : (IReadOnlyCollection<EntityUid>) Array.Empty<EntityUid>();

    /// <summary>Whether a shared alert made this crewman hostile to the target.</summary>
    public bool SharesHostile(EntityUid member, EntityUid target)
    {
        foreach (var alert in _alerts.Values)
        {
            if (alert.Members.TryGetValue(member, out var memory) && memory.AddedHostiles.Contains(target))
                return true;
        }
        return false;
    }

    /// <summary>Ship weapons are fire-controlled or mounted on a grid; carried guns are personal.</summary>
    public bool IsShipWeapon(EntityUid? weapon)
    {
        if (weapon is not { } uid || TerminatingOrDeleted(uid))
            return false;
        if (HasComp<FireControllableComponent>(uid))
            return true;
        var xform = Transform(uid);
        return xform.GridUid is { } grid && xform.ParentUid == grid;
    }

    private void OnHullHit(ref WFCrewHullHitEvent args)
    {
        ReportAttack(args.Grid, args.AttackerGrid);
    }

    private void OnShieldHit(ref WFShipShieldAttackedEvent args)
    {
        // A handheld shot at a shield is not its grid's attack; only ship weapons raise ship alerts.
        if (!IsShipWeapon(args.Weapon))
            return;
        if ((args.Weapon ?? args.Shooter) is { } source
            && EntityManager.System<WFCrewFriendlyFireSystem>().Protected(source, args.Grid))
            return;
        ReportAttack(args.Grid, args.AttackerGrid);
    }

    private void ReportAttack(EntityUid grid, EntityUid attacker, string? group = null)
    {
        if (grid == attacker || !HasComp<MapGridComponent>(grid) || !HasComp<MapGridComponent>(attacker)
            || Transform(grid).MapID != Transform(attacker).MapID)
            return;
        // Grids without crew, escorts or battlegroups have nobody to alert.
        if (group == null && !_escorts.IsInvolved(grid))
            return;
        var formation = _escorts.GetFormation(grid);
        // A formation partner that fires on this ship becomes hostile to the victim's own crews only.
        var partner = formation.Contains(attacker);
        var recipients = new HashSet<(EntityUid Grid, string Group)>();
        if (group != null)
            recipients.Add((grid, group));
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var transform))
        {
            if ((crew.Post?.EntityId ?? transform.GridUid) is { } home && (partner ? home == grid : formation.Contains(home))
                && _mobState.IsAlive(uid) && !HasComp<ActorComponent>(uid))
                recipients.Add((home, crew.Group));
        }
        // Snapshot every recipient before captains change their flight orders in response.
        foreach (var recipient in recipients)
            AlertShip(recipient.Grid, recipient.Group, attacker);
    }

    /// <summary>Reports an explicit vessel threat to a crew and every ship in its escort formation.</summary>
    public void ReportShipThreat(EntityUid grid, string group, EntityUid attacker) => ReportAttack(grid, attacker, group);

    /// <summary>
    /// Reports an intruder in a patrol zone as a threat to answer. Unlike an attack it raises no mayday, and a real
    /// attack by the same ship later still does.
    /// </summary>
    public void ReportZoneThreat(EntityUid grid, string group, EntityUid intruder)
    {
        _zoneReport = true;
        try
        {
            ReportAttack(grid, intruder, group);
        }
        finally
        {
            _zoneReport = false;
        }
    }

    /// <summary>Raises a local docking alert without treating a changeable security policy as incoming fire.</summary>
    public void ReportDockingThreat(EntityUid grid, string group, EntityUid visitor) => AlertShip(grid, group, visitor, false);

    private void AlertShip(EntityUid grid, string group, EntityUid attacker, bool retaliation = true)
    {
        if (grid == attacker || !HasComp<MapGridComponent>(grid) || !HasComp<MapGridComponent>(attacker)
            || Transform(grid).MapID != Transform(attacker).MapID)
            return;
        var key = (grid, group);
        if (!_alerts.TryGetValue(key, out var alert))
            _alerts[key] = alert = new Alert();
        var now = _timing.CurTime;
        alert.LastTarget = now;
        alert.ExternalUntil = now + Decay;
        if (retaliation)
        {
            var live = alert.Vessels.TryGetValue(attacker, out var until) && now < until;
            // A vessel known only from a zone report is news again once it really attacks.
            var known = live && (_zoneReport || !alert.Zone.Contains(attacker));
            alert.Vessels[attacker] = now + Decay;
            if (!_zoneReport)
                alert.Zone.Remove(attacker);
            else if (!live)
                alert.Zone.Add(attacker);
            // A known attacker only extends its window.
            if (known)
                return;
        }
        else
            alert.DockingVessels.Add(attacker);
        var ev = new WFCrewAlertEvent(grid, group, new[] { attacker });
        RaiseLocalEvent(grid, ref ev, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextPoll)
            return;

        var now = _timing.CurTime;
        _nextPoll = now + TimeSpan.FromSeconds(1);
        var groups = new Dictionary<(EntityUid Grid, string Group), List<Entity<HTNComponent>>>();
        var query = EntityQueryEnumerator<WFCrewComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var htn, out var xform))
        {
            // Crew share alerts only aboard their own ship, so equal labels on other ships stay separate crews.
            if (!crew.ShareAlerts || crew.Engagement != WFCrewEngagement.OnSight || !htn.Enabled
                || !_mobState.IsAlive(uid) || HasComp<ActorComponent>(uid) || xform.GridUid is not { } grid
                || _crew.HomeGrid(uid, crew) != grid)
                continue;

            var key = (grid, crew.Group);
            if (!groups.TryGetValue(key, out var members))
                groups[key] = members = new List<Entity<HTNComponent>>();
            members.Add((uid, htn));
        }

        foreach (var (key, alert) in _alerts.ToArray())
        {
            _expired.Clear();
            foreach (var (ship, until) in alert.Vessels)
            {
                if (now >= until || TerminatingOrDeleted(ship))
                    _expired.Add(ship);
            }
            foreach (var ship in _expired)
            {
                alert.Vessels.Remove(ship);
                alert.Zone.Remove(ship);
            }
            alert.DockingVessels.RemoveWhere(ship => TerminatingOrDeleted(ship));

            if (!groups.TryGetValue(key, out var members))
            {
                foreach (var (uid, memory) in alert.Members)
                    Restore(uid, memory);
                alert.Members.Clear();
                if (now >= alert.ExternalUntil)
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
                {
                    targets.Add(target);
                    if (EntityManager.System<WFCrewWeaponSystem>().CanSee(member, target))
                        EntityManager.System<WFCrewCommsSystem>().Report(member, target);
                }
            }

            if (!_alerts.TryGetValue(key, out var alert))
            {
                if (targets.Count == 0)
                    continue;
                _alerts[key] = alert = new Alert();
            }

            if (targets.Count > 0)
                alert.LastTarget = now;
            else if (now >= alert.LastTarget + Decay)
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
            if (!EntityManager.System<WFCrewCommsSystem>().Knows(member, target)
                && !EntityManager.System<WFCrewWeaponSystem>().CanSee(member, target))
                continue;
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
        public TimeSpan ExternalUntil;
        /// <summary>Each attacking vessel and when its attack window closes.</summary>
        public readonly Dictionary<EntityUid, TimeSpan> Vessels = new();
        /// <summary>The attacking vessels known only from a patrol zone report, not from a hit.</summary>
        public readonly HashSet<EntityUid> Zone = new();
        public readonly HashSet<EntityUid> DockingVessels = new();
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
