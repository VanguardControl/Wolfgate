using System.Linq;
using Content.Server._WG.MentorCritters;
using Content.Server._WG.MentorHelp;
using Content.Server._WG.Mentors;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Ghost;
using Content.Server.Popups;
using Content.Shared._WG.MentorCritters;
using Content.Shared._WG.MentorObserve;
using Content.Shared._WG.Mentors;
using Content.Shared.Actions;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Follower;
using Content.Shared.Follower.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.MentorObserve;

/// <summary>
/// Mentors going to the asker of a ticket after the asker allows it: watching (a ghost bound to the asker's view, or a
/// living mentor stepped out into an observer, D10 and D11), or joining as a critter. Ends on return (Shift+F3, the
/// action), the ticket closing, the asker withdrawing, the time limit, or the stepped-out body being hurt.
/// </summary>
public sealed class WGMentorVisitSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WGMentorCritterSystem _critters = default!;
    [Dependency] private SharedCuffableSystem _cuffs = default!;
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private FollowerSystem _follower = default!;
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private WGMentorHelpSystem _help = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private WGMentorSystem _mentors = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedRoleSystem _roles = default!;
    [Dependency] private IGameTiming _timing = default!;

    public static readonly EntProtoId ObserverPrototype = "WGMentorObserver";
    public static readonly EntProtoId ReturnAction = "WGActionMentorReturn";

    /// <summary>The key players press to return, as the notice names it.</summary>
    private const string ReturnKey = "Shift+F3";

    private sealed class Visit
    {
        public int Ticket;
        public NetUserId Mentor;
        public NetUserId Asker;
        public MentorVisitKind Kind;
        public EntityUid AskerBody;

        /// <summary>The mentor's body while they are away from it; null for a ghost.</summary>
        public EntityUid? Body;

        /// <summary>The bound ghost, the observer stepped out into, or the critter.</summary>
        public EntityUid? Vessel;

        /// <summary>The mind went into the critter for good (from a ghost with no body to go back to).</summary>
        public bool Transferred;

        public TimeSpan? Ends;
    }

    private sealed record Pending(NetUserId Mentor, MentorVisitKind Kind, string? Critter);

    private readonly Dictionary<NetUserId, Visit> _visits = new();
    private readonly Dictionary<int, Pending> _pending = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastStepOut = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastHurt = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMentorVisitRequestEvent>(OnRequest);
        SubscribeNetworkEvent<WGMentorVisitAnswerEvent>(OnAnswer);
        SubscribeNetworkEvent<WGMentorVisitEndEvent>(OnAskerEnd);
        SubscribeNetworkEvent<WGMentorReturnRequestEvent>((_, args) => Return(args.SenderSession));

        SubscribeLocalEvent<WGMentorObserverComponent, WGMentorReturnActionEvent>(OnReturnAction);
        SubscribeLocalEvent<WGMentorCritterComponent, WGMentorReturnActionEvent>(OnReturnAction);
        SubscribeLocalEvent<WGMentorAwayComponent, DamageChangedEvent>(OnAwayDamaged);
        SubscribeLocalEvent<WGMentorAwayComponent, MobStateChangedEvent>(OnAwayMobState);
        // Every EntityTerminatingEvent subscription here takes the same order: ahead of the mind system, so a body that
        // goes while its mentor is away doesn't hand the mind to the observer or critter for good.
        SubscribeLocalEvent<WGMentorAwayComponent, EntityTerminatingEvent>(OnBodyTerminating, before: new[] { typeof(Content.Server.Mind.MindSystem) });
        SubscribeLocalEvent<WGMentorAwayComponent, ExaminedEvent>(OnAwayExamined);
        SubscribeLocalEvent<WGMentorMarkComponent, DamageChangedEvent>(OnMentorDamaged);
        SubscribeLocalEvent<WGMentorWatcherComponent, StoppedFollowingEntityEvent>(OnStoppedFollowing);
        SubscribeLocalEvent<WGMentorObserverComponent, EntityTerminatingEvent>(OnObserverTerminating, before: new[] { typeof(Content.Server.Mind.MindSystem) });
        SubscribeLocalEvent<WGMentorCritterComponent, EntityTerminatingEvent>(OnCritterTerminating, before: new[] { typeof(Content.Server.Mind.MindSystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());

        _help.TicketClosed += OnTicketClosed;
        _critters.RidingChanged += Refresh;
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _help.TicketClosed -= OnTicketClosed;
        _critters.RidingChanged -= Refresh;
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    /// <summary>A mentor's visit, if they are on one: the ticket and how.</summary>
    public (int Ticket, MentorVisitKind Kind)? VisitOf(NetUserId mentor)
    {
        return _visits.TryGetValue(mentor, out var visit) ? (visit.Ticket, visit.Kind) : null;
    }

    /// <summary>Whether anyone is visiting this asker.</summary>
    public bool IsVisited(NetUserId asker)
    {
        return _visits.Values.Any(v => v.Asker == asker);
    }

    #region Asking

    private void OnRequest(WGMentorVisitRequestEvent ev, EntitySessionEventArgs args)
    {
        if (RequestVisit(args.SenderSession, ev) is { } refusal)
            Notify(args.SenderSession, refusal);
    }

    /// <summary>A responder asks the asker of a ticket to let them watch or join. Returns a refusal, or null once the asker is asked.</summary>
    public string? RequestVisit(ICommonSession mentor, WGMentorVisitRequestEvent ev)
    {
        if (Refusal(mentor, ev) is { } refusal)
            return refusal;

        var ticket = _help.Tickets[ev.TicketId];
        _pending[ev.TicketId] = new Pending(mentor.UserId, ev.Kind, ev.Critter);

        var animal = ev.Kind == MentorVisitKind.Critter && ev.Critter != null && _proto.TryIndex<WGMentorCritterPrototype>(ev.Critter, out var critter)
            ? Loc.GetString(critter.Animal)
            : "";

        if (_players.TryGetSessionById(ticket.Player, out var asker))
            RaiseNetworkEvent(new WGMentorVisitPromptEvent { TicketId = ev.TicketId, Kind = ev.Kind, Animal = animal }, asker);

        SendState(mentor.UserId);
        return null;
    }

    /// <summary>Why a responder may not ask to visit a ticket's asker now, or null.</summary>
    private string? Refusal(ICommonSession mentor, WGMentorVisitRequestEvent ev)
    {
        if (!_help.Tickets.TryGetValue(ev.TicketId, out var ticket) || ticket.Closed || !_help.CanSee(mentor, ticket))
            return Loc.GetString("wg-mentor-watch-refused-ticket");

        if (ticket.Claimer != mentor.UserId && !_help.IsStaff(mentor))
            return Loc.GetString("wg-mentor-watch-refused-ticket");

        if (_visits.ContainsKey(mentor.UserId) || _pending.ContainsKey(ev.TicketId))
            return Loc.GetString("wg-mentor-watch-refused-busy");

        if (!_players.TryGetSessionById(ticket.Player, out var asker) || asker.AttachedEntity is not { } askerBody
            || HasComp<GhostComponent>(askerBody))
        {
            return Loc.GetString("wg-mentor-watch-refused-asker");
        }

        if (mentor.AttachedEntity is not { } body)
            return Loc.GetString("wg-mentor-watch-refused-lobby");

        var senior = _help.IsStaff(mentor) || _mentors.GetTier(mentor.UserId) is MentorTier.Mentor or MentorTier.Senior;
        if (ev.Kind == MentorVisitKind.Critter)
        {
            if (!_cfg.GetCVar(MentorObserveCVars.Critters) || ev.Critter == null
                || !_proto.HasIndex<WGMentorCritterPrototype>(ev.Critter))
            {
                return Loc.GetString("wg-mentor-watch-refused-ticket");
            }

            if (!senior)
                return Loc.GetString("wg-mentor-watch-refused-trainee");
        }

        // A ghost only orbits; stepping out of a living body has the D10 limits, and Trainees orbit only (D4).
        if (HasComp<GhostComponent>(body))
            return null;

        if (ev.Kind == MentorVisitKind.Watch && !senior)
            return Loc.GetString("wg-mentor-watch-refused-trainee");

        return StepOutRefusal(mentor, body);
    }

    /// <summary>Why a living mentor may not leave their body now, or null.</summary>
    private string? StepOutRefusal(ICommonSession mentor, EntityUid body)
    {
        var now = _timing.CurTime;
        if (_lastStepOut.TryGetValue(mentor.UserId, out var last) && now - last < MentorObserveRules.Cooldown)
        {
            var wait = (int) Math.Ceiling((MentorObserveRules.Cooldown - (now - last)).TotalSeconds);
            return Loc.GetString("wg-mentor-watch-refused-cooldown", ("seconds", wait));
        }

        if (!_mobState.IsAlive(body)
            || TryComp<CuffableComponent>(body, out var cuffable) && _cuffs.IsCuffed((body, cuffable))
            || _lastHurt.TryGetValue(body, out var hurt) && now - hurt < MentorObserveRules.RecentDamage)
        {
            return Loc.GetString("wg-mentor-watch-refused-danger");
        }

        if (_mind.TryGetMind(mentor, out var mindId, out _) && _roles.MindIsAntagonist(mindId))
            return Loc.GetString("wg-mentor-watch-refused-antag");

        return null;
    }

    private void OnAnswer(WGMentorVisitAnswerEvent ev, EntitySessionEventArgs args)
    {
        AnswerVisit(args.SenderSession, ev.TicketId, ev.Allow);
    }

    /// <summary>The asker allows or refuses the visit waiting on their ticket.</summary>
    public void AnswerVisit(ICommonSession asker, int ticketId, bool allow)
    {
        var ev = new WGMentorVisitAnswerEvent { TicketId = ticketId, Allow = allow };
        if (!_pending.Remove(ev.TicketId, out var pending)
            || !_help.Tickets.TryGetValue(ev.TicketId, out var ticket)
            || ticket.Player != asker.UserId
            || !_players.TryGetSessionById(pending.Mentor, out var mentor))
        {
            return;
        }

        if (!ev.Allow)
        {
            Notify(mentor, Loc.GetString("wg-mentor-watch-denied"));
            SendState(mentor.UserId);
            return;
        }

        // Things may have changed while the asker read the prompt.
        var request = new WGMentorVisitRequestEvent { TicketId = ev.TicketId, Kind = pending.Kind, Critter = pending.Critter };
        if (Refusal(mentor, request) is { } refusal)
        {
            Notify(mentor, refusal);
            SendState(mentor.UserId);
            return;
        }

        Start(mentor, asker, ticket, pending);
    }

    #endregion

    #region Starting

    private void Start(ICommonSession mentor, ICommonSession asker, MentorTicket ticket, Pending pending)
    {
        var askerBody = asker.AttachedEntity!.Value;
        var body = mentor.AttachedEntity!.Value;
        var visit = new Visit
        {
            Ticket = ticket.Id,
            Mentor = mentor.UserId,
            Asker = asker.UserId,
            Kind = pending.Kind,
            AskerBody = askerBody,
        };

        var ok = pending.Kind == MentorVisitKind.Watch
            ? StartWatch(mentor, body, visit)
            : StartCritter(mentor, body, visit, pending.Critter!, ticket);

        if (!ok)
            return;

        _visits[mentor.UserId] = visit;
        var kind = pending.Kind == MentorVisitKind.Watch ? "watch" : "critter";
        var staffText = Loc.GetString("wg-mentor-watch-staff-start", ("mentor", mentor.Name), ("character", ticket.CharacterName));
        _chat.SendAdminAlert(staffText);
        _help.RecordVisit(ticket, mentor.UserId, kind, staffText);
        _adminLog.Add(LogType.WGMentor, LogImpact.Low,
            $"{mentor.Name} started a mentor {kind} of {asker.Name} from ticket #{ticket.Id}");

        if (pending.Kind == MentorVisitKind.Watch)
            Notify(mentor, Loc.GetString("wg-mentor-watch-started", ("character", ticket.CharacterName), ("key", ReturnKey)));

        SendState(mentor.UserId);
        SendState(asker.UserId);
    }

    private bool StartWatch(ICommonSession mentor, EntityUid body, Visit visit)
    {
        if (HasComp<GhostComponent>(body))
        {
            // D11: the ghost sees what the asker sees until it moves away or the watch ends.
            _follower.StartFollowingEntity(body, visit.AskerBody);
            _eye.SetTarget(body, visit.AskerBody);
            _eye.SetDrawFov(body, true);
            EnsureComp<WGMentorWatcherComponent>(body).Asker = visit.AskerBody;
            visit.Vessel = body;
            return true;
        }

        if (!_mind.TryGetMind(mentor, out var mindId, out var mind) || mind.VisitingEntity != null)
            return false;

        var observer = Spawn(ObserverPrototype, Transform(visit.AskerBody).Coordinates);
        _mind.Visit(mindId, observer, mind);
        if (mind.VisitingEntity != observer)
        {
            QueueDel(observer);
            return false;
        }

        _follower.StartFollowingEntity(observer, visit.AskerBody);
        _eye.SetTarget(observer, visit.AskerBody);
        _actions.AddAction(observer, ReturnAction);

        EnsureComp<WGMentorAwayComponent>(body);
        visit.Body = body;
        visit.Vessel = observer;
        visit.Ends = _timing.CurTime + TimeSpan.FromMinutes(_cfg.GetCVar(MentorObserveCVars.StepOutMinutes));
        return true;
    }

    private bool StartCritter(ICommonSession mentor, EntityUid body, Visit visit, string critterId, MentorTicket ticket)
    {
        if (!_mind.TryGetMind(mentor, out var mindId, out var mind)
            || !_proto.TryIndex<WGMentorCritterPrototype>(critterId, out var proto))
        {
            return false;
        }

        var critter = _critters.Spawn(proto, visit.AskerBody, ticket.Id);
        _actions.AddAction(critter, ReturnAction);

        if (HasComp<GhostComponent>(body) && mind.VisitingEntity != body)
        {
            // A ghost with no body to go back to goes into the critter for good, and gets a new ghost after.
            _mind.TransferTo(mindId, critter, mind: mind);
            visit.Transferred = true;
        }
        else
        {
            if (mind.VisitingEntity != null)
                _mind.UnVisit(mindId, mind);

            _mind.Visit(mindId, critter, mind);
            if (mind.VisitingEntity != critter)
            {
                QueueDel(critter);
                return false;
            }

            if (mind.OwnedEntity is { } owned && !HasComp<GhostComponent>(owned))
            {
                EnsureComp<WGMentorAwayComponent>(owned);
                visit.Body = owned;
            }
        }

        visit.Vessel = critter;
        return true;
    }

    #endregion

    #region Ending

    /// <summary>The mentor goes back: from the observer or critter to the body, or a ghost stops following.</summary>
    public void Return(ICommonSession mentor)
    {
        if (_visits.TryGetValue(mentor.UserId, out var visit))
            End(visit, null);
    }

    private void OnReturnAction(EntityUid uid, Component comp, WGMentorReturnActionEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.Performer, out var actor))
            return;

        args.Handled = true;
        Return(actor.PlayerSession);
    }

    private void OnAskerEnd(WGMentorVisitEndEvent ev, EntitySessionEventArgs args)
    {
        var asker = args.SenderSession.UserId;
        foreach (var visit in _visits.Values.Where(v => v.Asker == asker).ToList())
        {
            if (ev.TakeOutOnly)
            {
                if (visit.Kind == MentorVisitKind.Critter && visit.Vessel is { } critter)
                    _critters.TakeOut(critter);

                continue;
            }

            End(visit, "wg-mentor-return-withdrawn");
        }

        SendState(asker);
    }

    private void OnTicketClosed(MentorTicket ticket)
    {
        foreach (var visit in _visits.Values.Where(v => v.Ticket == ticket.Id).ToList())
        {
            End(visit, "wg-mentor-return-closed");
        }

        if (_pending.Remove(ticket.Id, out var pending))
            SendState(pending.Mentor);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        foreach (var visit in _visits.Values.Where(v => v.Mentor == args.Session.UserId || v.Asker == args.Session.UserId).ToList())
        {
            End(visit, visit.Asker == args.Session.UserId ? "wg-mentor-return-withdrawn" : null);
        }
    }

    private void OnAwayDamaged(EntityUid uid, WGMentorAwayComponent comp, DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        _lastHurt[uid] = _timing.CurTime;
        EndForBody(uid, "wg-mentor-return-hurt");
    }

    private void OnAwayMobState(EntityUid uid, WGMentorAwayComponent comp, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            EndForBody(uid, "wg-mentor-return-hurt");
    }

    private void OnBodyTerminating(EntityUid uid, WGMentorAwayComponent comp, ref EntityTerminatingEvent args)
    {
        EndForBody(uid, null);
    }

    private void OnAwayExamined(EntityUid uid, WGMentorAwayComponent comp, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-mentor-away-examine"));
    }

    private void OnMentorDamaged(EntityUid uid, WGMentorMarkComponent comp, DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            _lastHurt[uid] = _timing.CurTime;
    }

    private void OnStoppedFollowing(EntityUid uid, WGMentorWatcherComponent comp, StoppedFollowingEntityEvent args)
    {
        if (_visits.Values.FirstOrDefault(v => v.Vessel == uid && v.Body == null) is { } visit)
            End(visit, null, stopFollowing: false);
    }

    private void OnObserverTerminating(EntityUid uid, WGMentorObserverComponent comp, ref EntityTerminatingEvent args)
    {
        VesselGone(uid);
    }

    private void OnCritterTerminating(EntityUid uid, WGMentorCritterComponent comp, ref EntityTerminatingEvent args)
    {
        VesselGone(uid);
    }

    private void VesselGone(EntityUid vessel)
    {
        if (_visits.Values.FirstOrDefault(v => v.Vessel == vessel) is { } visit)
            End(visit, null, vesselGone: true);
    }

    private void EndForBody(EntityUid body, string? reason)
    {
        if (_visits.Values.FirstOrDefault(v => v.Body == body) is { } visit)
            End(visit, reason);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + TimeSpan.FromSeconds(1);
        foreach (var visit in _visits.Values.Where(v => v.Ends <= now).ToList())
        {
            End(visit, "wg-mentor-return-time");
        }
    }

    /// <summary>Ends a visit: the mentor goes back, the vessel goes, and both sides and staff are told.</summary>
    private void End(Visit visit, string? reason, bool stopFollowing = true, bool vesselGone = false)
    {
        if (!_visits.Remove(visit.Mentor))
            return;

        _players.TryGetSessionById(visit.Mentor, out var mentor);
        if (visit.Vessel is { } vessel && Exists(vessel))
        {
            if (visit.Body == null && visit.Kind == MentorVisitKind.Watch)
            {
                // A bound ghost: let it go.
                RemComp<WGMentorWatcherComponent>(vessel);
                if (stopFollowing && TryComp<FollowerComponent>(vessel, out var follower))
                    _follower.StopFollowingEntity(vessel, follower.Following);

                _eye.SetTarget(vessel, null);
                _eye.SetDrawFov(vessel, false);
            }
            else
            {
                if (mentor != null && _mind.TryGetMind(mentor, out var mindId, out var mind))
                {
                    if (visit.Transferred)
                        _ghost.SpawnGhost((mindId, mind), vessel);
                    else if (mind.VisitingEntity == vessel)
                        _mind.UnVisit(mindId, mind);
                }

                if (visit.Kind == MentorVisitKind.Critter)
                    _critters.Despawn(vessel, visit.AskerBody);
                else if (!vesselGone)
                    QueueDel(vessel);
            }
        }

        if (visit.Body is { } body)
        {
            RemComp<WGMentorAwayComponent>(body);
            _lastStepOut[visit.Mentor] = _timing.CurTime;
        }

        if (mentor != null && reason != null)
            Notify(mentor, Loc.GetString(reason));

        var character = _help.Tickets.TryGetValue(visit.Ticket, out var ticket) ? ticket.CharacterName : "";
        _chat.SendAdminAlert(Loc.GetString("wg-mentor-watch-staff-end",
            ("mentor", mentor?.Name ?? visit.Mentor.ToString()), ("character", character)));
        _adminLog.Add(LogType.WGMentor, LogImpact.Low,
            $"{mentor?.Name ?? visit.Mentor.ToString()} ended a mentor visit to ticket #{visit.Ticket}{(reason != null ? $" ({reason})" : "")}");

        SendState(visit.Mentor);
        SendState(visit.Asker);
    }

    private void Clear()
    {
        _visits.Clear();
        _pending.Clear();
        _lastHurt.Clear();
    }

    #endregion

    /// <summary>Tells one player where their visits stand, as mentor and as asker.</summary>
    private void SendState(NetUserId user)
    {
        if (!_players.TryGetSessionById(user, out var session) || session.Status != SessionStatus.InGame)
            return;

        var ev = new WGMentorVisitStateEvent
        {
            Pending = _pending.Where(p => p.Value.Mentor == user).Select(p => p.Key).ToList(),
        };

        foreach (var visit in _visits.Values)
        {
            if (visit.Mentor == user)
            {
                ev.VisitingTicket = visit.Ticket;
                ev.VisitingKind = visit.Kind;
                ev.VisitingAsGhost = visit.Kind == MentorVisitKind.Watch && visit.Body == null;
            }

            if (visit.Asker != user)
                continue;

            if (visit.Kind == MentorVisitKind.Watch)
            {
                ev.Watched = true;
            }
            else
            {
                ev.CritterWithMe = true;
                ev.CritterRiding = visit.Vessel is { } critter && _critters.IsRiding(critter);
            }
        }

        RaiseNetworkEvent(ev, session);
    }

    /// <summary>Sends the asker's state again, as after the critter climbs on or off.</summary>
    public void Refresh(EntityUid askerBody)
    {
        foreach (var visit in _visits.Values.Where(v => v.AskerBody == askerBody).ToList())
        {
            SendState(visit.Asker);
        }
    }

    private void Notify(ICommonSession session, string text)
    {
        if (session.Status != SessionStatus.Disconnected)
            _chat.DispatchServerMessage(session, text);
    }
}
