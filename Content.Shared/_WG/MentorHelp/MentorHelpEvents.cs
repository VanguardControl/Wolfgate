using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.MentorHelp;

/// <summary>Where a ticket stands.</summary>
public enum MentorTicketStatus : byte
{
    Waiting,
    Claimed,
    Closed,
}

/// <summary>Why a ticket closed. Stored in lowercase in <c>wolfgate_mentor_ticket.close_reason</c>.</summary>
public enum MentorCloseReason : byte
{
    Resolved,
    Player,
    Idle,
    RoundEnd,
    Escalated,
    Fallback,
}

/// <summary>Who wrote a line.</summary>
public enum MentorSenderRole : byte
{
    Player,
    Mentor,
    Staff,
    System,
}

/// <summary>What a responder does to a ticket.</summary>
public enum MentorHelpAction : byte
{
    Claim,
    Unclaim,
    TakeOver,
    Transfer,
    Resolve,
    Escalate,
}

/// <summary>What the asker does to their own ticket.</summary>
public enum MentorHelpPlayerAction : byte
{
    End,
    Reopen,
    Rate,
    Report,
}

/// <summary>Topics a player may pick. The id is the Fluent suffix (<c>wg-mhelp-topic-*</c>).</summary>
public static class MentorHelpTopics
{
    public static readonly string[] All = { "ships", "combat", "medical", "trade", "crafting", "interface", "other" };
}

/// <summary>One line of a ticket as one receiver sees it: the sender's name is already what they may see.</summary>
[Serializable, NetSerializable]
public sealed class MentorHelpLine
{
    public MentorSenderRole Role;
    public string Sender = string.Empty;
    public string Text = string.Empty;
    public DateTime SentAt;

    /// <summary>Written by the receiver.</summary>
    public bool Own;
}

/// <summary>The asker's own ticket.</summary>
[Serializable, NetSerializable]
public sealed class MentorHelpPlayerTicket
{
    public int Id;
    public MentorTicketStatus Status;
    public MentorCloseReason? CloseReason;
    public List<MentorHelpLine> Lines = new();

    /// <summary>Until when a closed ticket may be reopened, or null.</summary>
    public DateTime? ReopenUntil;

    /// <summary>Whether a mentor posted in it, so it can be rated.</summary>
    public bool Answered;

    public bool Rated;

    /// <summary>The asker has reported it to staff.</summary>
    public bool Reported;
}

/// <summary>A ticket as a mentor or staff member sees it.</summary>
[Serializable, NetSerializable]
public sealed class MentorHelpTicketView
{
    public int Id;
    public MentorTicketStatus Status;
    public MentorCloseReason? CloseReason;

    /// <summary>The asker's character name.</summary>
    public string Asker = string.Empty;

    /// <summary>The asker's username: sent to staff only.</summary>
    public string? AskerUsername;

    /// <summary>The asker's user id: sent to staff only, for the marker in the ahelp player list.</summary>
    public NetUserId? AskerId;

    public bool AskerInLobby;
    public string? Topic;
    public bool Secret;
    public bool Newbie;
    public bool FromAhelp;

    /// <summary>The receiver's own character is on the asker's grid.</summary>
    public bool Crew;

    public DateTime OpenedAt;
    public string? ClaimedBy;
    public bool ClaimedByMe;
    public List<MentorHelpLine> Lines = new();
}

/// <summary>Server to the asker: their ticket, or null when they have none.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpPlayerStateEvent : EntityEventArgs
{
    public MentorHelpPlayerTicket? Ticket;
}

/// <summary>Server to a responder: every ticket they may see, replacing what they had.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpSnapshotEvent : EntityEventArgs
{
    /// <summary>Whether the receiver answers tickets (on-duty mentor or staff).</summary>
    public bool Responder;

    /// <summary>Whether the receiver sees tickets as staff.</summary>
    public bool Staff;

    public List<MentorHelpTicketView> Tickets = new();
}

/// <summary>Server to a responder: one ticket added or changed.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpTicketEvent : EntityEventArgs
{
    public MentorHelpTicketView Ticket = new();

    /// <summary>A new ticket or a new message, worth a sound.</summary>
    public bool Alert;
}

/// <summary>Server to a responder: a ticket they may no longer see.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpTicketRemovedEvent : EntityEventArgs
{
    public int Id;
}

/// <summary>Server to one client: a short notice for the Mentor Help window (a refusal, a hand-off).</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpNoticeEvent : EntityEventArgs
{
    public string Text = string.Empty;
}

/// <summary>Client to server: open a ticket.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpOpenRequest : EntityEventArgs
{
    public string Text = string.Empty;
    public string? Topic;
    public bool Secret;
}

/// <summary>Client to server: post in a ticket; null for the sender's own.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpPostRequest : EntityEventArgs
{
    public int? TicketId;
    public string Text = string.Empty;
}

/// <summary>Client to server: a responder's action on a ticket.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpActionRequest : EntityEventArgs
{
    public int TicketId;
    public MentorHelpAction Action;

    /// <summary>The take-over note.</summary>
    public string? Note;

    /// <summary>The mentor a ticket is transferred to.</summary>
    public string? Target;
}

/// <summary>Client to server: the asker's action on their own ticket.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpPlayerActionRequest : EntityEventArgs
{
    public MentorHelpPlayerAction Action;

    /// <summary>For Rate: thumbs up.</summary>
    public bool Up;

    /// <summary>For Rate: the optional line.</summary>
    public string? Comment;
}

/// <summary>Client to server: staff move a player's ahelp to Mentor Help.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpFromAHelpRequest : EntityEventArgs
{
    public NetUserId Player;
}

/// <summary>Client to server: send the current state again (the window opened).</summary>
[Serializable, NetSerializable]
public sealed class WGMentorHelpRefreshRequest : EntityEventArgs
{
}
