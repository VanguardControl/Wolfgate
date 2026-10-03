using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Repairable;
using Content.Shared.Stacks;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Assigns physical repair and cargo jobs to deck crew using normal hands, tools and walking.</summary>
public sealed partial class WFCrewWorkSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedToolSystem _tools = default!;
    [Dependency] private WFCrewWeaponSystem _weapons = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFCrewEvaSystem _eva = default!;
    private readonly Dictionary<EntityUid, Job> _jobs = new();

    /// <summary>Incapacitated or dead workers cannot continue their assigned interaction.</summary>
    public void CancelWorker(EntityUid worker) => _jobs.Remove(worker);

    /// <summary>Workers collecting cargo remain listed with their ship while visiting the supply grid.</summary>
    public EntityUid? HomeGrid(EntityUid worker) => _jobs.TryGetValue(worker, out var job) ? job.Grid : null;

    /// <summary>Stops assigning work when the corresponding order is cancelled or paused.</summary>
    public void Cancel(EntityUid grid, string group)
    {
        foreach (var (worker, job) in _jobs.ToArray())
        {
            if (job.Grid == grid && job.Group == group)
            {
                if (job.Returning && !TerminatingOrDeleted(worker))
                    _hands.TryDrop(worker, job.Target);
                if (job.Tool is { } tool && TryComp<WelderComponent>(tool, out var welder) && welder.Enabled)
                    _tools.TurnOff((tool, welder), worker);
                _jobs.Remove(worker);
            }
        }
    }

    /// <summary>Assigns the next job, returning a localized status key suffix or complete.</summary>
    public string Advance(EntityUid grid, string group, WFCrewObjectiveKind kind, EntityUid source)
    {
        foreach (var (worker, job) in _jobs.ToArray())
        {
            if (job.Grid != grid || job.Group != group)
                continue;
            if (TerminatingOrDeleted(worker) || !_mobs.IsAlive(worker) || HasComp<ActorComponent>(worker))
            {
                _jobs.Remove(worker);
                continue;
            }
            return job.Failed ? "work-blocked" : "working";
        }

        EntityUid? chosen = null;
        var crewQuery = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crewQuery.MoveNext(out var uid, out var crew, out var xform))
        {
            if (xform.GridUid == grid && crew.Group == group && crew.Role == WFCrewRoles.Deckhand && crew.Duty == WFCrewDuties.Guard
                && _mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid) && !_weapons.HasLiveThreat(uid))
            {
                chosen = uid;
                break;
            }
        }
        if (chosen is not { } mob)
            return "no-worker";
        if (!_eva.Prepare(mob))
            return "no-eva";

        var home = Comp<WFCrewComponent>(mob).Post ?? Transform(mob).Coordinates;
        if (kind == WFCrewObjectiveKind.Repair)
        {
            if (HasComp<ShipRepairToolComponent>(mob) && MissingStructure(grid).FirstOrDefault() is { } missing)
            {
                _jobs[mob] = new Job(grid, group, grid, home, null, _timing.CurTime) { SrdCoordinates = missing };
                return "working";
            }
            var targets = EntityQueryEnumerator<RepairableComponent, DamageableComponent, TransformComponent>();
            while (targets.MoveNext(out var target, out var repair, out var damage, out var xform))
            {
                if (xform.GridUid != grid || damage.TotalDamage <= 0 || HasComp<MobStateComponent>(target))
                    continue;
                var tool = FindTool(mob, grid, repair);
                if (tool == null)
                    return "no-tool";
                _jobs[mob] = new Job(grid, group, target, home, tool, _timing.CurTime);
                return "working";
            }
            return "complete";
        }

        var items = EntityQueryEnumerator<TransformComponent>();
        while (items.MoveNext(out var item, out var transform))
        {
            if (transform.ParentUid != source || transform.Anchored)
                continue;
            if (kind == WFCrewObjectiveKind.Salvage ? !HasComp<StackComponent>(item)
                : !(HasComp<BallisticAmmoProviderComponent>(item) && !HasComp<GunComponent>(item) && _weapons.AmmoCount(item) > 0
                    || TryComp<Content.Shared.Atmos.Components.GasTankComponent>(item, out var tank) && tank.Air.Pressure > 600
                    && tank.Air.GetMoles(Content.Shared.Atmos.Gas.Oxygen) > 0))
                continue;
            _jobs[mob] = new Job(grid, group, item, home, null, _timing.CurTime);
            return "working";
        }
        return "complete";
    }

    private EntityUid? FindTool(EntityUid mob, EntityUid grid, RepairableComponent repair)
    {
        if (repair.Qualities.Any(quality => _tools.HasQuality(mob, quality)))
            return mob;
        foreach (var held in _hands.EnumerateHeld(mob))
        {
            if (repair.Qualities.Any(quality => _tools.HasQuality(held, quality)))
                return held;
        }
        var query = EntityQueryEnumerator<ToolComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var tool, out var xform))
        {
            if (xform.ParentUid == grid && repair.Qualities.Any(quality => _tools.HasQuality(uid, quality, tool)))
                return uid;
        }
        return null;
    }

    /// <summary>Provides the next walking destination without moving the worker or the cargo.</summary>
    public bool Destination(EntityUid mob, out EntityCoordinates coordinates)
    {
        if (_eva.TryGetSafetyDestination(mob, out coordinates))
            return true;
        coordinates = default;
        if (!_jobs.TryGetValue(mob, out var job) || job.Failed || HasComp<ActorComponent>(mob) || !_mobs.IsAlive(mob))
            return false;
        if (!_eva.Prepare(mob))
        {
            job.Failed = true;
            return false;
        }
        if (_timing.CurTime - job.Started > TimeSpan.FromSeconds(120))
        {
            job.Failed = true;
            return false;
        }
        if (job.Returning)
        {
            coordinates = job.Home;
            return true;
        }
        if (job.SrdCoordinates is { } repairCoordinates)
        {
            if (!MissingStructure(job.Grid).Any(position => position == repairCoordinates))
            {
                _jobs.Remove(mob);
                return false;
            }
            coordinates = repairCoordinates;
            return true;
        }
        var target = job.Tool is { } tool && tool != mob && !_hands.IsHolding(mob, tool, out _) ? tool : job.Target;
        if (TerminatingOrDeleted(target))
        {
            _jobs.Remove(mob);
            return false;
        }
        coordinates = Transform(target).Coordinates;
        return true;
    }

    /// <summary>Performs a job step only after ordinary navigation reaches its interaction range.</summary>
    public bool Perform(EntityUid mob)
    {
        if (!_jobs.TryGetValue(mob, out var job) || !Destination(mob, out var destination))
            return true;
        EntityManager.System<WFCrewSpeechSystem>().Say(mob,
            job.SrdCoordinates != null || job.Tool != null ? "repair" : job.Returning ? "return-cargo" : "collect");
        if (!Transform(mob).Coordinates.InRange(EntityManager, destination, 1.5f))
            return true;
        if (job.SrdCoordinates != null || job.Returning)
        {
            if (!_interaction.InRangeUnobstructed(mob, destination, range: 1.5f))
                return true;
        }
        else
        {
            var target = job.Tool is { } needed && needed != mob && !_hands.IsHolding(mob, needed, out _) ? needed : job.Target;
            if (!_interaction.InRangeUnobstructed(mob, target, range: 1.5f))
                return true;
        }
        if (job.SrdCoordinates is { } repairCoordinates)
        {
            if (!MissingStructure(job.Grid).Any(position => position == repairCoordinates))
            {
                _jobs.Remove(mob);
                return true;
            }
            if (_timing.CurTime >= job.NextUse)
            {
                RaiseLocalEvent(mob, new AfterInteractEvent(mob, mob, null, repairCoordinates, true));
                job.NextUse = _timing.CurTime + TimeSpan.FromSeconds(2);
            }
            return false;
        }
        if (job.Returning)
        {
            if (_hands.TryDrop(mob, job.Target))
                _jobs.Remove(mob);
            else
                job.Failed = true;
            return true;
        }
        if (job.Tool is { } tool)
        {
            if (tool != mob && !_hands.IsHolding(mob, tool, out _))
            {
                if (!_hands.TryPickupAnyHand(mob, tool))
                    job.Failed = true;
                return true;
            }
            if (!TryComp<DamageableComponent>(job.Target, out var damage) || damage.TotalDamage <= 0)
            {
                if (TryComp<WelderComponent>(tool, out var doneWelder))
                    _tools.TurnOff((tool, doneWelder), mob);
                _jobs.Remove(mob);
                return true;
            }
            if (_timing.CurTime < job.NextUse)
                return false;
            if (TryComp<WelderComponent>(tool, out var welder) && !welder.Enabled)
                _tools.TurnOn((tool, welder), mob);
            if (tool == mob)
            {
                var use = new InteractUsingEvent(mob, mob, job.Target, Transform(job.Target).Coordinates);
                RaiseLocalEvent(job.Target, use);
            }
            else
            {
                _hands.TrySelect(mob, tool);
                _interaction.InteractUsing(mob, tool, job.Target, Transform(job.Target).Coordinates);
            }
            job.NextUse = _timing.CurTime + TimeSpan.FromSeconds(Comp<RepairableComponent>(job.Target).DoAfterDelay + 2);
            return false;
        }
        if (!_hands.TryPickupAnyHand(mob, job.Target))
        {
            job.Failed = true;
            return true;
        }
        job.Returning = true;
        return true;
    }

    private sealed class Job(EntityUid grid, string group, EntityUid target,
        EntityCoordinates home, EntityUid? tool, TimeSpan started)
    {
        public readonly EntityUid Grid = grid;
        public readonly string Group = group;
        public readonly EntityUid Target = target;
        public readonly EntityUid? Tool = tool;
        public readonly EntityCoordinates Home = home;
        public readonly TimeSpan Started = started;
        public TimeSpan NextUse;
        public EntityCoordinates? SrdCoordinates;
        public bool Returning;
        public bool Failed;
    }
}
