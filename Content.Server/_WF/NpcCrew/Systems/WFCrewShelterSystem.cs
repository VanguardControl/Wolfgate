using Content.Server._WF.NpcCrew.Components;
using Content.Shared._White.Standing;
using Content.Shared._WF.NpcCrew;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Crew who never fight, cowards among them, drop their work and run for the bridge when their ship is alerted,
/// boarded or one of them is attacked; there they cower on the deck, and they go back to their posts a minute after
/// the last alarm.
/// </summary>
public sealed partial class WFCrewShelterSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedLayingDownSystem _laying = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private Content.Server.Chat.Systems.ChatSystem _chat = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _random = default!;

    /// <summary>The pleas a sheltering crewman cries out, aloud and never over the radio.</summary>
    private const int Cries = 5;
    private readonly Dictionary<EntityUid, TimeSpan> _nextCry = new();

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
            _nextCry.Clear();
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

    /// <summary>Whether a crewman is sheltering from an alarm.</summary>
    public bool IsSheltering(EntityUid uid) => _posts.ContainsKey(uid);

    /// <summary>Sends a ship's non-combatants to the bridge, or keeps the alarm they shelter from going.</summary>
    public void Shelter(EntityUid grid, string group)
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
        var work = EntityManager.System<WFCrewWorkSystem>();
        foreach (var member in passive)
        {
            _posts[member] = member.Comp.Post;
            work.CancelWorker(member);
            // With no helm to run to, he gets down where he stands.
            _crew.SetPost((member, member.Comp), bridge ?? Transform(member).Coordinates);
            _nextCry[member] = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0.5f, 2f));
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
                _nextCry.Remove(uid);
                continue;
            }

            if (_crew.HomeGrid(uid, crew) is not { } grid)
                continue;

            var key = (grid, crew.Group);
            if (_alerts.IsAlerted(grid, crew.Group) || _alarms.TryGetValue(key, out var until) && _timing.CurTime < until)
            {
                Cower(uid, crew);
                Cry(uid);
                continue;
            }

            _alarms.Remove(key);
            _posts.Remove(uid);
            _nextCry.Remove(uid);
            _laying.TryStandUp(uid);
            if (post is not { } original)
                _crew.SetPost((uid, crew), null);
            else if (!TerminatingOrDeleted(original.EntityId))
                _crew.SetPost((uid, crew), original);
        }
    }

    /// <summary>A frightened crewman screams and begs aloud every so often while the alarm lasts.</summary>
    private void Cry(EntityUid uid)
    {
        var now = _timing.CurTime;
        if (!_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || _nextCry.TryGetValue(uid, out var next) && now < next)
            return;

        _nextCry[uid] = now + TimeSpan.FromSeconds(_random.NextFloat(7f, 14f));
        if (_random.NextFloat() < 0.5f)
            _chat.TryEmoteWithChat(uid, "Scream", Content.Shared.Chat.ChatTransmitRange.HideChat, hideLog: true);
        else
            _chat.TrySendInGameICMessage(uid, Loc.GetString($"wf-crew-coward-cry-{_random.Next(1, Cries + 1)}"),
                Content.Shared.Chat.InGameICChatType.Speak, hideChat: true, hideLog: true, checkRadioPrefix: false);
    }

    /// <summary>Once at the bridge, a sheltering crewman gets down on the deck until the alarm is over.</summary>
    private void Cower(EntityUid uid, WFCrewComponent crew)
    {
        if (crew.Post is not { } shelter || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || _standing.IsDown(uid)
            || !_transform.InRange(Transform(uid).Coordinates, shelter, crew.PostRange + 0.5f))
            return;

        _laying.TryLieDown(uid);
    }
}
