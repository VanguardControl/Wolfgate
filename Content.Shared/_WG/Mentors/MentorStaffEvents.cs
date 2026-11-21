using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Mentors;

/// <summary>A part of the staff Mentors tab.</summary>
public enum MentorStaffView : byte
{
    Roster,
    Applications,
    Tickets,
}

/// <summary>Client to server: send a part of the Mentors tab. Tickets are filtered by the search text.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorStaffRequestEvent : EntityEventArgs
{
    public MentorStaffView View;
    public string Search = "";
}

/// <summary>A roster row as staff see it.</summary>
[Serializable, NetSerializable]
public sealed class MentorRosterRow
{
    public NetUserId User;
    public string Name = "";
    public MentorTier Tier;

    /// <summary>The stored state: active, inactive, suspended, revoked or resigned.</summary>
    public string State = "";

    public bool Connected;
    public bool OnDuty;
    public DateTime GrantedAt;
    public DateTime LastActiveAt;
    public int Resolved;
    public int Rated;
    public int Helpful;
    public List<string> History = new();
    public string Notes = "";
}

/// <summary>A pending application as reviewers see it.</summary>
[Serializable, NetSerializable]
public sealed class MentorApplicationRow
{
    public int Id;
    public NetUserId User;
    public string Name = "";
    public DateTime CreatedAt;
    public int Hours;

    /// <summary>The answers, in the form's order, by question id.</summary>
    public Dictionary<string, string> Answers = new();

    /// <summary>Why it wasn't approved at once, one line each; empty when the mode queues every application.</summary>
    public List<string> Record = new();
}

/// <summary>A ticket in the staff list.</summary>
[Serializable, NetSerializable]
public sealed class MentorTicketRow
{
    public int Id;
    public DateTime OpenedAt;
    public string Player = "";
    public string Character = "";
    public string Mentor = "";
    public string CloseReason = "";
    public int? Rating;
}

[Serializable, NetSerializable]
public sealed class WGMentorStaffRosterEvent : EntityEventArgs
{
    public List<MentorRosterRow> Rows = new();
}

[Serializable, NetSerializable]
public sealed class WGMentorStaffApplicationsEvent : EntityEventArgs
{
    public List<MentorApplicationRow> Rows = new();
}

[Serializable, NetSerializable]
public sealed class WGMentorStaffTicketsEvent : EntityEventArgs
{
    public List<MentorTicketRow> Rows = new();
}

/// <summary>Client to server: a stored ticket's full conversation.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorTranscriptRequestEvent : EntityEventArgs
{
    public int Id;
}

[Serializable, NetSerializable]
public sealed class WGMentorTranscriptEvent : EntityEventArgs
{
    public int Id;
    public List<string> Lines = new();
}

public enum MentorStaffAction : byte
{
    Grant,
    Revoke,
    Tier,
    Restore,
    Notes,
}

/// <summary>Client to server: a roster change from the Mentors tab. Needs the Moderator flag.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorStaffActionEvent : EntityEventArgs
{
    public MentorStaffAction Action;

    /// <summary>A username or user id; for grants it may be someone not on the roster.</summary>
    public string Player = "";

    public MentorTier Tier;

    /// <summary>The revoke reason, or the staff notes.</summary>
    public string Text = "";
}

/// <summary>Client to server: approve or deny an application. Staff with Moderator, or a Senior Mentor.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorReviewEvent : EntityEventArgs
{
    public int Id;
    public bool Approve;
    public string Reason = "";

    /// <summary>Days before a denied player may apply again; 0 for the server's default.</summary>
    public int ReapplyDays;
}

/// <summary>Server to one client: how a staff action or review went.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorStaffResultEvent : EntityEventArgs
{
    public string Message = "";
}
