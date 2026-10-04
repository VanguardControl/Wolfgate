using Content.Server._WF.NpcCrew.Components;
using Content.Server.Humanoid.Systems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Components;
using Content.Shared.Damage;
using Content.Shared.Access.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Roles;
using Content.Server.NPC.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Spawns crew, keeps the HTN blackboard in step with <see cref="WFCrewComponent"/>, puts role titles on names and
/// reports crew going down.
/// </summary>
public sealed class WFCrewSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private WFCrewAccessSystem _crewAccess = default!;
    [Dependency] private NPCRetaliationSystem _retaliation = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly List<EntityUid> _expired = new();

    /// <summary>Blackboard key holding the duty name; selects the duty branch of the crew HTN root.</summary>
    public const string DutyKey = "WFCrewDuty";

    /// <summary>Blackboard key holding the post coordinates.</summary>
    public const string PostKey = "WFCrewPost";

    /// <summary>Blackboard key holding how close to the post counts as being there.</summary>
    public const string PostRangeKey = "WFCrewPostRange";

    public override void Initialize()
    {
        base.Initialize();

        // After the random name so the title goes in front of the final name. Every map-init subscription a system
        // makes must share one ordering, so the spawn point carries the same constraint.
        SubscribeLocalEvent<WFCrewComponent, MapInitEvent>(OnCrewMapInit, after: [typeof(RandomHumanoidAppearanceSystem)]);
        SubscribeLocalEvent<WFCrewSpawnPointComponent, MapInitEvent>(OnSpawnPointMapInit, after: [typeof(RandomHumanoidAppearanceSystem)]);
        SubscribeLocalEvent<WFCrewComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<NPCRetaliationComponent, BeforeDamageChangedEvent>(OnBeforeCrewDamage,
            before: [typeof(Content.Shared._Onyx.Wounds.WoundDamageRoutingSystem)]);
        // Upstream de-aggroes expired memories every tick without removing them; prune right after it runs.
        UpdatesAfter.Add(typeof(NPCRetaliationSystem));
    }

    /// <summary>The grid a crewman serves: its post's grid, or the grid it stands on without a post.</summary>
    public EntityUid? HomeGrid(EntityUid uid, WFCrewComponent? crew = null)
    {
        if (!Resolve(uid, ref crew, false))
            return null;
        return crew.Post?.EntityId ?? Transform(uid).GridUid;
    }

    /// <summary>One crew is the same group label serving the same home grid.</summary>
    public bool SameCrew(EntityUid first, EntityUid second)
    {
        return TryComp<WFCrewComponent>(first, out var a) && TryComp<WFCrewComponent>(second, out var b)
            && a.Group == b.Group && HomeGrid(first, a) is { } home && HomeGrid(second, b) == home;
    }

    /// <summary>
    /// The mob behind a damage origin. Hitscan names the gun as the origin, so a carried gun resolves to whoever
    /// holds it; mounted guns and everything else come back unchanged.
    /// </summary>
    public EntityUid Wielder(EntityUid origin)
    {
        if (TerminatingOrDeleted(origin) || HasComp<MobStateComponent>(origin)
            || !HasComp<Content.Shared.Weapons.Ranged.Components.GunComponent>(origin))
            return origin;

        var holder = Transform(origin).ParentUid;
        return HasComp<MobStateComponent>(holder) ? holder : origin;
    }

    /// <summary>Records crew attacks before body-part routing loses the damage origin.</summary>
    private void OnBeforeCrewDamage(Entity<NPCRetaliationComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !HasComp<WFCrewComponent>(ent)
            || !args.Damage.AnyPositive()
            || args.Origin is not { } source
            || source == ent.Owner
            || TerminatingOrDeleted(source))
        {
            return;
        }

        var attacker = Wielder(source);
        if (attacker == ent.Owner)
            return;

        if (_retaliation.TryRetaliate(ent, attacker) || !HasComp<MobStateComponent>(attacker) || SameCrew(ent, attacker))
            return;

        // Upstream never remembers a friendly-faction attacker; crew still answer and stop protecting it.
        _factions.AggroEntity(ent.Owner, attacker);
        if (ent.Comp.AttackMemoryLength is { } length)
        {
            var memories = ent.Comp.AttackMemories;
            memories[attacker] = _timing.CurTime + length;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFCrewComponent, NPCRetaliationComponent>();
        while (query.MoveNext(out var uid, out _, out var retaliation))
        {
            var memories = retaliation.AttackMemories;
            if (memories.Count == 0)
                continue;
            _expired.Clear();
            foreach (var (attacker, until) in memories)
            {
                if (now >= until || TerminatingOrDeleted(attacker))
                    _expired.Add(attacker);
            }
            foreach (var attacker in _expired)
            {
                memories.Remove(attacker);
                // Upstream just dropped this hostility; restore it where a boarding rule or shared alert still owns it.
                if (!TerminatingOrDeleted(attacker)
                    && (EntityManager.System<WFCrewSecuritySystem>().IsHostileVisitor(uid, attacker)
                        || EntityManager.System<WFCrewAlertSystem>().SharesHostile(uid, attacker)))
                    _factions.AggroEntity(uid, attacker);
            }
        }
    }

    private void OnCrewMapInit(EntityUid uid, WFCrewComponent component, MapInitEvent args)
    {
        Apply((uid, component));
    }

    private void OnMobStateChanged(EntityUid uid, WFCrewComponent component, MobStateChangedEvent args)
    {
        EntityManager.System<WFCrewEscortSystem>().Invalidate();
        if (args.NewMobState == MobState.Alive)
            return;

        EntityManager.System<WFCrewWorkSystem>().CancelWorker(uid);
        if (args.NewMobState == MobState.Dead && HasComp<WFCrewRepairComponent>(uid))
        {
            RemComp<Content.Shared._Mono.ShipRepair.Components.ShipRepairToolComponent>(uid);
            RemComp<Content.Shared.Tools.Components.ToolComponent>(uid);
            RemComp<WFCrewRepairComponent>(uid);
        }

        var ev = new WFCrewMemberDownEvent(uid, component.Group, component.Role, args.NewMobState == MobState.Dead);
        RaiseLocalEvent(uid, ref ev, true);
    }

    private void OnSpawnPointMapInit(EntityUid uid, WFCrewSpawnPointComponent component, MapInitEvent args)
    {
        if (component.Spawned != null)
            return;

        component.Spawned = SpawnCrewman(component.Role, Transform(uid).Coordinates, component.Group);
    }

    /// <summary>Spawns one crewman of a role with its post at the coordinates. Null when the role is unknown.</summary>
    public EntityUid? SpawnCrewman(ProtoId<WFCrewRolePrototype> roleId, EntityCoordinates post, string group,
        ProtoId<StartingGearPrototype>? loadout = null, EntProtoId? body = null)
    {
        if (!_prototypes.TryIndex(roleId, out var role))
        {
            Log.Error($"Unknown crew role {roleId}");
            return null;
        }

        if (loadout is { } gear && !_prototypes.HasIndex(gear))
            return null;
        EntityUid uid;
        if (body != null && !_prototypes.HasIndex<EntityPrototype>(body.Value))
            return null;
        if (loadout != null || body != null)
        {
            uid = EntityManager.CreateEntityUninitialized(body ?? role.Mob, post);
            // A profile's body is bare: the role's own parts come from its kit.
            if (body != null && role.Kit is { } kit && _prototypes.TryIndex<EntityPrototype>(kit, out var parts))
                EntityManager.AddComponents(uid, parts.Components);
            if (loadout is { } selected)
                EnsureComp<LoadoutComponent>(uid).StartingGear = new List<ProtoId<StartingGearPrototype>> { selected };
            EntityManager.InitializeAndStartEntity(uid);
        }
        else
            uid = Spawn(role.Mob, post);
        EntityManager.AddComponents(uid, role.Components);
        var crew = EnsureComp<WFCrewComponent>(uid);
        crew.Role = roleId;
        crew.Duty = role.Duty;
        crew.Engagement = role.Engagement;
        crew.Group = group;
        crew.Post = post;
        Apply((uid, crew));
        _crewAccess.RegisterSpawnShip(uid);
        if (Transform(uid).GridUid is { } ship && !HasComp<Content.Shared._Mono.ShipRepair.Components.ShipRepairDataComponent>(ship))
            EntityManager.System<Content.Server._Mono.ShipRepair.ShipRepairSystem>().GenerateRepairData(ship);
        return uid;
    }

    /// <summary>Spawns every post of a plan as one crew. Returns the crew spawned.</summary>
    public List<EntityUid> SpawnCrew(IEnumerable<WFCrewPost> plan, string group)
    {
        var crew = new List<EntityUid>();
        foreach (var post in plan)
        {
            if (SpawnCrewman(post.Role, post.Coordinates, group) is { } uid)
                crew.Add(uid);
        }

        return crew;
    }

    /// <summary>Deletes every crewman of a group. Returns how many.</summary>
    public int ClearGroup(string group)
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFCrewComponent>();
        while (query.MoveNext(out var uid, out var crew))
        {
            if (crew.Group != group)
                continue;

            QueueDel(uid);
            count++;
        }

        return count;
    }

    public void SetDuty(Entity<WFCrewComponent?> ent, string duty)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.Duty = duty;
        Apply((ent, ent.Comp));
    }

    public void SetPost(Entity<WFCrewComponent?> ent, EntityCoordinates? post)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.Post = post;
        Apply((ent, ent.Comp));
    }

    public void SetEngagement(Entity<WFCrewComponent?> ent, WFCrewEngagement engagement)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.Engagement = engagement;
        Apply((ent, ent.Comp));
    }

    /// <summary>Writes the component to the blackboard, replans, and titles the name once.</summary>
    public void Apply(Entity<WFCrewComponent> ent)
    {
        var (uid, crew) = ent;
        EnsureComp<AccessComponent>(uid);
        EntityManager.System<WFCrewEscortSystem>().Invalidate();

        if (TryComp<HTNComponent>(uid, out var htn))
        {
            _npc.SetBlackboard(uid, "NavAccess", true, htn);
            _npc.SetBlackboard(uid, DutyKey, crew.Duty, htn);
            _npc.SetBlackboard(uid, PostRangeKey, crew.PostRange, htn);
            if (crew.Post is { } post)
                _npc.SetBlackboard(uid, PostKey, post, htn);
            else
                htn.Blackboard.Remove<EntityCoordinates>(PostKey);

            _htn.Replan(htn);
        }

        if (crew.Titled || crew.Role is not { } roleId || !_prototypes.TryIndex(roleId, out var role))
            return;

        _meta.SetEntityName(uid, Loc.GetString("wf-crew-name-format",
            ("title", Loc.GetString(role.Title)),
            ("name", Name(uid))));
        crew.Titled = true;
    }
}
