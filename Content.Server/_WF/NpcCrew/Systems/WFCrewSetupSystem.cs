using System.Linq;
using System.Numerics;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Administration.Managers;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Administration;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Radio;
using Content.Shared.Roles;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Validates crew plans and applies missions for both admin tools and encounter callers.</summary>
public sealed partial class WFCrewSetupSystem : EntitySystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private WFPilotDutySystem _pilots = default!;
    [Dependency] private WFRadioOperatorSystem _radio = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private AdminVesselSpawnSystem _vessels = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFCrewSetupRequest>(OnRequest);
        SubscribeLocalEvent<WFCrewComponent, GetVerbsEvent<Verb>>(OnVerbs);
    }

    private void OnVerbs(EntityUid uid, WFCrewComponent crew, GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor)
            || !_admins.HasAdminFlag(actor.PlayerSession, AdminFlags.Spawn) || Transform(uid).GridUid is not { } grid)
            return;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wf-crew-setup-title"), Category = VerbCategory.Admin,
            Act = () =>
            {
                if (_admins.HasAdminFlag(actor.PlayerSession, AdminFlags.Spawn) && !TerminatingOrDeleted(grid))
                    RaiseNetworkEvent(new WFCrewSetupOpenEvent(GetNetEntity(grid), crew.Group), Filter.SinglePlayer(actor.PlayerSession));
            },
        });
    }

    /// <summary>Builds the same roster used by commands, optionally adding a captain to a spare deck post.</summary>
    public List<WFCrewSetupPost> Plan(EntityUid grid, int deckhands, bool captain)
    {
        var posts = _planner.Plan(grid, Math.Clamp(deckhands, 0, 32) + (captain ? 1 : 0));
        if (captain && posts.All(post => post.Role != WFCrewRoles.Captain))
        {
            var index = posts.FindLastIndex(post => post.Kind == WFCrewPostKind.Deck);
            if (index >= 0)
                posts[index] = posts[index] with { Role = WFCrewRoles.Captain };
        }
        return posts.Select(post => new WFCrewSetupPost { Role = post.Role.Id, Position = post.Coordinates.Position }).ToList();
    }

    /// <summary>Checks an entire plan before creating anything, including posts, prototypes and mission targets.</summary>
    public bool Validate(EntityUid grid, List<WFCrewSetupPost> posts, WFCrewMission mission)
    {
        if (!ValidateMission(grid, mission) || !TryComp<MapGridComponent>(grid, out var map) || posts.Count is < 1 or > 64)
            return false;
        foreach (var post in posts)
        {
            if (!Finite(post.Position) || !_prototypes.HasIndex<WFCrewRolePrototype>(post.Role)
                || post.Engagement is { } engagement && !Enum.IsDefined(engagement)
                || post.Loadout.Length > 0 && !_prototypes.HasIndex<StartingGearPrototype>(post.Loadout))
                return false;
            var coordinates = new EntityCoordinates(grid, post.Position);
            var tile = _maps.TileIndicesFor(grid, map, coordinates);
            if (!_planner.IsSafePost(grid, tile, map))
                return false;
        }
        return true;
    }

    private bool ValidateMission(EntityUid grid, WFCrewMission mission)
    {
        if (!HasComp<MapGridComponent>(grid) || Transform(grid).MapUid == null || Transform(grid).MapID == MapId.Nullspace
            || mission.Group.Length > 64 || mission.Callsign.Length > 100 || !Enum.IsDefined(mission.Order)
            || !float.IsFinite(mission.Range) || mission.Range is < 1 or > 5000
            || !Finite(mission.Destination) || !_prototypes.HasIndex<NpcFactionPrototype>(mission.Faction)
            || !_prototypes.HasIndex<RadioChannelPrototype>(mission.LocalChannel)
            || !_prototypes.HasIndex<RadioChannelPrototype>(mission.AlertChannel)
            || mission.Company.Length > 0 && !_prototypes.HasIndex<CompanyPrototype>(mission.Company))
            return false;
        if (mission.Order is WFPilotOrder.Follow or WFPilotOrder.Dock
            && (mission.Target is not { } target || !TryGetEntity(target, out var other) || other == grid
                || !HasComp<MapGridComponent>(other) || Transform(other.Value).MapID != Transform(grid).MapID))
            return false;
        return true;
    }

    private static bool Finite(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y);

    /// <summary>Encounter entry point: spawns the validated roster through the ordinary crew factory.</summary>
    public bool TrySpawn(EntityUid grid, List<WFCrewSetupPost> posts, WFCrewMission mission, out List<EntityUid> spawned)
    {
        spawned = new List<EntityUid>();
        if (!Validate(grid, posts, mission))
            return false;
        ApplyCompany(grid, mission.Company);
        _factions.ClearFactions(grid);
        _factions.AddFaction(grid, mission.Faction);
        foreach (var post in posts)
        {
            var uid = _crew.SpawnCrewman(post.Role, new EntityCoordinates(grid, post.Position), mission.Group,
                post.Loadout.Length > 0 ? new ProtoId<StartingGearPrototype>(post.Loadout) : (ProtoId<StartingGearPrototype>?) null);
            if (uid is not { } mob)
                continue;
            if (post.Engagement is { } engagement)
                _crew.SetEngagement(mob, engagement);
            ApplyMission(mob, grid, mission);
            spawned.Add(mob);
        }
        return spawned.Count == posts.Count;
    }

    private void ApplyCompany(EntityUid uid, string company)
    {
        if (company.Length == 0)
            return;
        var component = EnsureComp<CompanyComponent>(uid);
        component.CompanyName = company;
        Dirty(uid, component);
    }

    /// <summary>Updates an existing crew without requiring a new spawn roster.</summary>
    public bool TryApplyMission(EntityUid grid, WFCrewMission mission)
    {
        if (!ValidateMission(grid, mission))
            return false;
        ApplyCompany(grid, mission.Company);
        _factions.ClearFactions(grid);
        _factions.AddFaction(grid, mission.Faction);
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var transform))
        {
            if (transform.GridUid == grid && crew.Group == mission.Group)
                ApplyMission(uid, grid, mission);
        }
        return true;
    }

    /// <summary>Reissues a validated mission without changing membership or issuing new credentials.</summary>
    public void ApplyMission(EntityUid mob, EntityUid grid, WFCrewMission mission)
    {
        ApplyCompany(mob, mission.Company);
        _factions.ClearFactions(mob);
        _factions.AddFaction(mob, mission.Faction);
        if (TryComp<WFCaptainComponent>(mob, out var captain))
            captain.HeaveTo = mission.HeaveTo;
        if (TryComp<WFRadioOperatorComponent>(mob, out var radio))
        {
            _radio.SetCallsign((mob, radio), mission.Callsign);
            radio.LocalChannel = mission.LocalChannel;
            radio.AlertChannel = mission.AlertChannel;
        }
        if (!TryComp<WFCrewComponent>(mob, out var crew) || crew.Duty != WFCrewDuties.Pilot)
            return;
        var destination = new EntityCoordinates(Transform(grid).MapUid!.Value, mission.Destination);
        switch (mission.Order)
        {
            case WFPilotOrder.Hold: _pilots.Hold(mob); break;
            case WFPilotOrder.GoTo: _pilots.GoTo(mob, new List<EntityCoordinates> { destination }); break;
            case WFPilotOrder.Loiter: _pilots.Loiter(mob, destination, mission.Range); break;
            case WFPilotOrder.Follow: _pilots.Follow(mob, GetEntity(mission.Target!.Value), mission.Range); break;
            case WFPilotOrder.Dock: _pilots.Dock(mob, GetEntity(mission.Target!.Value)); break;
            case WFPilotOrder.Undock: _pilots.Undock(mob); break;
        }
    }

    private void OnRequest(WFCrewSetupRequest request, EntitySessionEventArgs args)
    {
        if (!_admins.HasAdminFlag(args.SenderSession, AdminFlags.Spawn))
            return;
        var response = new WFCrewSetupResponse { Action = request.Action, Grid = request.Grid };
        var session = args.SenderSession;
        if (request.Action == WFCrewSetupAction.List)
        {
            var query = EntityQueryEnumerator<MapGridComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out _, out var meta))
                response.Grids.Add(new WFCrewSetupGrid(GetNetEntity(uid), meta.EntityName));
            var current = session.AttachedEntity is { } player ? Transform(player).GridUid : null;
            response.Grids = response.Grids.OrderBy(info => GetEntity(info.Id) == current ? 0 : 1).ThenBy(info => info.Name).ToList();
        }
        else if (request.Action == WFCrewSetupAction.SpawnVessel)
        {
            if (session.AttachedEntity is { } actor && _prototypes.TryIndex<VesselPrototype>(request.Vessel, out var vessel)
                && !vessel.Abstract && Transform(actor).MapID != MapId.Nullspace
                && _vessels.TrySpawnVessel(vessel, Transform(actor).MapID, _transform.GetWorldPosition(actor), actor, out var created))
                response.Grid = GetNetEntity(created.Value);
            else
                response.Message = Loc.GetString("wf-crew-setup-invalid");
        }
        else if (request.Grid is { } net && TryGetEntity(net, out var found) && found is { } grid && HasComp<MapGridComponent>(grid))
        {
            if (request.Action == WFCrewSetupAction.Plan)
            {
                response.Posts = Plan(grid, request.Deckhands, request.Captain);
                if (response.Posts.Count == 0)
                    response.Message = Loc.GetString("wf-crew-setup-no-safe-posts");
            }
            else if (request.Action == WFCrewSetupAction.Clear)
            {
                var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var crew, out var transform))
                {
                    if (transform.GridUid == grid && crew.Group == request.Mission.Group)
                        QueueDel(uid);
                }
            }
            else if (request.Action == WFCrewSetupAction.Orders)
            {
                if (!TryApplyMission(grid, request.Mission))
                    response.Message = Loc.GetString("wf-crew-setup-invalid");
            }
            else if (!Validate(grid, request.Posts, request.Mission))
                response.Message = Loc.GetString("wf-crew-setup-invalid");
            else if (request.Action == WFCrewSetupAction.Spawn)
            {
                TrySpawn(grid, request.Posts, request.Mission, out var spawned);
                response.Message = Loc.GetString("wf-crew-setup-spawned", ("count", spawned.Count));
            }
            else if (request.Action == WFCrewSetupAction.Preview)
                response.Posts = request.Posts;
            else if (request.Action == WFCrewSetupAction.Teleport && _admins.HasAdminFlag(session, AdminFlags.Admin)
                && session.AttachedEntity is { } actor)
                _transform.SetCoordinates(actor, new EntityCoordinates(grid, request.Posts[0].Position));
        }
        else
            response.Message = Loc.GetString("wf-crew-setup-invalid");
        RaiseNetworkEvent(response, Filter.SinglePlayer(session));
    }
}
