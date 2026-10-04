using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Repairable;
using Content.Shared.Shuttles.Components;
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
    [Dependency] private EntityLookupSystem _lookup = default!;
    private readonly Dictionary<EntityUid, Job> _jobs = new();
    private readonly Dictionary<(EntityUid Grid, string Group), Skipped> _skipped = new();
    private readonly HashSet<Entity<DockingComponent>> _docks = new();
    private float _timer;

    /// <summary>How long a worker may be away from a home grid it is not docked to before the job is dropped.</summary>
    private static readonly TimeSpan StrandedAfter = TimeSpan.FromSeconds(10);

    /// <summary>Incapacitated or dead workers cannot continue their assigned interaction.</summary>
    public void CancelWorker(EntityUid worker)
    {
        if (_jobs.TryGetValue(worker, out var job))
            EndJob(worker, job);
    }

    /// <summary>Whether the crewman currently holds a work job.</summary>
    public bool HasJob(EntityUid worker) => _jobs.ContainsKey(worker);

    /// <summary>Workers collecting cargo remain listed with their ship while visiting the supply grid.</summary>
    public EntityUid? HomeGrid(EntityUid worker)
    {
        if (!_jobs.TryGetValue(worker, out var job))
            return null;
        if (TerminatingOrDeleted(job.Grid) || TerminatingOrDeleted(worker))
        {
            EndJob(worker, job);
            return null;
        }
        return job.Grid;
    }

    /// <summary>Stops assigning work when the corresponding order is cancelled or paused.</summary>
    public void Cancel(EntityUid grid, string group)
    {
        foreach (var (worker, job) in _jobs.ToArray())
        {
            if (job.Grid == grid && job.Group == group)
                EndJob(worker, job);
        }
        _skipped.Remove((grid, group));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 1f)
            return;
        _timer = 0;
        foreach (var (worker, job) in _jobs.ToArray())
        {
            if (TerminatingOrDeleted(worker) || TerminatingOrDeleted(job.Grid) || !_mobs.IsAlive(worker)
                || HasComp<ActorComponent>(worker))
                EndJob(worker, job);
            else if (job.Failed)
                Release(worker, job);
            else
                CheckStranded(worker, job);
        }
        foreach (var (key, skipped) in _skipped.ToArray())
        {
            if (TerminatingOrDeleted(key.Grid))
                _skipped.Remove(key);
            else
                skipped.Entities.RemoveWhere(uid => TerminatingOrDeleted(uid));
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
                EndJob(worker, job);
                continue;
            }
            if (!job.Failed)
                return "working";
            Release(worker, job);
        }

        EntityUid? chosen = null;
        var crewQuery = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crewQuery.MoveNext(out var uid, out var crew, out var xform))
        {
            if (xform.GridUid == grid && (crew.Post?.EntityId ?? xform.GridUid) == grid && crew.Group == group
                && crew.Role == WFCrewRoles.Deckhand && crew.Duty == WFCrewDuties.Guard
                && _mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid) && !_weapons.HasLiveThreat(uid))
            {
                chosen = uid;
                break;
            }
        }
        if (chosen is not { } mob)
            return "no-worker";
        // Looters go through a docked port in whatever they are wearing.
        if (kind != WFCrewObjectiveKind.Loot && !_eva.Prepare(mob))
        {
            _eva.SeekSpare(mob);
            return "no-eva";
        }

        var home = Comp<WFCrewComponent>(mob).Post ?? Transform(mob).Coordinates;
        var skipped = SkippedFor(grid, group, kind);
        var blocked = false;
        if (kind == WFCrewObjectiveKind.Repair)
        {
            foreach (var missing in MissingStructure(grid, mob))
            {
                if (missing is not { } at)
                    continue;
                if (skipped.Coordinates.Contains(at))
                {
                    blocked = true;
                    continue;
                }
                _jobs[mob] = new Job(grid, group, kind, grid, home, null, _timing.CurTime) { SrdCoordinates = at };
                return "working";
            }
            var noTool = false;
            var targets = EntityQueryEnumerator<RepairableComponent, DamageableComponent, TransformComponent>();
            while (targets.MoveNext(out var target, out var repair, out var damage, out var xform))
            {
                if (xform.GridUid != grid || damage.TotalDamage <= 0 || HasComp<MobStateComponent>(target))
                    continue;
                if (skipped.Entities.Contains(target))
                {
                    blocked = true;
                    continue;
                }
                var tool = FindTool(mob, repair);
                if (tool == null)
                {
                    noTool = true;
                    continue;
                }
                _jobs[mob] = new Job(grid, group, kind, target, home, tool, _timing.CurTime);
                return "working";
            }
            return noTool ? "no-tool" : blocked ? "work-blocked" : Complete(grid, group);
        }

        if (TerminatingOrDeleted(source))
            return Complete(grid, group);
        // A raid takes a few things and goes.
        if (kind == WFCrewObjectiveKind.Loot && _looted.GetValueOrDefault((grid, group)) >= LootLimit)
            return Complete(grid, group);
        var items = Transform(source).ChildEnumerator;
        while (items.MoveNext(out var item))
        {
            if (Transform(item).Anchored)
                continue;
            var wanted = kind switch
            {
                WFCrewObjectiveKind.Salvage => HasComp<StackComponent>(item),
                WFCrewObjectiveKind.Loot => HasComp<Content.Shared.Item.ItemComponent>(item),
                _ => HasComp<BallisticAmmoProviderComponent>(item) && !HasComp<GunComponent>(item) && _weapons.AmmoCount(item) > 0
                     || TryComp<Content.Shared.Atmos.Components.GasTankComponent>(item, out var tank) && _eva.IsUsableSpare(item, tank),
            };
            if (!wanted)
                continue;
            if (skipped.Entities.Contains(item))
            {
                blocked = true;
                continue;
            }
            _jobs[mob] = new Job(grid, group, kind, item, home, null, _timing.CurTime);
            if (kind == WFCrewObjectiveKind.Loot)
                _looted[(grid, group)] = _looted.GetValueOrDefault((grid, group)) + 1;
            return "working";
        }
        return blocked ? "work-blocked" : Complete(grid, group);
    }

    /// <summary>How many things a looting crew carries off before it has enough.</summary>
    private const int LootLimit = 4;
    private readonly Dictionary<(EntityUid Grid, string Group), int> _looted = new();

    private string Complete(EntityUid grid, string group)
    {
        _looted.Remove((grid, group));
        _skipped.Remove((grid, group));
        return "complete";
    }

    private Skipped SkippedFor(EntityUid grid, string group, WFCrewObjectiveKind kind)
    {
        if (!_skipped.TryGetValue((grid, group), out var skipped) || skipped.Kind != kind)
            _skipped[(grid, group)] = skipped = new Skipped(kind);
        return skipped;
    }

    /// <summary>Ends a job, blacklisting its target for the order when the job failed through no fault of the worker's kit.</summary>
    private void Release(EntityUid worker, Job job)
    {
        if (job.Blame)
        {
            var skipped = SkippedFor(job.Grid, job.Group, job.Kind);
            if (job.SrdCoordinates is { } at)
                skipped.Coordinates.Add(at);
            else
                skipped.Entities.Add(job.Target);
        }
        EndJob(worker, job);
    }

    /// <summary>The single exit for a job: drops carried cargo and switches the welder off.</summary>
    private void EndJob(EntityUid worker, Job job)
    {
        _jobs.Remove(worker);
        if (TerminatingOrDeleted(worker))
            return;
        if (job.Returning && !TerminatingOrDeleted(job.Target))
            _hands.TryDrop(worker, job.Target);
        if (job.Tool is { } tool && !TerminatingOrDeleted(tool) && TryComp<WelderComponent>(tool, out var welder) && welder.Enabled)
            _tools.TurnOff((tool, welder), worker);
    }

    /// <summary>Drops a job whose worker has been off the home grid, with that grid undocked from where they stand, for too long.</summary>
    private void CheckStranded(EntityUid worker, Job job)
    {
        var here = Transform(worker).GridUid;
        if (here == job.Grid || here is { } grid && DockedTo(job.Grid, grid))
        {
            job.StrandedSince = null;
            return;
        }
        job.StrandedSince ??= _timing.CurTime;
        if (_timing.CurTime - job.StrandedSince.Value >= StrandedAfter)
            EndJob(worker, job);
    }

    private bool DockedTo(EntityUid grid, EntityUid other)
    {
        _docks.Clear();
        _lookup.GetChildEntities(grid, _docks);
        foreach (var dock in _docks)
        {
            if (dock.Comp.DockedWith is { } with && Transform(with).GridUid == other)
                return true;
        }
        return false;
    }

    /// <summary>Only a tool the crewman carries or has built in counts; loose tools on deck belong to whoever left them.</summary>
    private EntityUid? FindTool(EntityUid mob, RepairableComponent repair)
    {
        if (repair.Qualities.Any(quality => _tools.HasQuality(mob, quality)))
            return mob;
        foreach (var held in _hands.EnumerateHeld(mob))
        {
            if (repair.Qualities.Any(quality => _tools.HasQuality(held, quality)))
                return held;
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
            Fail(job, false);
            return false;
        }
        if (_timing.CurTime - job.Started > TimeSpan.FromSeconds(120))
        {
            Fail(job, true);
            return false;
        }
        if (job.Returning)
        {
            coordinates = job.Home;
            return true;
        }
        if (job.SrdCoordinates is { } repairCoordinates)
        {
            if (!MissingStructure(job.Grid, mob, repairCoordinates).Any())
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
                Fail(job, true);
            return true;
        }
        if (job.Tool is { } tool)
        {
            if (tool != mob && !_hands.IsHolding(mob, tool, out _))
            {
                if (!_hands.TryPickupAnyHand(mob, tool))
                    Fail(job, true);
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
            Fail(job, true);
            return true;
        }
        job.Returning = true;
        return true;
    }

    /// <summary>Marks a job failed; a blamed failure also keeps the order from picking the same target again.</summary>
    private static void Fail(Job job, bool blame)
    {
        job.Failed = true;
        job.Blame |= blame;
    }

    /// <summary>Targets an order gave up on, so the next pick moves on instead of repeating them.</summary>
    private sealed class Skipped(WFCrewObjectiveKind kind)
    {
        public readonly WFCrewObjectiveKind Kind = kind;
        public readonly HashSet<EntityCoordinates> Coordinates = new();
        public readonly HashSet<EntityUid> Entities = new();
    }

    private sealed class Job(EntityUid grid, string group, WFCrewObjectiveKind kind, EntityUid target,
        EntityCoordinates home, EntityUid? tool, TimeSpan started)
    {
        public readonly EntityUid Grid = grid;
        public readonly string Group = group;
        public readonly WFCrewObjectiveKind Kind = kind;
        public readonly EntityUid Target = target;
        public readonly EntityUid? Tool = tool;
        public readonly EntityCoordinates Home = home;
        public readonly TimeSpan Started = started;
        public TimeSpan NextUse;
        public TimeSpan? StrandedSince;
        public EntityCoordinates? SrdCoordinates;
        public bool Returning;
        public bool Failed;
        public bool Blame;
    }
}
