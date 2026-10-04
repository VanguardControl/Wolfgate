using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Crew who never fight run for the bridge when their ship is alerted or boarded, and go back to their posts a
/// minute after the last alarm.
/// </summary>
public sealed partial class WFCrewShelterSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private MobStateSystem _mobs = default!;

    private static readonly TimeSpan Calm = TimeSpan.FromSeconds(60);
    private readonly Dictionary<(EntityUid Grid, string Group), TimeSpan> _alarms = new();
    private readonly Dictionary<EntityUid, EntityCoordinates?> _posts = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewAlertEvent>(OnAlert);
        SubscribeLocalEvent<WFCrewSecurityIncidentEvent>(OnIncident);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _alarms.Clear();
            _posts.Clear();
        });
    }

    private void OnAlert(ref WFCrewAlertEvent args)
    {
        Shelter(args.Grid, args.Group);
    }

    private void OnIncident(ref WFCrewSecurityIncidentEvent args)
    {
        Shelter(args.Grid, args.Group);
    }

    private void Shelter(EntityUid grid, string group)
    {
        EntityCoordinates? bridge = null;
        var sheltered = false;
        var passive = new List<Entity<WFCrewComponent>>();
        var query = EntityQueryEnumerator<WFCrewComponent>();
        while (query.MoveNext(out var uid, out var crew))
        {
            if (crew.Group != group || _crew.HomeGrid(uid, crew) != grid || !_mobs.IsAlive(uid))
                continue;

            if (crew.Duty == WFCrewDuties.Pilot && crew.Post is { } helm)
                bridge ??= helm;
            else if (crew.Engagement == WFCrewEngagement.Never && !_posts.ContainsKey(uid))
                passive.Add((uid, crew));
            else if (crew.Engagement == WFCrewEngagement.Never)
                sheltered = true;
        }

        // An alarm only matters while someone is, or is about to be, sheltering from it.
        if (passive.Count == 0 && !sheltered)
            return;

        _alarms[(grid, group)] = _timing.CurTime + Calm;
        if (bridge == null)
            return;

        foreach (var member in passive)
        {
            _posts[member] = member.Comp.Post;
            _crew.SetPost((member, member.Comp), bridge);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(1);
        foreach (var (key, until) in _alarms)
        {
            if (_timing.CurTime >= until || TerminatingOrDeleted(key.Grid))
                _alarms.Remove(key);
        }

        if (_posts.Count == 0)
            return;

        foreach (var (uid, post) in new Dictionary<EntityUid, EntityCoordinates?>(_posts))
        {
            if (TerminatingOrDeleted(uid) || !TryComp<WFCrewComponent>(uid, out var crew))
            {
                _posts.Remove(uid);
                continue;
            }

            if (_crew.HomeGrid(uid, crew) is not { } grid)
                continue;

            var key = (grid, crew.Group);
            if (_alerts.IsAlerted(grid, crew.Group) || _alarms.TryGetValue(key, out var until) && _timing.CurTime < until)
                continue;

            _alarms.Remove(key);
            _posts.Remove(uid);
            if (post is not { } original)
                _crew.SetPost((uid, crew), null);
            else if (!TerminatingOrDeleted(original.EntityId))
                _crew.SetPost((uid, crew), original);
        }
    }
}
