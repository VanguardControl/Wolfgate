using Content.Server._WF.NpcCrew.Components;
using Content.Server.Humanoid.Systems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Spawns crew, keeps the HTN blackboard in step with <see cref="WFCrewComponent"/>, puts role titles on names and
/// reports crew going down.
/// </summary>
public sealed class WFCrewSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    /// <summary>Blackboard key holding the duty name; selects the duty branch of the crew HTN root.</summary>
    public const string DutyKey = "WFCrewDuty";

    /// <summary>Blackboard key holding the post coordinates.</summary>
    public const string PostKey = "WFCrewPost";

    /// <summary>Blackboard key holding how close to the post counts as being there.</summary>
    public const string PostRangeKey = "WFCrewPostRange";

    public override void Initialize()
    {
        base.Initialize();

        // After the random name so the title goes in front of the final name.
        SubscribeLocalEvent<WFCrewComponent, MapInitEvent>(OnCrewMapInit, after: [typeof(RandomHumanoidAppearanceSystem)]);
        SubscribeLocalEvent<WFCrewComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WFCrewSpawnPointComponent, MapInitEvent>(OnSpawnPointMapInit);
    }

    private void OnCrewMapInit(EntityUid uid, WFCrewComponent component, MapInitEvent args)
    {
        Apply((uid, component));
    }

    private void OnMobStateChanged(EntityUid uid, WFCrewComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

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
    public EntityUid? SpawnCrewman(ProtoId<WFCrewRolePrototype> roleId, EntityCoordinates post, string group)
    {
        if (!_prototypes.TryIndex(roleId, out var role))
        {
            Log.Error($"Unknown crew role {roleId}");
            return null;
        }

        var uid = Spawn(role.Mob, post);
        var crew = EnsureComp<WFCrewComponent>(uid);
        crew.Role = roleId;
        crew.Duty = role.Duty;
        crew.Engagement = role.Engagement;
        crew.Group = group;
        crew.Post = post;
        Apply((uid, crew));
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

        if (TryComp<HTNComponent>(uid, out var htn))
        {
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
