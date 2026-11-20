using System.Threading.Tasks;
using Content.Shared._WG.MentorHelp;
using Robust.Shared.Network;

namespace Content.Server._WG.MentorHelp;

/// <summary>A Mentor Help ticket held in memory for the round; mirrored to the database as it changes.</summary>
public sealed class MentorTicket
{
    /// <summary>The round-local number shown in windows and logs.</summary>
    public int Id;

    /// <summary>The row id in <c>wolfgate_mentor_ticket</c>, once written.</summary>
    public int? DbId;

    /// <summary>The database writes for this ticket, run one after another.</summary>
    public Task Writes = Task.CompletedTask;

    public NetUserId Player;
    public string PlayerName = string.Empty;
    public string CharacterName = string.Empty;
    public string? Topic;
    public bool Secret;
    public bool Newbie;
    public bool FromAhelp;

    public DateTime OpenedAt;
    public TimeSpan OpenedTime;
    public TimeSpan LastMessageTime;
    public DateTime? FirstAnswerAt;

    public NetUserId? Claimer;
    public string? ClaimerName;

    public bool Closed;
    public MentorCloseReason? CloseReason;
    public TimeSpan ClosedTime;
    public DateTime? ClosedAt;

    /// <summary>Whether a mentor or staff member posted in it.</summary>
    public bool Answered;

    public int? Rating;
    public string? RatingComment;

    /// <summary>The asker has reported it to staff.</summary>
    public bool Reported;

    /// <summary>Who is typing in it, until when.</summary>
    public readonly Dictionary<NetUserId, TimeSpan> Typing = new();

    public readonly List<MentorTicketLine> Lines = new();

    /// <summary>Responders who were last sent this ticket, so they can be told when they may no longer see it.</summary>
    public readonly HashSet<NetUserId> SentTo = new();

    public MentorTicketStatus Status => Closed
        ? MentorTicketStatus.Closed
        : Claimer == null ? MentorTicketStatus.Waiting : MentorTicketStatus.Claimed;
}

/// <summary>One line of a ticket as stored, with the sender's real identity.</summary>
public sealed class MentorTicketLine
{
    public MentorSenderRole Role;

    /// <summary>Null for system lines.</summary>
    public NetUserId? Sender;

    /// <summary>The sender's username; the asker's character name is on the ticket.</summary>
    public string SenderName = string.Empty;

    public string Text = string.Empty;
    public DateTime SentAt;

    /// <summary>Seen by responders only, never by the asker (take-over notes).</summary>
    public bool RespondersOnly;
}
