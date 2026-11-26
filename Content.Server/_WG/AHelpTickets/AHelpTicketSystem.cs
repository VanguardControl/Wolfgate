using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.Bank;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Administration.Systems;
using Content.Server.Database;
using Content.Server.Discord.DiscordLink;
using Content.Server.GameTicking;
using Content.Shared._WG.AHelpTickets;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.AHelpTickets;

/// <summary>Who wrote a line of an ahelp.</summary>
public enum AHelpLineSource : byte
{
    Player,
    Staff,
    Discord,
    System,
}

/// <summary>
/// Turns ahelp conversations into numbered tickets: opens one on a player's first message, claims it on the first
/// staff reply, closes it on Resolve, Reject or IC issue (or at round end), reminds staff of unclaimed ones and keeps
/// every ticket and line in the database.
/// </summary>
public sealed class AHelpTicketSystem : EntitySystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DiscordChatLink _discord = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private BwoinkSystem _bwoink = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMindSystem _minds = default!;

    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private const int SearchLimit = 50;
    private const int PlayerTextLimit = 50;

    /// <summary>This round's tickets by key, plus closed ones still waiting to be written.</summary>
    private readonly Dictionary<int, Ticket> _tickets = new();

    /// <summary>Each player's open ticket.</summary>
    private readonly Dictionary<NetUserId, Ticket> _open = new();

    private int _nextKey = 1;
    private TimeSpan _nextFlush;
    private Task? _flushTask;

    /// <summary>The ticket the last ahelp line went to, for tying a macro to its reply.</summary>
    private Ticket? _lastLineTicket;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<AHelpTicketActionMessage>(OnAction);
        SubscribeNetworkEvent<AHelpTicketMacroUsedMessage>(OnMacroUsed);
        SubscribeNetworkEvent<AHelpTicketMacroReplyMessage>(OnMacroReply);
        SubscribeNetworkEvent<AHelpTicketPlayerCardRequest>(OnPlayerCard);
        SubscribeNetworkEvent<AHelpTicketTranscriptRequest>(OnTranscript);
        SubscribeNetworkEvent<AHelpTicketSearchRequest>(OnSearch);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        _admins.OnPermsChanged += OnPermsChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        _admins.OnPermsChanged -= OnPermsChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.RealTime < _nextFlush)
            return;

        _nextFlush = _timing.RealTime + FlushInterval;
        RemindStale();
        Flush();
    }

    /// <summary>The player's open ticket, if any.</summary>
    public AHelpTicketInfo? GetOpenTicket(NetUserId player)
    {
        return _open.TryGetValue(player, out var ticket) ? ticket.Info : null;
    }

    /// <summary>Every ticket of this round.</summary>
    public IEnumerable<AHelpTicketInfo> Tickets => _tickets.Values.Where(t => !t.Retired).Select(t => t.Info);

    /// <summary>A ticket of this round by key, with what the player wrote in it and when it closed.</summary>
    public bool TryGetTicket(int key, [NotNullWhen(true)] out AHelpTicketInfo? info, out IReadOnlyList<string> playerText, out DateTime? closed)
    {
        if (_tickets.TryGetValue(key, out var ticket) && !ticket.Retired)
        {
            info = ticket.Info;
            playerText = ticket.PlayerText;
            closed = ticket.Row.ClosedAt;
            return true;
        }

        info = null;
        playerText = Array.Empty<string>();
        closed = null;
        return false;
    }

    /// <summary>Takes a line of an ahelp, opening, claiming or extending the player's ticket.</summary>
    public void OnLine(NetUserId player, AHelpLineSource source, NetUserId sender, string senderName, string text, bool adminOnly)
    {
        var now = DateTime.UtcNow;
        _open.TryGetValue(player, out var ticket);

        if (ticket == null)
        {
            switch (source)
            {
                // Connects, disconnects and bans only matter to a ticket that is open.
                case AHelpLineSource.System:
                    return;
                // A staff note with nothing open belongs with the player's latest ticket this round.
                case AHelpLineSource.Staff or AHelpLineSource.Discord when adminOnly:
                    ticket = _tickets.Values.Where(t => !t.Retired && t.Info.Player == player).MaxBy(t => t.Info.Key);
                    break;
            }

            ticket ??= Open(player, now);
        }

        var info = ticket.Info;
        info.LastActivity = now;
        info.Messages++;
        info.Online = IsOnline(player);

        switch (source)
        {
            case AHelpLineSource.Player:
                info.LastPlayerMessage = now;
                ticket.LastReminder = now;
                if (ticket.PlayerText.Count < PlayerTextLimit)
                    ticket.PlayerText.Add(text);
                break;
            case AHelpLineSource.Staff or AHelpLineSource.Discord:
                if (adminOnly)
                {
                    info.HasNotes = true;
                    break;
                }

                info.FirstReply ??= now;
                ticket.Row.FirstReplyAt ??= now;
                if (info.State == AHelpTicketState.Open)
                    Claim(ticket, source == AHelpLineSource.Staff ? sender : null, senderName, now);
                break;
        }

        ticket.Pending.Add(new WolfgateAhelpMessage
        {
            SentAt = now,
            AuthorUserId = source is AHelpLineSource.Player or AHelpLineSource.Staff ? sender.UserId : null,
            AuthorName = Trim(senderName, 64),
            Source = source.ToString().ToLowerInvariant(),
            AdminOnly = adminOnly,
            Text = text,
        });

        ticket.RowDirty = true;
        _lastLineTicket = ticket;
        SendUpdate(ticket);
    }

    /// <summary>A staffer acts on a ticket by its key.</summary>
    public void StaffAction(ICommonSession staff, int key, AHelpTicketAction action)
    {
        if (!_tickets.TryGetValue(key, out var ticket) || ticket.Retired || !_bwoink.WGCanAnswer(staff, ticket.Info.Player))
            return;

        Act(staff, ticket, action);
    }

    /// <summary>A staffer acts on the player's open ticket; closing with none open still tells everyone.</summary>
    public void StaffActionOnPlayer(ICommonSession staff, NetUserId player, AHelpTicketAction action)
    {
        if (_open.TryGetValue(player, out var ticket))
        {
            Act(staff, ticket, action);
            return;
        }

        if (action is AHelpTicketAction.Resolve or AHelpTicketAction.Reject or AHelpTicketAction.IcIssue)
            _bwoink.WGAnnounceClosed(player, staff.Name, ToState(action), 0);
    }

    private void Act(ICommonSession staff, Ticket ticket, AHelpTicketAction action)
    {
        var now = DateTime.UtcNow;
        switch (action)
        {
            case AHelpTicketAction.Claim:
                if (ticket.Info.State.IsClosed())
                    return;
                Claim(ticket, staff.UserId, staff.Name, now);
                break;
            case AHelpTicketAction.Unclaim:
                if (ticket.Info.State != AHelpTicketState.Claimed)
                    return;
                ticket.Info.State = AHelpTicketState.Open;
                ticket.Info.Assignee = null;
                ticket.Info.AssigneeName = null;
                ticket.LastReminder = now;
                _bwoink.WGSetClaimed(ticket.Info.Player, false);
                break;
            case AHelpTicketAction.Reopen:
                if (!ticket.Info.State.IsClosed() || _open.ContainsKey(ticket.Info.Player))
                    return;
                ticket.Info.State = AHelpTicketState.Open;
                ticket.Row.ClosedAt = null;
                ticket.Row.ClosedBy = null;
                ticket.LastReminder = now;
                _open[ticket.Info.Player] = ticket;
                Claim(ticket, staff.UserId, staff.Name, now);
                break;
            default:
                if (ticket.Info.State.IsClosed())
                    return;
                Close(ticket, ToState(action), staff.Name, now);
                _bwoink.WGAnnounceClosed(ticket.Info.Player, staff.Name, ticket.Info.State, ticket.Info.Number);
                break;
        }

        ticket.RowDirty = true;
        SendUpdate(ticket);
    }

    private static AHelpTicketState ToState(AHelpTicketAction action)
    {
        return action switch
        {
            AHelpTicketAction.Reject => AHelpTicketState.Rejected,
            AHelpTicketAction.IcIssue => AHelpTicketState.IcIssue,
            _ => AHelpTicketState.Resolved,
        };
    }

    private Ticket Open(NetUserId player, DateTime now)
    {
        var username = _players.TryGetSessionById(player, out var session) ? session.Name : player.ToString();
        var character = _minds.GetCharacterName(player) ?? string.Empty;
        var category = _bwoink.GetAHelpCategory(player);

        var ticket = new Ticket
        {
            Info = new AHelpTicketInfo
            {
                Key = _nextKey++,
                RoundId = _ticker.RoundId,
                Player = player,
                Username = username,
                Character = character,
                Category = category,
                State = AHelpTicketState.Open,
                Opened = now,
                LastActivity = now,
                LastPlayerMessage = now,
                Online = session != null,
            },
            Row = new WolfgateAhelpTicket
            {
                RoundId = _ticker.RoundId,
                ServerName = Trim(_cfg.GetCVar(CVars.GameHostName), 128),
                PlayerUserId = player.UserId,
                PlayerName = Trim(username, 64),
                CharacterName = character.Length == 0 ? null : Trim(character, 128),
                Category = category.ToString(),
                State = nameof(AHelpTicketState.Open),
                OpenedAt = now,
            },
            LastReminder = now,
        };

        _tickets[ticket.Info.Key] = ticket;
        _open[player] = ticket;
        return ticket;
    }

    private void Claim(Ticket ticket, NetUserId? staff, string staffName, DateTime now)
    {
        ticket.Info.State = AHelpTicketState.Claimed;
        ticket.Info.Assignee = staff;
        ticket.Info.AssigneeName = staffName;
        ticket.Row.ClaimedAt ??= now;
        _bwoink.WGSetClaimed(ticket.Info.Player, true);
        SendPlayerStatus(ticket);
    }

    private void Close(Ticket ticket, AHelpTicketState state, string staffName, DateTime now)
    {
        ticket.Info.State = state;
        ticket.Row.ClosedAt = now;
        ticket.Row.ClosedBy = Trim(staffName, 64);
        if (_open.TryGetValue(ticket.Info.Player, out var open) && open == ticket)
            _open.Remove(ticket.Info.Player);

        SendPlayerStatus(ticket);
        _discord.WGCloseAhelpThread(ticket.Info.Player, ticket.Info.Number, state);
    }

    private void OnAction(AHelpTicketActionMessage msg, EntitySessionEventArgs args)
    {
        StaffAction(args.SenderSession, msg.Key, msg.Action);
    }

    private void OnMacroUsed(AHelpTicketMacroUsedMessage msg, EntitySessionEventArgs args)
    {
        if (!_tickets.TryGetValue(msg.Key, out var ticket) || !_bwoink.WGCanAnswer(args.SenderSession, ticket.Info.Player))
            return;

        RecordMacro(ticket, msg.Macro);
    }

    /// <summary>Sends a macro's reply as the staffer, then records the macro and applies its action on the ticket that took it.</summary>
    private void OnMacroReply(AHelpTicketMacroReplyMessage msg, EntitySessionEventArgs args)
    {
        var staff = args.SenderSession;
        if (!_bwoink.WGCanAnswer(staff, msg.Player))
            return;

        _lastLineTicket = null;
        _bwoink.WGSendAsStaff(staff, new BwoinkSystem.BwoinkTextMessage(msg.Player, staff.UserId, msg.Text,
            playSound: msg.PlaySound, adminOnly: msg.AdminOnly));

        // Set by OnLine for this very line; anything else means the reply was refused (rate limit, permissions).
        if (_lastLineTicket is not { } ticket || ticket.Info.Player != msg.Player)
            return;

        _lastLineTicket = null;
        RecordMacro(ticket, msg.Macro);
        if (msg.Action is { } action)
            Act(staff, ticket, action);
    }

    private static void RecordMacro(Ticket ticket, string macro)
    {
        var macros = ticket.Row.Macros is { Length: > 0 } existing ? $"{existing},{macro}" : macro;
        if (macros.Length > 512)
            return;

        ticket.Row.Macros = macros;
        ticket.RowDirty = true;
    }

    private async void OnPlayerCard(AHelpTicketPlayerCardRequest msg, EntitySessionEventArgs args)
    {
        var staff = args.SenderSession;
        if (!_bwoink.WGCanAnswer(staff, msg.Player))
            return;

        var card = new AHelpTicketPlayerCardMessage { Player = msg.Player };
        if (_players.TryGetSessionById(msg.Player, out var session) && _bank.TryGetBalance(session, out var balance))
            card.Bank = balance;

        try
        {
            var notes = _db.GetAllAdminRemarks(msg.Player.UserId);
            var bans = _db.GetServerBansAsync(null, msg.Player, null, null);
            var tickets = _db.GetAhelpTicketsForPlayerAsync(msg.Player.UserId, _cfg.GetCVar(AHelpTicketCVars.HistoryLimit));
            await Task.WhenAll(notes, bans, tickets);

            card.Notes = (await notes).Count;
            card.Bans = (await bans).Count;
            (card.TotalTickets, card.Previous) = await tickets;
        }
        catch (Exception e)
        {
            Log.Error($"Ahelp player card for {msg.Player} failed: {e}");
        }

        if (staff.Status == SessionStatus.InGame)
            RaiseNetworkEvent(card, staff);
    }

    private async void OnTranscript(AHelpTicketTranscriptRequest msg, EntitySessionEventArgs args)
    {
        var staff = args.SenderSession;
        if (!_admins.HasAdminFlag(staff, AdminFlags.Adminhelp))
            return;

        try
        {
            var (ticket, lines) = await _db.GetAhelpTicketTranscriptAsync(msg.Number);
            if (staff.Status == SessionStatus.InGame)
                RaiseNetworkEvent(new AHelpTicketTranscriptMessage(ticket, lines), staff);
        }
        catch (Exception e)
        {
            Log.Error($"Ahelp transcript #{msg.Number} failed: {e}");
        }
    }

    private async void OnSearch(AHelpTicketSearchRequest msg, EntitySessionEventArgs args)
    {
        var staff = args.SenderSession;
        if (!_admins.HasAdminFlag(staff, AdminFlags.Adminhelp) || string.IsNullOrWhiteSpace(msg.Text))
            return;

        try
        {
            var results = await _db.SearchAhelpTicketsAsync(Trim(msg.Text, 200), SearchLimit);
            if (staff.Status == SessionStatus.InGame)
                RaiseNetworkEvent(new AHelpTicketSearchResultsMessage(msg.Text, results), staff);
        }
        catch (Exception e)
        {
            Log.Error($"Ahelp search failed: {e}");
        }
    }

    /// <summary>At round end every open ticket closes; it is written out and then forgotten.</summary>
    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        var now = DateTime.UtcNow;
        foreach (var ticket in _tickets.Values)
        {
            if (!ticket.Info.State.IsClosed())
            {
                Close(ticket, AHelpTicketState.ClosedRoundEnd, Loc.GetString("wg-ahelp-ticket-round-end-closer"), now);
                ticket.RowDirty = true;
            }

            ticket.Retired = true;
        }

        _open.Clear();
        foreach (var staff in StaffSessions())
        {
            RaiseNetworkEvent(new AHelpTicketListMessage(new List<AHelpTicketInfo>()), staff);
        }

        Flush();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (_open.TryGetValue(e.Session.UserId, out var ticket))
        {
            ticket.Info.Online = e.NewStatus == SessionStatus.InGame;
            SendUpdate(ticket);
        }

        if (e.NewStatus != SessionStatus.InGame)
            return;

        SendList(e.Session);
        if (_open.TryGetValue(e.Session.UserId, out var own))
            SendPlayerStatus(own);
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player.Status == SessionStatus.InGame)
            SendList(args.Player);
    }

    /// <summary>Staff get a sound and a line for each ticket left unclaimed past the stale time, once per interval.</summary>
    private void RemindStale()
    {
        var minutes = _cfg.GetCVar(AHelpTicketCVars.StaleMinutes);
        if (minutes <= 0)
            return;

        var stale = TimeSpan.FromMinutes(minutes);
        var now = DateTime.UtcNow;
        foreach (var ticket in _open.Values)
        {
            if (ticket.Info.State != AHelpTicketState.Open || now - ticket.LastReminder < stale)
                continue;

            ticket.LastReminder = now;
            var waited = (int) (now - ticket.Info.LastPlayerMessage).TotalMinutes;
            _bwoink.WGStaffLine(ticket.Info.Player,
                Loc.GetString("wg-ahelp-ticket-stale", ("number", ticket.Info.Number), ("minutes", waited)),
                playSound: true);
        }
    }

    private void Flush()
    {
        if (_flushTask is not { IsCompleted: false })
            _ = FlushAsync();
    }

    /// <summary>
    /// Writes new tickets, their lines and changes, then forgets retired tickets that are fully written. A flush
    /// already running is waited for first, so the returned task covers everything queued before the call.
    /// </summary>
    public async Task FlushAsync()
    {
        while (_flushTask is { IsCompleted: false } running)
        {
            await running;
        }

        _flushTask = FlushOnce();
        await _flushTask;
    }

    /// <summary>One pass. Anything the database refuses stays queued (lines) or dirty (rows) for the next pass.</summary>
    private async Task FlushOnce()
    {
        foreach (var ticket in _tickets.Values.ToList())
        {
            if (ticket.Info.Number != 0)
                continue;

            // Cleared before the insert: changes made while it runs stay flagged for the update below.
            ticket.RowDirty = false;
            SyncRow(ticket);
            try
            {
                ticket.Info.Number = await _db.AddAhelpTicketAsync(ticket.Row);
            }
            catch (Exception e)
            {
                Log.Error($"Storing ahelp ticket {ticket.Info.Key} failed: {e}");
                continue;
            }

            if (!ticket.Retired)
            {
                SendUpdate(ticket);
                // A closed ticket getting its number late mustn't replace the status of the player's newer one.
                if (IsCurrentFor(ticket))
                    SendPlayerStatus(ticket);
            }
        }

        var lines = new List<(Ticket Ticket, WolfgateAhelpMessage Line)>();
        var dirty = new List<Ticket>();
        foreach (var ticket in _tickets.Values)
        {
            // Not stored yet; its lines and changes wait for the insert.
            if (ticket.Info.Number == 0)
                continue;

            foreach (var line in ticket.Pending)
            {
                line.TicketId = ticket.Info.Number;
                lines.Add((ticket, line));
            }

            if (ticket.RowDirty)
                dirty.Add(ticket);
        }

        if (lines.Count > 0)
        {
            try
            {
                await _db.AddAhelpMessagesAsync(lines.Select(l => l.Line).ToList());
                foreach (var (ticket, line) in lines)
                {
                    ticket.Pending.Remove(line);
                }
            }
            catch (Exception e)
            {
                Log.Error($"Storing {lines.Count} ahelp lines failed; they are retried: {e}");
            }
        }

        foreach (var ticket in dirty)
        {
            ticket.RowDirty = false;
            SyncRow(ticket);
            try
            {
                await _db.UpdateAhelpTicketAsync(ticket.Row);
            }
            catch (Exception e)
            {
                ticket.RowDirty = true;
                Log.Error($"Updating ahelp ticket #{ticket.Info.Number} failed; it is retried: {e}");
            }
        }

        foreach (var (key, ticket) in _tickets.ToList())
        {
            if (ticket.Retired && ticket.Pending.Count == 0 && !ticket.RowDirty && ticket.Info.Number != 0)
                _tickets.Remove(key);
        }
    }

    /// <summary>Copies what changes on a ticket into its row. The row's Messages stay empty; lines go in on their own.</summary>
    private static void SyncRow(Ticket ticket)
    {
        var info = ticket.Info;
        var row = ticket.Row;
        row.Id = info.Number;
        row.State = info.State.ToString();
        row.AssigneeUserId = info.Assignee?.UserId;
        row.AssigneeName = info.AssigneeName == null ? null : Trim(info.AssigneeName, 64);
        if (info.Character.Length > 0)
            row.CharacterName = Trim(info.Character, 128);
    }

    private void SendUpdate(Ticket ticket)
    {
        if (ticket.Retired)
            return;

        var msg = new AHelpTicketUpdatedMessage(ticket.Info);
        foreach (var staff in _bwoink.WGTargetAdmins(ticket.Info.Category))
        {
            RaiseNetworkEvent(msg, staff);
        }
    }

    private void SendList(ICommonSession session)
    {
        if (_admins.GetAdminData(session) == null)
            return;

        var visible = _tickets.Values
            .Where(t => !t.Retired && _bwoink.WGTargetAdmins(t.Info.Category).Contains(session.Channel))
            .Select(t => t.Info)
            .ToList();
        RaiseNetworkEvent(new AHelpTicketListMessage(visible), session);
    }

    private void SendPlayerStatus(Ticket ticket)
    {
        if (!_players.TryGetSessionById(ticket.Info.Player, out var session) || session.Status != SessionStatus.InGame)
            return;

        RaiseNetworkEvent(new AHelpTicketPlayerStatusMessage(ticket.Info.Number, ticket.Info.State, ticket.Info.AssigneeName), session);
    }

    /// <summary>Whether this is the ticket the player sees: their open one, or their newest when none is open.</summary>
    private bool IsCurrentFor(Ticket ticket)
    {
        var player = ticket.Info.Player;
        if (_open.TryGetValue(player, out var open))
            return open == ticket;

        return _tickets.Values.Where(t => !t.Retired && t.Info.Player == player).MaxBy(t => t.Info.Key) == ticket;
    }

    private IEnumerable<ICommonSession> StaffSessions()
    {
        return _admins.ActiveAdmins.Where(s => _admins.GetAdminData(s) != null);
    }

    private bool IsOnline(NetUserId player)
    {
        return _players.TryGetSessionById(player, out var session) && session.Status == SessionStatus.InGame;
    }

    private static string Trim(string text, int max)
    {
        return text.Length <= max ? text : text[..max];
    }

    /// <summary>A ticket this round: what staff see, its database row and the lines not yet written.</summary>
    private sealed class Ticket
    {
        public AHelpTicketInfo Info = default!;
        public WolfgateAhelpTicket Row = default!;
        public readonly List<WolfgateAhelpMessage> Pending = new();

        /// <summary>What the player wrote, for finding the names they mention.</summary>
        public readonly List<string> PlayerText = new();

        public bool RowDirty;
        public DateTime LastReminder;

        /// <summary>From a round that has ended; kept only until it is written.</summary>
        public bool Retired;
    }
}
