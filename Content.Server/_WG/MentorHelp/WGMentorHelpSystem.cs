using System.Linq;
using System.Threading.Tasks;
using Content.Server._WG.Mentors;
using Content.Server._WG.Newbie;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Administration.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._WG.EventStaff;
using Content.Shared._WG.MentorHelp;
using Content.Shared._WG.Mentors;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WG.MentorHelp;

/// <summary>What became of a request to open a ticket.</summary>
public enum MentorHelpOpenResult : byte
{
    Opened,
    Disabled,
    Muted,
    Empty,
    TooLong,
    Cooldown,
    AlreadyOpen,
}

/// <summary>
/// Mentor Help tickets: opened by players, answered by on-duty mentors and staff, closed by resolve, the player, idling,
/// the staff fallback or the round's end. Tickets live in memory for the round; every message and state change is
/// written to the database in the background. Names follow D6: the asker sees responders only as "Mentor", responders
/// see the asker only by character name, staff see both.
/// </summary>
public sealed class WGMentorHelpSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IBanManager _bans = default!;
    [Dependency] private BwoinkSystem _bwoink = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private WGMentorHelpDiscordSystem _discord = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private WGMentorSystem _mentors = default!;
    [Dependency] private WGNewbieTagSystem _newbie = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Longest message a player or mentor may send.</summary>
    public const int MaxLength = 1000;

    /// <summary>Longest rating comment.</summary>
    public const int MaxCommentLength = 200;

    /// <summary>How long a closed ticket may be reopened, rated and is still shown.</summary>
    public static readonly TimeSpan ReopenWindow = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>How many quick retries a ticket's row gets before retrying once a minute.</summary>
    private const int InsertAttempts = 5;

    private static readonly TimeSpan InsertRetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InsertSlowRetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>How long a typing mark lasts without a fresh one.</summary>
    private static readonly TimeSpan TypingTimeout = TimeSpan.FromSeconds(10);

    private readonly Dictionary<int, MentorTicket> _tickets = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastOpened = new();
    private int _nextId = 1;
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WGMentorHelpOpenRequest>(OnOpenRequest);
        SubscribeNetworkEvent<WGMentorHelpPostRequest>(OnPostRequest);
        SubscribeNetworkEvent<WGMentorHelpActionRequest>(OnActionRequest);
        SubscribeNetworkEvent<WGMentorHelpPlayerActionRequest>(OnPlayerActionRequest);
        SubscribeNetworkEvent<WGMentorHelpRefreshRequest>(OnRefreshRequest);
        SubscribeNetworkEvent<WGMentorHelpFromAHelpRequest>(OnFromAHelpRequest);
        SubscribeNetworkEvent<WGMentorHelpTypingRequest>(OnTypingRequest);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PlayerAttachedEvent>(args => OnAttachChanged(args.Player));
        SubscribeLocalEvent<PlayerDetachedEvent>(args => OnAttachChanged(args.Player));

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        _admin.OnPermsChanged += OnPermsChanged;
        _mentors.StatusChanged += OnMentorStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        _admin.OnPermsChanged -= OnPermsChanged;
        _mentors.StatusChanged -= OnMentorStatusChanged;
    }

    /// <summary>Every ticket held this round, open and recently closed.</summary>
    public IReadOnlyDictionary<int, MentorTicket> Tickets => _tickets;

    /// <summary>Whether a connected player has a Mentor Help mute (the <c>Mentor:Help</c> role ban).</summary>
    public bool IsMuted(NetUserId player)
    {
        return _bans.GetRoleBans(player)?.Contains(MentorBans.HelpRole) == true;
    }

    /// <summary>A player's open ticket, or null.</summary>
    public MentorTicket? GetOpenTicket(NetUserId player)
    {
        return _tickets.Values.FirstOrDefault(t => t.Player == player && !t.Closed);
    }

    /// <summary>A player's latest ticket this round, open or closed, or null.</summary>
    public MentorTicket? GetLatestTicket(NetUserId player)
    {
        return _tickets.Values.Where(t => t.Player == player).MaxBy(t => t.Id);
    }

    /// <summary>Staff who see every ticket: the Adminhelp or Event flag.</summary>
    public bool IsStaff(ICommonSession session)
    {
        return _admin.GetAdminData(session) is { Active: true } data
               && (data.HasFlag(AdminFlags.Adminhelp) || data.HasFlag(AdminFlags.Event));
    }

    /// <summary>Whether a player answers tickets: a mentor on duty, or staff.</summary>
    public bool IsResponder(ICommonSession session)
    {
        return IsStaff(session) || _mentors.IsOnDuty(session.UserId);
    }

    /// <summary>
    /// Whether a responder may see a ticket. A spoiler-safe one only reaches mentors who are out of the round, and
    /// staff. Nobody answers their own question.
    /// </summary>
    public bool CanSee(ICommonSession session, MentorTicket ticket)
    {
        if (session.UserId == ticket.Player || !IsResponder(session))
            return false;

        return !ticket.Secret || IsStaff(session) || !InRound(session);
    }

    /// <summary>Whether a player has a living character in the round (not in the lobby, not a ghost or observer).</summary>
    public bool InRound(ICommonSession session)
    {
        return session.AttachedEntity is { } entity && !HasComp<GhostComponent>(entity);
    }

    #region Asker

    /// <summary>Opens a ticket for a player.</summary>
    public MentorHelpOpenResult Open(ICommonSession player, string text, string? topic, bool secret, bool fromAhelp = false)
    {
        if (!_cfg.GetCVar(MentorCVars.Enabled))
            return MentorHelpOpenResult.Disabled;

        if (IsMuted(player.UserId))
            return MentorHelpOpenResult.Muted;

        if (!Clean(text, MaxLength, out var clean, out var refusal))
            return refusal;

        if (GetOpenTicket(player.UserId) != null)
            return MentorHelpOpenResult.AlreadyOpen;

        var now = _timing.RealTime;
        var cooldown = TimeSpan.FromSeconds(_cfg.GetCVar(MentorHelpCVars.TicketCooldown));
        if (!fromAhelp && _lastOpened.TryGetValue(player.UserId, out var last) && now - last < cooldown)
            return MentorHelpOpenResult.Cooldown;

        _lastOpened[player.UserId] = now;
        DropClosed(player.UserId);

        var ticket = new MentorTicket
        {
            Id = _nextId++,
            Player = player.UserId,
            PlayerName = player.Name,
            CharacterName = CharacterName(player),
            Topic = topic != null && MentorHelpTopics.All.Contains(topic) ? topic : null,
            Secret = secret,
            Newbie = _newbie.IsTagged(player),
            FromAhelp = fromAhelp,
            OpenedAt = DateTime.UtcNow,
            OpenedTime = now,
            FallbackFrom = now,
            LastMessageTime = now,
        };

        _tickets[ticket.Id] = ticket;
        Insert(ticket);
        _discord.Opened(ticket);
        AddLine(ticket, MentorSenderRole.Player, player, clean);

        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low,
            $"{player.Name} opened mentor ticket #{ticket.Id}{(secret ? " (secret role)" : "")}: {clean}");

        Publish(ticket, alert: true);
        return MentorHelpOpenResult.Opened;
    }

    /// <summary>The asker adds to their open ticket. False when they have none or the text is refused.</summary>
    public bool PostAsPlayer(ICommonSession player, string text)
    {
        if (GetOpenTicket(player.UserId) is not { } ticket || !Clean(text, MaxLength, out var clean, out _))
            return false;

        AddLine(ticket, MentorSenderRole.Player, player, clean);
        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"{player.Name} in mentor ticket #{ticket.Id}: {clean}");
        Publish(ticket, alert: true);
        return true;
    }

    /// <summary>The asker ends their open ticket.</summary>
    public bool End(ICommonSession player)
    {
        if (GetOpenTicket(player.UserId) is not { } ticket)
            return false;

        Close(ticket, MentorCloseReason.Player, player.Name);
        return true;
    }

    /// <summary>The asker reopens their ticket within the reopen window, after a resolve, their own end or idling.</summary>
    public bool Reopen(ICommonSession player)
    {
        if (GetOpenTicket(player.UserId) != null
            || GetLatestTicket(player.UserId) is not { Closed: true } ticket
            || !CanReopen(ticket))
        {
            return false;
        }

        ticket.Closed = false;
        ticket.CloseReason = null;
        ticket.ClosedAt = null;
        ticket.LastMessageTime = _timing.RealTime;
        // An unanswered ticket gets a full claim window again before it falls back to staff.
        ticket.FallbackFrom = _timing.RealTime;
        Update(ticket);

        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"{player.Name} reopened mentor ticket #{ticket.Id}");
        Publish(ticket, alert: true);
        return true;
    }

    /// <summary>The asker rates their closed, answered ticket once.</summary>
    public bool Rate(ICommonSession player, bool up, string? comment)
    {
        if (GetLatestTicket(player.UserId) is not { Closed: true, Answered: true, Rating: null } ticket)
            return false;

        ticket.Rating = up ? 1 : -1;
        if (comment != null && Clean(comment, MaxCommentLength, out var clean, out _))
            ticket.RatingComment = clean;

        Update(ticket);
        if (ticket.Claimer is { } claimer)
            _ = PromoteAfterWrites(ticket, claimer);

        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low,
            $"{player.Name} rated mentor ticket #{ticket.Id} {(up ? "up" : "down")}{(ticket.RatingComment != null ? $": {ticket.RatingComment}" : "")}");
        Publish(ticket);
        return true;
    }

    #endregion

    #region Responders

    /// <summary>A responder posts in a ticket. Claims it when nobody has.</summary>
    public bool PostAsResponder(ICommonSession responder, int id, string text)
    {
        if (!_tickets.TryGetValue(id, out var ticket) || ticket.Closed || !CanSee(responder, ticket))
            return false;

        if (!Clean(text, MaxLength, out var clean, out _))
            return false;

        ticket.Claimer ??= responder.UserId;
        ticket.ClaimerName ??= responder.Name;
        ticket.Answered = true;
        ticket.FirstAnswerAt ??= DateTime.UtcNow;

        AddLine(ticket, IsStaff(responder) ? MentorSenderRole.Staff : MentorSenderRole.Mentor, responder, clean);
        Update(ticket);

        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"{responder.Name} in mentor ticket #{ticket.Id}: {clean}");
        Publish(ticket, alert: true);
        return true;
    }

    /// <summary>A responder takes an unclaimed ticket.</summary>
    public bool Claim(ICommonSession responder, int id)
    {
        if (!_tickets.TryGetValue(id, out var ticket) || ticket.Closed || ticket.Claimer != null || !CanSee(responder, ticket))
            return false;

        SetClaimer(ticket, responder.UserId, responder.Name);
        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"{responder.Name} claimed mentor ticket #{ticket.Id}");
        Publish(ticket);
        return true;
    }

    /// <summary>The claimer, or staff, lets a ticket go back to the queue.</summary>
    public bool Unclaim(ICommonSession responder, int id)
    {
        if (!_tickets.TryGetValue(id, out var ticket) || ticket.Closed || ticket.Claimer == null || !CanSee(responder, ticket))
            return false;

        if (ticket.Claimer != responder.UserId && !IsStaff(responder))
            return false;

        SetClaimer(ticket, null, null);
        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"{responder.Name} unclaimed mentor ticket #{ticket.Id}");
        Publish(ticket);
        return true;
    }

    /// <summary>A responder takes over a ticket another holds, with a note other responders see.</summary>
    public bool TakeOver(ICommonSession responder, int id, string? note)
    {
        if (!_tickets.TryGetValue(id, out var ticket) || ticket.Closed || ticket.Claimer == null
            || ticket.Claimer == responder.UserId || !CanSee(responder, ticket))
        {
            return false;
        }

        var from = ticket.ClaimerName;
        SetClaimer(ticket, responder.UserId, responder.Name);
        string? accepted = null;
        if (note != null && Clean(note, MaxLength, out var clean, out _))
        {
            accepted = clean;
            AddLine(ticket, MentorSenderRole.Mentor, responder, clean, respondersOnly: true);
        }

        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low,
            $"{responder.Name} took over mentor ticket #{ticket.Id} from {from}{(accepted != null ? $": {accepted}" : "")}");
        Publish(ticket);
        return true;
    }

    /// <summary>The claimer, or staff, hands a ticket to another responder who may see it.</summary>
    public bool Transfer(ICommonSession responder, int id, string target)
    {
        if (!_tickets.TryGetValue(id, out var ticket) || ticket.Closed || !CanSee(responder, ticket))
            return false;

        if (ticket.Claimer != null && ticket.Claimer != responder.UserId && !IsStaff(responder))
            return false;

        if (!_players.TryGetSessionByUsername(target, out var to) || to == responder || !CanSee(to, ticket))
            return false;

        SetClaimer(ticket, to.UserId, to.Name);
        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low,
            $"{responder.Name} transferred mentor ticket #{ticket.Id} to {to.Name}");
        Publish(ticket, alert: true);
        return true;
    }

    /// <summary>
    /// Marks a ticket answered. The claimer or staff may; on an unclaimed ticket any responder may, and is credited.
    /// </summary>
    public bool Resolve(ICommonSession responder, int id)
    {
        if (!_tickets.TryGetValue(id, out var ticket) || ticket.Closed || !CanSee(responder, ticket))
            return false;

        if (ticket.Claimer != null && ticket.Claimer != responder.UserId && !IsStaff(responder))
            return false;

        if (ticket.Claimer == null)
            SetClaimer(ticket, responder.UserId, responder.Name);

        Close(ticket, MentorCloseReason.Resolved, responder.Name);
        return true;
    }

    /// <summary>
    /// Sends a ticket to staff as a Gameplay ahelp whose first line names the ticket, with the conversation under it,
    /// and closes it. The claimer or staff may; on an unclaimed ticket any responder may.
    /// </summary>
    public bool Escalate(ICommonSession responder, int id)
    {
        if (!_tickets.TryGetValue(id, out var ticket) || ticket.Closed || !CanSee(responder, ticket))
            return false;

        if (ticket.Claimer != null && ticket.Claimer != responder.UserId && !IsStaff(responder))
            return false;

        var header = Loc.GetString("wg-mhelp-ahelp-escalated", ("ticket", ticket.Id), ("mentor", responder.Name));
        _bwoink.RelayMentorTicket(ticket.Player, AHelpCategory.Gameplay, Tag(ticket), $"{header}\n{Transcript(ticket)}");
        Close(ticket, MentorCloseReason.Escalated, responder.Name);
        return true;
    }

    /// <summary>The asker reports their latest ticket to staff as a Player-issue ahelp with the conversation. Once a ticket.</summary>
    public bool Report(ICommonSession player)
    {
        if (GetLatestTicket(player.UserId) is not { Answered: true, Reported: false } ticket)
            return false;

        ticket.Reported = true;
        var header = Loc.GetString("wg-mhelp-ahelp-reported", ("ticket", ticket.Id));
        _bwoink.RelayMentorTicket(ticket.Player, AHelpCategory.Player, Tag(ticket), $"{header}\n{Transcript(ticket)}");
        Notice(player.UserId, Loc.GetString("wg-mhelp-report-sent"));
        Publish(ticket);

        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Medium, $"{player.Name} reported mentor ticket #{ticket.Id} to staff");
        return true;
    }

    /// <summary>
    /// Staff move a player's ahelp to Mentor Help: a ticket opens with the player's last ahelp message, both sides are
    /// told, and the ahelp is resolved. False when the player isn't connected or wrote nothing.
    /// </summary>
    public bool OpenFromAHelp(ICommonSession staff, NetUserId player)
    {
        if (!IsStaff(staff) || !_players.TryGetSessionById(player, out var session) || session.Status == SessionStatus.Disconnected)
            return false;

        if (_bwoink.LastPlayerAHelpText(player) is not { Length: > 0 } text)
            return false;

        if (GetOpenTicket(player) is { } open)
        {
            if (!Clean(text, MaxLength, out _, out _))
                return false;

            // Set before posting, so responders get the flag with the line; posting writes it with the ticket state.
            var wasFromAhelp = open.FromAhelp;
            open.FromAhelp = true;
            if (!PostAsPlayer(session, text))
            {
                open.FromAhelp = wasFromAhelp;
                return false;
            }

            Update(open);
        }
        else if (Open(session, text, null, false, fromAhelp: true) != MentorHelpOpenResult.Opened)
        {
            return false;
        }

        _bwoink.PostAHelpNote(player,
            Loc.GetString("wg-mhelp-ahelp-sent", ("admin", staff.Name)),
            Loc.GetString("wg-mhelp-from-ahelp"));
        _bwoink.ResolveAHelp(player, staff.Name);
        Notice(player, Loc.GetString("wg-mhelp-from-ahelp"));

        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"{staff.Name} sent the ahelp of {session.Name} to Mentor Help");
        return true;
    }

    private string Tag(MentorTicket ticket)
    {
        return Loc.GetString("wg-mhelp-ahelp-tag", ("ticket", ticket.Id));
    }

    /// <summary>The conversation as staff see it: the asker by character and username, responders by username.</summary>
    private static string Transcript(MentorTicket ticket)
    {
        var lines = ticket.Lines.Select(l => l.Role switch
        {
            MentorSenderRole.Player => $"{ticket.CharacterName} ({ticket.PlayerName}): {l.Text}",
            MentorSenderRole.System => l.Text,
            _ => $"{l.SenderName}: {l.Text}",
        });

        return string.Join("\n", lines);
    }

    #endregion

    #region Closing and timers

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.RealTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;

        var fallback = TimeSpan.FromMinutes(_cfg.GetCVar(MentorHelpCVars.FallbackMinutes));
        var idle = TimeSpan.FromMinutes(_cfg.GetCVar(MentorHelpCVars.IdleCloseMinutes));

        foreach (var ticket in _tickets.Values.ToList())
        {
            if (ticket.Closed)
            {
                if (now - ticket.ClosedTime >= ReopenWindow)
                    Drop(ticket);

                continue;
            }

            if (ticket.Typing.Count > 0)
            {
                var before = ticket.Typing.Count;
                foreach (var (user, until) in ticket.Typing.ToList())
                {
                    if (until <= now)
                        ticket.Typing.Remove(user);
                }

                if (ticket.Typing.Count != before)
                    Publish(ticket);
            }

            if (ticket.Claimer == null && !ticket.Answered && now - ticket.FallbackFrom >= fallback)
                Fallback(ticket);
            else if (now - ticket.LastMessageTime >= idle)
                Close(ticket, MentorCloseReason.Idle, null);
        }
    }

    /// <summary>Closes a ticket and tells the asker.</summary>
    public void Close(MentorTicket ticket, MentorCloseReason reason, string? by)
    {
        if (ticket.Closed)
            return;

        ticket.Closed = true;
        ticket.CloseReason = reason;
        ticket.ClosedTime = _timing.RealTime;
        ticket.ClosedAt = DateTime.UtcNow;
        Update(ticket);
        if (ticket.Claimer is { } claimer && (reason == MentorCloseReason.Resolved || reason == MentorCloseReason.Player && ticket.Answered))
            _ = PromoteAfterWrites(ticket, claimer);

        var notice = reason switch
        {
            MentorCloseReason.Idle => ticket.Answered ? "wg-mhelp-idle-closed-answered" : "wg-mhelp-idle-closed",
            MentorCloseReason.RoundEnd => "wg-mhelp-round-closed",
            MentorCloseReason.Fallback => "wg-mhelp-fallback",
            MentorCloseReason.Escalated => "wg-mhelp-escalated-player",
            _ => null,
        };

        if (notice != null)
            Notice(ticket.Player, Loc.GetString(notice));

        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low,
            $"Mentor ticket #{ticket.Id} of {ticket.PlayerName} closed ({ReasonToDb(reason)}){(by != null ? $" by {by}" : "")}");
        _discord.Closed(ticket);
        Publish(ticket);
    }

    /// <summary>Nobody claimed it in time: the question goes to staff as a Gameplay ahelp.</summary>
    private void Fallback(MentorTicket ticket)
    {
        var question = string.Join("\n", ticket.Lines
            .Where(l => l.Role == MentorSenderRole.Player)
            .Select(l => l.Text));

        _players.TryGetSessionById(ticket.Player, out var session);
        _bwoink.SendFromMentorHelp(ticket.Player, ticket.PlayerName, session?.Channel, question, AHelpCategory.Gameplay);
        Close(ticket, MentorCloseReason.Fallback, null);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        foreach (var ticket in _tickets.Values.ToList())
        {
            Close(ticket, MentorCloseReason.RoundEnd, null);
            Drop(ticket);
        }

        _lastOpened.Clear();
    }

    private bool CanReopen(MentorTicket ticket)
    {
        return ticket.CloseReason is MentorCloseReason.Resolved or MentorCloseReason.Player or MentorCloseReason.Idle
               && _timing.RealTime - ticket.ClosedTime < ReopenWindow;
    }

    /// <summary>Forgets a closed ticket and takes it out of every window.</summary>
    private void Drop(MentorTicket ticket)
    {
        _tickets.Remove(ticket.Id);
        foreach (var user in ticket.SentTo)
        {
            if (_players.TryGetSessionById(user, out var session) && session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(new WGMentorHelpTicketRemovedEvent { Id = ticket.Id }, session);
        }

        if (_players.TryGetSessionById(ticket.Player, out var asker)
            && asker.Status == SessionStatus.InGame
            && GetLatestTicket(ticket.Player) == null)
        {
            RaiseNetworkEvent(new WGMentorHelpPlayerStateEvent(), asker);
        }
    }

    /// <summary>Forgets a player's closed tickets before they open a new one.</summary>
    private void DropClosed(NetUserId player)
    {
        foreach (var ticket in _tickets.Values.Where(t => t.Player == player && t.Closed).ToList())
        {
            Drop(ticket);
        }
    }

    #endregion

    #region Sending

    /// <summary>Sends a ticket to its asker and to every responder who may see it; takes it from those who no longer may.</summary>
    private void Publish(MentorTicket ticket, bool alert = false)
    {
        if (_players.TryGetSessionById(ticket.Player, out var asker) && asker.Status == SessionStatus.InGame)
            RaiseNetworkEvent(new WGMentorHelpPlayerStateEvent { Ticket = PlayerView(ticket) }, asker);

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            if (CanSee(session, ticket))
            {
                ticket.SentTo.Add(session.UserId);
                RaiseNetworkEvent(new WGMentorHelpTicketEvent { Ticket = ResponderView(ticket, session), Alert = alert }, session);
            }
            else if (ticket.SentTo.Remove(session.UserId))
            {
                RaiseNetworkEvent(new WGMentorHelpTicketRemovedEvent { Id = ticket.Id }, session);
            }
        }
    }

    /// <summary>Sends a player everything they may see: their own ticket and, as a responder, the queue.</summary>
    public void SendState(ICommonSession session)
    {
        if (session.Status != SessionStatus.InGame)
            return;

        var own = GetLatestTicket(session.UserId);
        RaiseNetworkEvent(new WGMentorHelpPlayerStateEvent { Ticket = own == null ? null : PlayerView(own) }, session);

        var snapshot = new WGMentorHelpSnapshotEvent
        {
            Responder = IsResponder(session),
            Staff = IsStaff(session),
        };

        foreach (var ticket in _tickets.Values.OrderBy(t => t.Id))
        {
            if (CanSee(session, ticket))
            {
                ticket.SentTo.Add(session.UserId);
                snapshot.Tickets.Add(ResponderView(ticket, session));
            }
            else
            {
                ticket.SentTo.Remove(session.UserId);
            }
        }

        RaiseNetworkEvent(snapshot, session);
    }

    /// <summary>The asker's view: responders are only "Mentor", notes for responders are left out.</summary>
    public MentorHelpPlayerTicket PlayerView(MentorTicket ticket)
    {
        var view = new MentorHelpPlayerTicket
        {
            Id = ticket.Id,
            Status = ticket.Status,
            CloseReason = ticket.CloseReason,
            Answered = ticket.Answered,
            Rated = ticket.Rating != null,
            Reported = ticket.Reported,
            ResponderTyping = ticket.Typing.Keys.Any(u => u != ticket.Player),
            ReopenUntil = ticket.Closed && CanReopen(ticket) ? ticket.ClosedAt + ReopenWindow : null,
        };

        foreach (var line in ticket.Lines.Where(l => !l.RespondersOnly))
        {
            var own = line.Sender == ticket.Player;
            view.Lines.Add(new MentorHelpLine
            {
                Role = line.Role,
                Sender = line.Role switch
                {
                    MentorSenderRole.System => string.Empty,
                    _ when own => Loc.GetString("wg-mhelp-sender-you"),
                    _ => Loc.GetString("wg-mhelp-sender-mentor"),
                },
                Text = line.Text,
                SentAt = line.SentAt,
                Own = own,
            });
        }

        return view;
    }

    /// <summary>A responder's view: the asker by character name (and username for staff), responders by username.</summary>
    public MentorHelpTicketView ResponderView(MentorTicket ticket, ICommonSession receiver)
    {
        var staff = IsStaff(receiver);
        _players.TryGetSessionById(ticket.Player, out var asker);

        var view = new MentorHelpTicketView
        {
            Id = ticket.Id,
            Status = ticket.Status,
            CloseReason = ticket.CloseReason,
            Asker = ticket.CharacterName,
            AskerUsername = staff ? ticket.PlayerName : null,
            AskerId = staff ? ticket.Player : null,
            AskerInLobby = asker == null || !InRound(asker),
            Topic = ticket.Topic,
            Secret = ticket.Secret,
            Newbie = ticket.Newbie,
            FromAhelp = ticket.FromAhelp,
            Crew = asker != null && SameGrid(asker, receiver),
            OpenedAt = ticket.OpenedAt,
            ClaimedBy = ticket.ClaimerName,
            ClaimedByMe = ticket.Claimer == receiver.UserId,
        };

        foreach (var user in ticket.Typing.Keys)
        {
            if (user == receiver.UserId)
                continue;

            if (user == ticket.Player)
                view.Typing.Add(ticket.CharacterName);
            else if (_players.TryGetSessionById(user, out var typer))
                view.Typing.Add(typer.Name);
        }

        foreach (var line in ticket.Lines)
        {
            view.Lines.Add(new MentorHelpLine
            {
                Role = line.Role,
                Sender = line.Role switch
                {
                    MentorSenderRole.System => string.Empty,
                    MentorSenderRole.Player => ticket.CharacterName,
                    _ => line.SenderName,
                },
                Text = line.Text,
                SentAt = line.SentAt,
                Own = line.Sender == receiver.UserId,
            });
        }

        return view;
    }

    private void Notice(NetUserId user, string text)
    {
        if (_players.TryGetSessionById(user, out var session) && session.Status == SessionStatus.InGame)
            RaiseNetworkEvent(new WGMentorHelpNoticeEvent { Text = text }, session);
    }

    #endregion

    #region Database

    private void Insert(MentorTicket ticket)
    {
        var row = new WolfgateMentorTicket
        {
            RoundId = _ticker.RoundId,
            PlayerUserId = ticket.Player.UserId,
            CharacterName = ticket.CharacterName,
            Topic = ticket.Topic,
            SecretRole = ticket.Secret,
            Newbie = ticket.Newbie,
            FromAhelp = ticket.FromAhelp,
            OpenedAt = ticket.OpenedAt,
        };

        ticket.Writes = InsertAsync(ticket, row);
    }

    /// <summary>
    /// Writes the ticket's row, retrying a failed insert until the database takes it: quickly at first, then once a
    /// minute. The writes queued behind it wait, so none is lost. Before each retry it looks for a row an uncertain
    /// earlier attempt may have committed, so a ticket is never stored twice.
    /// </summary>
    private async Task InsertAsync(MentorTicket ticket, WolfgateMentorTicket row)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    var existing = await _db.GetMentorTicketsAsync(row.PlayerUserId, null, row.OpenedAt.AddSeconds(-1), 10);
                    if (existing.FirstOrDefault(t => Math.Abs((t.OpenedAt - row.OpenedAt).TotalMilliseconds) < 1) is { } found)
                    {
                        ticket.DbId = found.Id;
                        return;
                    }
                }

                row.Id = 0;
                ticket.DbId = await _db.AddMentorTicketAsync(row);
                return;
            }
            catch (Exception e)
            {
                // Log each failure in the quick phase, then only every tenth slow retry.
                if (attempt <= InsertAttempts || attempt % 10 == 0)
                    Log.Error($"Could not store mentor ticket #{ticket.Id} (attempt {attempt}): {e}");
            }

            await Task.Delay(attempt < InsertAttempts ? InsertRetryDelay : InsertSlowRetryDelay);
        }
    }

    /// <summary>Queues a write that needs the ticket's row id, after the writes before it.</summary>
    private void Write(MentorTicket ticket, Func<int, Task> write)
    {
        ticket.Writes = Chain(ticket, ticket.Writes, write);
    }

    private async Task Chain(MentorTicket ticket, Task before, Func<int, Task> write)
    {
        await before;
        if (ticket.DbId is not { } id)
            return;

        try
        {
            await write(id);
        }
        catch (Exception e)
        {
            Log.Error($"Could not write mentor ticket #{ticket.Id}: {e}");
        }
    }

    /// <summary>Checks the credited mentor for promotion once the ticket's state is in the database.</summary>
    private async Task PromoteAfterWrites(MentorTicket ticket, NetUserId mentor)
    {
        try
        {
            await ticket.Writes;
        }
        catch (Exception)
        {
            // A failed write is logged where it happens; the count just misses this ticket.
        }

        await _mentors.CheckPromotionAsync(mentor);
    }

    private void Update(MentorTicket ticket)
    {
        var row = new WolfgateMentorTicket
        {
            CharacterName = ticket.CharacterName,
            Topic = ticket.Topic,
            SecretRole = ticket.Secret,
            Newbie = ticket.Newbie,
            FromAhelp = ticket.FromAhelp,
            FirstAnswerAt = ticket.FirstAnswerAt,
            ClaimedBy = ticket.Claimer?.UserId,
            ClosedAt = ticket.ClosedAt,
            CloseReason = ticket.CloseReason is { } reason ? ReasonToDb(reason) : null,
            Rating = ticket.Rating,
            RatingComment = ticket.RatingComment,
        };

        Write(ticket, id =>
        {
            row.Id = id;
            return _db.UpdateMentorTicketAsync(row);
        });
    }

    private void AddLine(MentorTicket ticket, MentorSenderRole role, ICommonSession? sender, string text, bool respondersOnly = false)
    {
        if (sender != null)
            ticket.Typing.Remove(sender.UserId);

        var line = new MentorTicketLine
        {
            Role = role,
            Sender = sender?.UserId,
            SenderName = sender?.Name ?? string.Empty,
            Text = text,
            SentAt = DateTime.UtcNow,
            RespondersOnly = respondersOnly,
        };

        ticket.Lines.Add(line);
        if (!respondersOnly)
            ticket.LastMessageTime = _timing.RealTime;

        _discord.Line(ticket, line);

        Write(ticket, id => _db.AddMentorTicketMessageAsync(new WolfgateMentorTicketMessage
        {
            TicketId = id,
            SenderUserId = line.Sender?.UserId,
            SenderRole = RoleToDb(role),
            Text = line.Text,
            SentAt = line.SentAt,
        }));
    }

    public static string ReasonToDb(MentorCloseReason reason)
    {
        return reason switch
        {
            MentorCloseReason.Resolved => "resolved",
            MentorCloseReason.Player => "player",
            MentorCloseReason.Idle => "idle",
            MentorCloseReason.RoundEnd => "round_end",
            MentorCloseReason.Escalated => "escalated",
            _ => "fallback",
        };
    }

    private static string RoleToDb(MentorSenderRole role)
    {
        return role switch
        {
            MentorSenderRole.Mentor => "mentor",
            MentorSenderRole.Staff => "staff",
            MentorSenderRole.System => "system",
            _ => "player",
        };
    }

    #endregion

    #region Helpers

    private void SetClaimer(MentorTicket ticket, NetUserId? user, string? name)
    {
        ticket.Claimer = user;
        ticket.ClaimerName = name;
        Update(ticket);
    }

    /// <summary>Strips markup and trims; refuses empty or over-long text.</summary>
    private static bool Clean(string text, int max, out string clean, out MentorHelpOpenResult refusal)
    {
        clean = FormattedMessage.RemoveMarkupPermissive(text).Trim();
        refusal = MentorHelpOpenResult.Opened;

        if (clean.Length == 0)
        {
            refusal = MentorHelpOpenResult.Empty;
            return false;
        }

        if (clean.Length > max)
        {
            refusal = MentorHelpOpenResult.TooLong;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The name mentors see: the character the player is playing (a ghost keeps it), or the one selected in the lobby.
    /// </summary>
    private string CharacterName(ICommonSession session)
    {
        if (_mind.TryGetMind(session, out _, out var mind) && !string.IsNullOrWhiteSpace(mind.CharacterName))
            return mind.CharacterName;

        // Never the username: preferences can still be loading just after a player connects.
        var prefs = _prefs.GetPreferencesOrNull(session.UserId);
        return prefs?.SelectedCharacter.Name is { Length: > 0 } name ? name : Loc.GetString("wg-mhelp-asker-unknown");
    }

    private bool SameGrid(ICommonSession a, ICommonSession b)
    {
        if (!InRound(a) || !InRound(b))
            return false;

        var gridA = Transform(a.AttachedEntity!.Value).GridUid;
        return gridA != null && gridA == Transform(b.AttachedEntity!.Value).GridUid;
    }

    #endregion

    #region Events

    private void OnOpenRequest(WGMentorHelpOpenRequest ev, EntitySessionEventArgs args)
    {
        var result = Open(args.SenderSession, ev.Text, ev.Topic, ev.Secret);
        var refusal = result switch
        {
            MentorHelpOpenResult.AlreadyOpen => Loc.GetString("wg-mhelp-one-open"),
            MentorHelpOpenResult.TooLong => Loc.GetString("wg-mhelp-too-long", ("max", MaxLength)),
            MentorHelpOpenResult.Muted => Loc.GetString("wg-mhelp-muted"),
            MentorHelpOpenResult.Cooldown => Loc.GetString("wg-mhelp-cooldown",
                ("seconds", _cfg.GetCVar(MentorHelpCVars.TicketCooldown))),
            _ => null,
        };

        if (refusal != null)
            Notice(args.SenderSession.UserId, refusal);
    }

    private void OnPostRequest(WGMentorHelpPostRequest ev, EntitySessionEventArgs args)
    {
        if (ev.TicketId is { } id)
            PostAsResponder(args.SenderSession, id, ev.Text);
        else if (!PostAsPlayer(args.SenderSession, ev.Text) && ev.Text.Trim().Length > MaxLength)
            Notice(args.SenderSession.UserId, Loc.GetString("wg-mhelp-too-long", ("max", MaxLength)));
    }

    private void OnActionRequest(WGMentorHelpActionRequest ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        switch (ev.Action)
        {
            case MentorHelpAction.Claim:
                Claim(session, ev.TicketId);
                break;
            case MentorHelpAction.Unclaim:
                Unclaim(session, ev.TicketId);
                break;
            case MentorHelpAction.TakeOver:
                TakeOver(session, ev.TicketId, ev.Note);
                break;
            case MentorHelpAction.Transfer when ev.Target != null:
                Transfer(session, ev.TicketId, ev.Target);
                break;
            case MentorHelpAction.Resolve:
                Resolve(session, ev.TicketId);
                break;
            case MentorHelpAction.Escalate:
                Escalate(session, ev.TicketId);
                break;
        }
    }

    private void OnFromAHelpRequest(WGMentorHelpFromAHelpRequest ev, EntitySessionEventArgs args)
    {
        OpenFromAHelp(args.SenderSession, ev.Player);
    }

    private void OnTypingRequest(WGMentorHelpTypingRequest ev, EntitySessionEventArgs args)
    {
        SetTyping(args.SenderSession, ev.TicketId, ev.Typing);
    }

    /// <summary>Marks a player as typing in a ticket, or not; null for their own. Lasts a few seconds without renewal.</summary>
    public void SetTyping(ICommonSession session, int? ticketId, bool typing)
    {
        var ticket = ticketId is { } id
            ? _tickets.GetValueOrDefault(id) is { } t && CanSee(session, t) ? t : null
            : GetOpenTicket(session.UserId);

        if (ticket == null || ticket.Closed)
            return;

        bool changed;
        if (typing)
        {
            changed = !ticket.Typing.ContainsKey(session.UserId);
            ticket.Typing[session.UserId] = _timing.RealTime + TypingTimeout;
        }
        else
        {
            changed = ticket.Typing.Remove(session.UserId);
        }

        if (changed)
            Publish(ticket);
    }

    private void OnPlayerActionRequest(WGMentorHelpPlayerActionRequest ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        switch (ev.Action)
        {
            case MentorHelpPlayerAction.End:
                End(session);
                break;
            case MentorHelpPlayerAction.Reopen:
                Reopen(session);
                break;
            case MentorHelpPlayerAction.Rate:
                Rate(session, ev.Up, ev.Comment);
                break;
            case MentorHelpPlayerAction.Report:
                Report(session);
                break;
        }
    }

    private void OnRefreshRequest(WGMentorHelpRefreshRequest ev, EntitySessionEventArgs args)
    {
        SendState(args.SenderSession);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame)
            SendState(args.Session);
        else if (args.NewStatus == SessionStatus.Disconnected)
            ReleaseClaims(args.Session.UserId, null);
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        ReleaseClaims(args.Player.UserId, args.Player);
        SendState(args.Player);
    }

    private void OnMentorStatusChanged(ICommonSession session)
    {
        ReleaseClaims(session.UserId, session);
        SendState(session);
    }

    /// <summary>Entering or leaving a body changes which spoiler-safe tickets a mentor may see.</summary>
    private void OnAttachChanged(ICommonSession session)
    {
        if (IsResponder(session))
            SendState(session);
    }

    /// <summary>
    /// Lets go of the tickets a player holds once they can no longer answer them (off duty, revoked, disconnected,
    /// lost the flag), so unanswered ones go back to the queue and the staff fallback.
    /// </summary>
    private void ReleaseClaims(NetUserId user, ICommonSession? session)
    {
        foreach (var ticket in _tickets.Values)
        {
            if (ticket.Closed || ticket.Claimer != user || session != null && CanSee(session, ticket))
                continue;

            SetClaimer(ticket, null, null);
            _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"Mentor ticket #{ticket.Id} released: its claimer can no longer answer");
            Publish(ticket);
        }
    }

    #endregion
}
