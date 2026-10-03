using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Maintains escort alliances independently of temporary evasive flight orders.</summary>
public sealed partial class WFCrewEscortSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WFCaptainSystem _captains = default!;
    private readonly Dictionary<(EntityUid Grid, string Group), EntityUid> _escorts = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPilotOrdersChangedEvent>(OnOrdersChanged);
    }

    /// <summary>Assigns an escort without requiring its captain or pilot to survive the engagement.</summary>
    public void SetEscort(EntityUid grid, string group, EntityUid target)
    {
        if (grid != target && HasComp<MapGridComponent>(grid) && HasComp<MapGridComponent>(target)
            && Transform(grid).MapID == Transform(target).MapID)
            _escorts[(grid, group)] = target;
    }

    /// <summary>Records a pilot's newly assigned formation.</summary>
    public void SetEscort(EntityUid pilot, EntityUid target)
    {
        if (TryComp<WFCrewComponent>(pilot, out var crew) && Transform(pilot).GridUid is { } grid)
            SetEscort(grid, crew.Group, target);
    }

    /// <summary>Ends an assignment when its mission is replaced, paused, completed or cancelled.</summary>
    public void Clear(EntityUid grid, string group) => _escorts.Remove((grid, group));

    private void OnOrdersChanged(ref WFPilotOrdersChangedEvent args)
    {
        if (args.Continuation || _captains.IsChangingOrders
            || !TryComp<WFCrewComponent>(args.Mob, out var crew) || Transform(args.Mob).GridUid is not { } grid)
            return;
        Clear(grid, crew.Group);
    }

    /// <summary>Snapshots connected escorts and their protected vessels before alert handlers change orders.</summary>
    public HashSet<EntityUid> GetFormation(EntityUid grid)
    {
        var members = new HashSet<EntityUid> { grid };
        if (TerminatingOrDeleted(grid) || !HasComp<MapGridComponent>(grid))
            return members;
        var crews = new HashSet<(EntityUid Grid, string Group)>();
        var battlegroups = new Dictionary<string, List<EntityUid>>();
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var transform))
        {
            if (_mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid)
                && (crew.Post?.EntityId ?? transform.GridUid) is { } home && HasComp<MapGridComponent>(home))
            {
                crews.Add((home, crew.Group));
                if (crew.Battlegroup.Length == 0 || Transform(home).MapID != Transform(grid).MapID)
                    continue;
                if (!battlegroups.TryGetValue(crew.Battlegroup, out var ships))
                    battlegroups[crew.Battlegroup] = ships = new List<EntityUid>();
                if (!ships.Contains(home))
                    ships.Add(home);
            }
        }
        var neighbors = new Dictionary<EntityUid, HashSet<EntityUid>>();
        // Every ship of a battlegroup is linked through its first ship.
        foreach (var ships in battlegroups.Values)
        {
            for (var i = 1; i < ships.Count; i++)
            {
                if (!neighbors.TryGetValue(ships[0], out var first))
                    neighbors[ships[0]] = first = new HashSet<EntityUid>();
                first.Add(ships[i]);
                if (!neighbors.TryGetValue(ships[i], out var other))
                    neighbors[ships[i]] = other = new HashSet<EntityUid>();
                other.Add(ships[0]);
            }
        }
        foreach (var (key, target) in _escorts.ToArray())
        {
            if (!crews.Contains(key) || TerminatingOrDeleted(key.Grid) || TerminatingOrDeleted(target)
                || !HasComp<MapGridComponent>(key.Grid) || !HasComp<MapGridComponent>(target))
            {
                _escorts.Remove(key);
                continue;
            }
            // FTL temporarily separates maps; keep the assignment for their reunion.
            if (Transform(key.Grid).MapID != Transform(grid).MapID || Transform(target).MapID != Transform(grid).MapID)
                continue;
            if (!neighbors.TryGetValue(key.Grid, out var outgoing))
                neighbors[key.Grid] = outgoing = new HashSet<EntityUid>();
            outgoing.Add(target);
            if (!neighbors.TryGetValue(target, out var incoming))
                neighbors[target] = incoming = new HashSet<EntityUid>();
            incoming.Add(key.Grid);
        }
        var pending = new Queue<EntityUid>();
        pending.Enqueue(grid);
        while (pending.TryDequeue(out var current))
        {
            if (!neighbors.TryGetValue(current, out var adjacent))
                continue;
            foreach (var member in adjacent)
            {
                if (members.Add(member))
                    pending.Enqueue(member);
            }
        }
        return members;
    }

    /// <summary>Formation members remain allies even when their company or local crew labels differ.</summary>
    public bool AreInFormation(EntityUid first, EntityUid second) => GetFormation(first).Contains(second);
}
