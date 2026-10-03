using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._Mono.Company;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Checks boarding and docking authorization independently of nearby players or radio operators.</summary>
public sealed class WFCrewSecuritySystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    private float _timer;
    private readonly HashSet<(EntityUid Crew, EntityUid Visitor)> _boarders = new();
    private readonly HashSet<(EntityUid Crew, EntityUid Visitor)> _ownedHostiles = new();

    /// <summary>An explicit boarding rule also provokes otherwise defensive officers.</summary>
    public bool HasThreat(EntityUid crew) => _ownedHostiles.Any(pair => pair.Crew == crew);

    /// <summary>Whether the current security rule explicitly targets this visitor.</summary>
    public bool IsHostileVisitor(EntityUid crew, EntityUid visitor) => _ownedHostiles.Contains((crew, visitor));

    /// <summary>Acts on a personally observed or received contact, preserving the crew's boarding policy.</summary>
    public void ReceiveSighting(EntityUid uid, EntityUid visitor)
    {
        if (!TryComp<WFCrewComponent>(uid, out var crew) || !TryComp<WFCrewSecurityComponent>(uid, out var rules)
            || !_mobs.IsAlive(uid) || IsAuthorized(uid, visitor) || Transform(uid).GridUid is not { } grid
            || crew.Post is { } post && post.EntityId != grid || Transform(visitor).GridUid != grid)
            return;
        if (rules.Boarding == WFCrewSecurityResponse.Hostile)
        {
            if (!TryComp<FactionExceptionComponent>(uid, out var exceptions) || !exceptions.Hostiles.Any(target => target == visitor))
                _ownedHostiles.Add((uid, visitor));
            _factions.AggroEntity(uid, visitor);
        }
        if (_boarders.Add((uid, visitor)) && HasComp<WFRadioOperatorComponent>(uid))
            Respond(grid, crew.Group, visitor, rules.Boarding, docking: false);
    }

    /// <summary>Re-evaluates visitors after a policy edit without retaining hostility introduced by the old rule.</summary>
    public void Reset(EntityUid crew)
    {
        foreach (var pair in _ownedHostiles.Where(pair => pair.Crew == crew).ToArray())
        {
            _factions.DeAggroEntity(crew, pair.Visitor);
            _ownedHostiles.Remove(pair);
        }
        _boarders.RemoveWhere(pair => pair.Crew == crew);
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DockEvent>(OnDock);
    }

    /// <summary>Orders to dock authorize that destination, not other ships arriving at this crew.</summary>
    public bool IsOutboundDock(EntityUid grid, string group, EntityUid destination)
    {
        var query = EntityQueryEnumerator<WFCrewComponent, WFPilotDutyComponent, TransformComponent>();
        while (query.MoveNext(out _, out var crew, out var pilot, out var xform))
        {
            if (xform.GridUid == grid && crew.Group == group && pilot.Orders == WFPilotOrder.Dock
                && pilot.DockTarget == destination)
                return true;
        }
        return false;
    }

    /// <summary>Authorization requires the same company or an overlapping NPC faction.</summary>
    public bool IsAuthorized(EntityUid crew, EntityUid other)
    {
        if (TryComp<CompanyComponent>(crew, out var company) && company.CompanyName.Id is not ("" or "None")
            && TryComp<CompanyComponent>(other, out var visitorCompany))
            return company.CompanyName == visitorCompany.CompanyName;
        return TryComp<NpcFactionMemberComponent>(crew, out var faction)
            && TryComp<NpcFactionMemberComponent>(other, out var visitorFaction)
            && faction.Factions.Intersect(visitorFaction.Factions).Any();
    }

    private void OnDock(DockEvent args)
    {
        Arrived(args.GridAUid, args.GridBUid);
        Arrived(args.GridBUid, args.GridAUid);
    }

    private void Arrived(EntityUid grid, EntityUid visitor)
    {
        var reported = new HashSet<string>();
        var query = EntityQueryEnumerator<WFCrewComponent, WFCrewSecurityComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var rules, out var xform))
        {
            if (xform.GridUid != grid || HasComp<ActorComponent>(uid) || !_mobs.IsAlive(uid)
                || IsAuthorized(uid, visitor) || IsOutboundDock(grid, crew.Group, visitor))
                continue;
            if (reported.Add(crew.Group))
                Respond(grid, crew.Group, visitor, rules.Docking, docking: true);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 1f)
            return;
        _timer = 0;
        var present = new HashSet<(EntityUid Crew, EntityUid Visitor)>();
        var people = new List<(EntityUid Uid, TransformComponent Transform)>();
        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var person, out _, out var transform))
        {
            if (_mobs.IsAlive(person))
                people.Add((person, transform));
        }
        var query = EntityQueryEnumerator<WFCrewComponent, WFCrewSecurityComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var rules, out var xform))
        {
            if (xform.GridUid is not { } grid || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid)
                || crew.Post is { } post && post.EntityId != grid)
                continue;
            foreach (var (visitor, visitorXform) in people)
            {
                if (visitorXform.GridUid != grid || visitor == uid || IsAuthorized(uid, visitor)
                    || TryComp<WFCrewComponent>(visitor, out var other) && other.Group == crew.Group)
                    continue;
                present.Add((uid, visitor));
                if (!EntityManager.System<WFCrewWeaponSystem>().CanSee(uid, visitor))
                    continue;
                ReceiveSighting(uid, visitor);
                if (rules.Boarding != WFCrewSecurityResponse.Ignore)
                    EntityManager.System<WFCrewCommsSystem>().Report(uid, visitor);
            }
        }
        foreach (var pair in _ownedHostiles.Where(pair => !present.Contains(pair)).ToArray())
        {
            if (!TerminatingOrDeleted(pair.Crew))
                _factions.DeAggroEntity(pair.Crew, pair.Visitor);
            _ownedHostiles.Remove(pair);
        }
        _boarders.RemoveWhere(pair => !present.Contains(pair));
    }

    private void Respond(EntityUid grid, string group, EntityUid visitor, WFCrewSecurityResponse response, bool docking)
    {
        if (response == WFCrewSecurityResponse.Ignore)
            return;
        if (docking && response == WFCrewSecurityResponse.Hostile)
            _alerts.ReportShipThreat(grid, group, visitor);
        var ev = new WFCrewSecurityIncidentEvent(grid, group, visitor, response, docking);
        RaiseLocalEvent(grid, ref ev, true);
    }
}

/// <summary>One unauthorized arrival reported to the crew's radio officer.</summary>
[ByRefEvent]
public readonly record struct WFCrewSecurityIncidentEvent(EntityUid Grid, string Group, EntityUid Visitor,
    WFCrewSecurityResponse Response, bool Docking);
