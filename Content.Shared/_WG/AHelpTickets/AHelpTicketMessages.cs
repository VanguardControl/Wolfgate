using Content.Shared._WG.EventStaff;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.AHelpTickets;

/// <summary>Where an ahelp ticket stands.</summary>
[Serializable, NetSerializable]
public enum AHelpTicketState : byte
{
    Open,
    Claimed,
    Resolved,
    Rejected,
    IcIssue,
    ClosedRoundEnd,
}

/// <summary>What staff can do to a ticket.</summary>
[Serializable, NetSerializable]
public enum AHelpTicketAction : byte
{
    Claim,
    Unclaim,
    Resolve,
    Reject,
    IcIssue,
    Reopen,
}

/// <summary>Ticket state helpers.</summary>
public static class AHelpTicketStates
{
    /// <summary>Whether the ticket is finished with.</summary>
    public static bool IsClosed(this AHelpTicketState state)
    {
        return state >= AHelpTicketState.Resolved;
    }
}

/// <summary>What staff see of a ticket in the list.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketInfo
{
    /// <summary>Identifies the ticket this server run, before and after it has a number.</summary>
    public int Key;

    /// <summary>The ticket number, from the database; 0 until it is stored.</summary>
    public int Number;

    public int RoundId;
    public NetUserId Player;
    public string Username = string.Empty;
    public string Character = string.Empty;
    public AHelpCategory Category;
    public AHelpTicketState State;
    public NetUserId? Assignee;
    public string? AssigneeName;

    /// <summary>UTC.</summary>
    public DateTime Opened;

    /// <summary>The last player or staff message, UTC.</summary>
    public DateTime LastActivity;

    /// <summary>The player's last message, UTC.</summary>
    public DateTime LastPlayerMessage;

    /// <summary>The first staff reply the player saw, UTC.</summary>
    public DateTime? FirstReply;

    public bool HasNotes;
    public int Messages;
    public bool Online;

    /// <summary>Staff who have this ticket open in their panel now.</summary>
    public List<string> Viewers = new();
}

/// <summary>A past ticket, as listed in a player's history or a search.</summary>
[Serializable, NetSerializable]
public sealed record AHelpTicketHistoryEntry(
    int Number,
    int RoundId,
    string ServerName,
    string Username,
    DateTime Opened,
    AHelpTicketState State,
    string? Assignee,
    string Snippet);

/// <summary>One line of a stored ticket.</summary>
[Serializable, NetSerializable]
public sealed record AHelpTicketLine(DateTime SentAt, string Author, string Source, bool AdminOnly, string Text);

/// <summary>Server to staff: every ticket of the round they can see.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketListMessage(List<AHelpTicketInfo> tickets) : EntityEventArgs
{
    public readonly List<AHelpTicketInfo> Tickets = tickets;
}

/// <summary>Server to staff: one ticket changed.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketUpdatedMessage(AHelpTicketInfo ticket) : EntityEventArgs
{
    public readonly AHelpTicketInfo Ticket = ticket;
}

/// <summary>Staff to server: claim, close or reopen a ticket.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketActionMessage(int key, AHelpTicketAction action) : EntityEventArgs
{
    public readonly int Key = key;
    public readonly AHelpTicketAction Action = action;
}

/// <summary>Staff to server: a macro was used on a ticket, for counting.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketMacroUsedMessage(int key, string macro) : EntityEventArgs
{
    public readonly int Key = key;
    public readonly string Macro = macro;
}

/// <summary>
/// Staff to server: a macro's reply. The server sends it as the staffer and applies the macro to the ticket the
/// reply lands on, new or existing, so the action can't reach any other ticket.
/// </summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketMacroReplyMessage(NetUserId player, string text, bool playSound, bool adminOnly, string macro, AHelpTicketAction? action) : EntityEventArgs
{
    public readonly NetUserId Player = player;
    public readonly string Text = text;
    public readonly bool PlaySound = playSound;
    public readonly bool AdminOnly = adminOnly;
    public readonly string Macro = macro;
    public readonly AHelpTicketAction? Action = action;
}

/// <summary>Staff to server: what the player card needs that the client doesn't have.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketPlayerCardRequest(NetUserId player) : EntityEventArgs
{
    public readonly NetUserId Player = player;
}

/// <summary>A ship a player owns, for opening it in Shipmaster.</summary>
[Serializable, NetSerializable]
public sealed record AHelpTicketShip(NetEntity Grid, string Name);

/// <summary>Server to staff: a player's notes, bans, bank, past tickets, ships and loadout price.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketPlayerCardMessage : EntityEventArgs
{
    public NetUserId Player;
    public int Notes;
    public int Bans;
    public int? Bank;
    public int TotalTickets;
    public List<AHelpTicketHistoryEntry> Previous = new();
    public List<AHelpTicketShip> Ships = new();

    /// <summary>What the player's job loadout costs; null when they have no body or loadout to give.</summary>
    public int? LoadoutPrice;
}

/// <summary>Staff to server: which ticket this staffer has open in their panel; null when none.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketViewMessage(int? key) : EntityEventArgs
{
    public readonly int? Key = key;
}

/// <summary>Staff to server: ask the other staff online to look at a ticket.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketAssistMessage(int key) : EntityEventArgs
{
    public readonly int Key = key;
}

/// <summary>Server to the other staff: someone asks for help with a ticket; plays <see cref="Sound"/> whatever chat shows.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketAssistAlertMessage(int key, string sound) : EntityEventArgs
{
    public readonly int Key = key;
    public readonly string Sound = sound;
}

/// <summary>Staff to server: give the player their job loadout again, free or paid from their bank.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketLoadoutMessage(NetUserId player, bool charge) : EntityEventArgs
{
    public readonly NetUserId Player = player;
    public readonly bool Charge = charge;
}

/// <summary>Staff to server: the transcript of a stored ticket.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketTranscriptRequest(int number) : EntityEventArgs
{
    public readonly int Number = number;
}

/// <summary>Server to staff: a stored ticket's transcript; empty when there is no such ticket.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketTranscriptMessage(AHelpTicketHistoryEntry? ticket, List<AHelpTicketLine> lines) : EntityEventArgs
{
    public readonly AHelpTicketHistoryEntry? Ticket = ticket;
    public readonly List<AHelpTicketLine> Lines = lines;
}

/// <summary>Staff to server: find stored tickets by what was said in them.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketSearchRequest(string text) : EntityEventArgs
{
    public readonly string Text = text;
}

/// <summary>Server to staff: stored tickets matching a search, newest first.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketSearchResultsMessage(string text, List<AHelpTicketHistoryEntry> results) : EntityEventArgs
{
    public readonly string Text = text;
    public readonly List<AHelpTicketHistoryEntry> Results = results;
}

/// <summary>Server to a player: their ticket's number and state.</summary>
[Serializable, NetSerializable]
public sealed class AHelpTicketPlayerStatusMessage(int number, AHelpTicketState state, string? handler) : EntityEventArgs
{
    public readonly int Number = number;
    public readonly AHelpTicketState State = state;
    public readonly string? Handler = handler;
}
