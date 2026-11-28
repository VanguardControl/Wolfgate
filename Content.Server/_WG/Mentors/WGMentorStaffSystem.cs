using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared._WG.Mentors;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.Mentors;

/// <summary>
/// The server side of the staff Mentors tab and of the Seniors' applications list: the roster with its numbers and
/// history, pending applications, stored tickets and their transcripts, and the changes made from there.
/// </summary>
public sealed class WGMentorStaffSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private WGMentorApplicationSystem _applications = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private WGMentorSystem _mentors = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>Most tickets one search returns.</summary>
    private const int TicketLimit = 100;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMentorStaffRequestEvent>((ev, args) => _ = Guard(args.SenderSession, () => SendViewAsync(args.SenderSession, ev.View, ev.Search)));
        SubscribeNetworkEvent<WGMentorStaffActionEvent>((ev, args) => _ = Guard(args.SenderSession, () => ActAsync(args.SenderSession, ev)));
        SubscribeNetworkEvent<WGMentorReviewEvent>((ev, args) => _ = Guard(args.SenderSession, () => ReviewAsync(args.SenderSession, ev)));
        SubscribeNetworkEvent<WGMentorTranscriptRequestEvent>((ev, args) => _ = Guard(args.SenderSession, () => SendTranscriptAsync(args.SenderSession, ev.Id)));
    }

    /// <summary>Staff who see the Mentors tab: the Adminhelp or Moderator flag.</summary>
    public bool IsStaff(ICommonSession session)
    {
        return _admin.HasAdminFlag(session, AdminFlags.Adminhelp) || _admin.HasAdminFlag(session, AdminFlags.Moderator);
    }

    private async Task SendViewAsync(ICommonSession session, MentorStaffView view, string search)
    {
        switch (view)
        {
            case MentorStaffView.Roster when IsStaff(session):
                Send(session, new WGMentorStaffRosterEvent { Rows = await RosterAsync() });
                break;
            case MentorStaffView.Applications when _applications.CanReview(session):
                Send(session, new WGMentorStaffApplicationsEvent { Rows = await ApplicationsAsync() });
                break;
            case MentorStaffView.Tickets when IsStaff(session):
                Send(session, new WGMentorStaffTicketsEvent { Rows = await TicketsAsync(search) });
                break;
        }
    }

    /// <summary>Every roster row, active ones first, with their names and resolved-ticket numbers.</summary>
    public async Task<List<MentorRosterRow>> RosterAsync()
    {
        var rows = new List<MentorRosterRow>();
        foreach (var row in await _db.GetMentorsAsync())
        {
            var user = new NetUserId(row.UserId);
            var (resolved, rated, helpful) = await _db.GetMentorCreditAsync(row.UserId);
            var history = new List<string>();
            foreach (var entry in MentorRows.History(row))
            {
                var by = entry.By is { } id ? await NameAsync(new NetUserId(id)) : "server";
                history.Add($"{entry.At:yyyy-MM-dd HH:mm} {entry.What} ({by}){(entry.Why != null ? $": {entry.Why}" : "")}");
            }

            rows.Add(new MentorRosterRow
            {
                User = user,
                Name = await NameAsync(user),
                Tier = MentorRows.TierFromDb(row.Tier),
                State = row.State,
                Connected = _players.TryGetSessionById(user, out _),
                OnDuty = _mentors.IsOnDuty(user),
                GrantedAt = row.GrantedAt,
                LastActiveAt = row.LastActiveAt,
                Resolved = resolved,
                Rated = rated,
                Helpful = helpful,
                History = history,
                Notes = row.StaffNotes,
            });
        }

        return rows
            .OrderBy(r => MentorRows.OnRoster(r.State) ? 0 : 1)
            .ThenByDescending(r => r.Tier)
            .ThenBy(r => r.Name)
            .ToList();
    }

    /// <summary>Pending applications, oldest first, with their answers and what kept them from approving at once.</summary>
    public async Task<List<MentorApplicationRow>> ApplicationsAsync()
    {
        var rows = new List<MentorApplicationRow>();
        foreach (var application in (await _db.GetMentorApplicationsAsync(WGMentorApplicationSystem.StatePending, null))
                 .OrderBy(a => a.Id))
        {
            Dictionary<string, string> answers;
            try
            {
                answers = JsonSerializer.Deserialize<Dictionary<string, string>>(application.Answers) ?? new();
            }
            catch (JsonException)
            {
                answers = new();
            }

            var record = new List<string>();
            try
            {
                using var summary = JsonDocument.Parse(application.RecordSummary);
                if (summary.RootElement.TryGetProperty("concerns", out var concerns)
                    && concerns.ValueKind == JsonValueKind.Array)
                {
                    record.AddRange(concerns.EnumerateArray().Select(c => c.GetString() ?? ""));
                }
            }
            catch (JsonException)
            {
                // An unreadable summary shows no concerns.
            }

            var user = new NetUserId(application.UserId);
            rows.Add(new MentorApplicationRow
            {
                Id = application.Id,
                User = user,
                Name = await NameAsync(user),
                CreatedAt = application.CreatedAt,
                Hours = application.PlaytimeMinutes / 60,
                Answers = answers,
                Record = record,
            });
        }

        return rows;
    }

    /// <summary>Stored tickets: one by number ("#12" or "12"), those of a player or mentor by name, or the latest.</summary>
    private async Task<List<MentorTicketRow>> TicketsAsync(string search)
    {
        search = search.Trim();
        List<WolfgateMentorTicket> tickets;
        if (int.TryParse(search.TrimStart('#'), out var id))
        {
            tickets = await _db.GetMentorTicketAsync(id) is { } one ? new List<WolfgateMentorTicket> { one } : new();
        }
        else if (search.Length > 0)
        {
            if (await _locator.LookupIdByNameOrIdAsync(search) is not { } located)
                return new List<MentorTicketRow>();

            var asked = await _db.GetMentorTicketsAsync(located.UserId.UserId, null, null, TicketLimit);
            var answered = await _db.GetMentorTicketsAsync(null, located.UserId.UserId, null, TicketLimit);
            tickets = asked.Concat(answered).DistinctBy(t => t.Id).OrderByDescending(t => t.Id).Take(TicketLimit).ToList();
        }
        else
        {
            tickets = await _db.GetMentorTicketsAsync(null, null, null, TicketLimit);
        }

        var rows = new List<MentorTicketRow>();
        foreach (var ticket in tickets)
        {
            rows.Add(new MentorTicketRow
            {
                Id = ticket.Id,
                OpenedAt = ticket.OpenedAt,
                Player = await NameAsync(new NetUserId(ticket.PlayerUserId)),
                Character = ticket.CharacterName,
                Mentor = ticket.ClaimedBy is { } mentor ? await NameAsync(new NetUserId(mentor)) : "",
                CloseReason = ticket.CloseReason ?? "",
                Rating = ticket.Rating,
            });
        }

        return rows;
    }

    private async Task SendTranscriptAsync(ICommonSession session, int id)
    {
        if (!IsStaff(session) || await _db.GetMentorTicketAsync(id) is not { } ticket)
            return;

        var player = await NameAsync(new NetUserId(ticket.PlayerUserId));
        var lines = new List<string>();
        foreach (var message in ticket.Messages)
        {
            var sender = message.SenderRole switch
            {
                "player" => $"{ticket.CharacterName} ({player})",
                "system" => "",
                _ => message.SenderUserId is { } user ? await NameAsync(new NetUserId(user)) : "",
            };

            var text = message.Text.Length == 0 ? Loc.GetString("wg-mentor-tab-transcript-cleared") : message.Text;
            lines.Add(sender.Length == 0 ? $"{message.SentAt:HH:mm} {text}" : $"{message.SentAt:HH:mm} {sender}: {text}");
        }

        Send(session, new WGMentorTranscriptEvent { Id = id, Lines = lines });
    }

    private async Task ActAsync(ICommonSession session, WGMentorStaffActionEvent ev)
    {
        if (!_admin.HasAdminFlag(session, AdminFlags.Moderator))
            return;

        if (await _locator.LookupIdByNameOrIdAsync(ev.Player.Trim()) is not { } located)
        {
            Result(session, Loc.GetString("wg-mentor-cmd-no-player", ("player", ev.Player)));
            return;
        }

        var user = located.UserId;
        var actor = session.UserId;
        var done = ev.Action switch
        {
            MentorStaffAction.Grant => await Grant(user, ev.Tier, actor),
            MentorStaffAction.Revoke => ev.Text.Trim().Length > 0 && await _mentors.RevokeAsync(user, actor, ev.Text.Trim()),
            MentorStaffAction.Tier => await _mentors.SetTierAsync(user, ev.Tier, actor),
            MentorStaffAction.Restore => await _mentors.RestoreAsync(user, actor) != null,
            MentorStaffAction.Notes => await _mentors.SetNotesAsync(user, ev.Text, actor),
            _ => false,
        };

        Result(session, Loc.GetString(done ? "wg-mentor-tab-done" : "wg-mentor-tab-not-done", ("player", located.Username)));
        Send(session, new WGMentorStaffRosterEvent { Rows = await RosterAsync() });
    }

    private async Task<bool> Grant(NetUserId user, MentorTier tier, NetUserId actor)
    {
        await _mentors.GrantAsync(user, tier, actor, MentorRows.SourceStaff);
        return true;
    }

    private async Task ReviewAsync(ICommonSession session, WGMentorReviewEvent ev)
    {
        if (!_applications.CanReview(session))
            return;

        var problem = await _applications.ReviewAsync(ev.Id, ev.Approve, ev.Reason, ev.ReapplyDays, session.UserId);
        Result(session, problem ?? Loc.GetString(ev.Approve ? "wg-mentor-review-approved" : "wg-mentor-review-denied"));
        Send(session, new WGMentorStaffApplicationsEvent { Rows = await ApplicationsAsync() });
    }

    private async Task<string> NameAsync(NetUserId user)
    {
        return (await _locator.LookupIdAsync(user))?.Username ?? user.ToString();
    }

    private void Result(ICommonSession session, string message)
    {
        Send(session, new WGMentorStaffResultEvent { Message = message });
    }

    private void Send(ICommonSession session, EntityEventArgs ev)
    {
        if (session.Status != SessionStatus.Disconnected)
            RaiseNetworkEvent(ev, session);
    }

    private async Task Guard(ICommonSession session, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e)
        {
            Log.Error($"Mentors tab request from {session.Name} failed: {e}");
            Result(session, Loc.GetString("wg-mentor-cmd-failed"));
        }
    }
}
