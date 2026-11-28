using Robust.Shared.Serialization;

namespace Content.Shared._WG.Mentors;

/// <summary>Where a player stands on the Mentoring page.</summary>
public enum MentorPageStatus : byte
{
    /// <summary>Mentors are switched off on this server.</summary>
    Disabled,

    /// <summary>Not a mentor; may apply when nothing blocks it.</summary>
    Apply,

    /// <summary>An application is waiting for review.</summary>
    Pending,

    Active,
    Inactive,
    Suspended,
}

/// <summary>Something that stops a player applying now.</summary>
public enum MentorBlock : byte
{
    Hours,
    Ban,
    MentorBan,
    Record,
    Reapply,
}

/// <summary>Whether the player's Discord is linked through the panel.</summary>
public enum MentorDiscordState : byte
{
    /// <summary>No panel bridge on this server (no symphony.url, or not PostgreSQL).</summary>
    Unavailable,
    Linked,
    Unlinked,
}

/// <summary>The application form's question ids, in order. Their text is <c>form-&lt;id&gt;</c> in Fluent.</summary>
public static class MentorForm
{
    public static readonly string[] Questions = { "q1", "q2", "q3", "q4", "q5" };

    public const int MaxAnswerLength = 500;
}

/// <summary>Client to server: send the Mentoring page's state.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorPageRequestEvent : EntityEventArgs
{
}

/// <summary>Server to one client: the Mentoring page.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorPageStateEvent : EntityEventArgs
{
    public MentorPageStatus Status;
    public MentorTier Tier;
    public float Hours;
    public float NeededHours;
    public List<MentorBlock> Blocks = new();

    /// <summary>When a mentor ban ends; null with <see cref="MentorBlock.MentorBan"/> for a permanent one.</summary>
    public DateTime? MentorBanUntil;

    /// <summary>When the player may apply again, with <see cref="MentorBlock.Reapply"/>.</summary>
    public DateTime? ReapplyAfter;

    public int CleanDays;
    public int ReapplyDays;
    public MentorDiscordState Discord;
}

/// <summary>Client to server: apply with the form's answers, keyed by question id.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorApplyEvent : EntityEventArgs
{
    public Dictionary<string, string> Answers = new();
    public bool AgreedToCode;
}

/// <summary>Client to server: what a mentor or would-be mentor does on the Mentoring page.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorPageActionEvent : EntityEventArgs
{
    public MentorPageAction Action;
}

public enum MentorPageAction : byte
{
    Resign,
    Reactivate,
    LinkDiscord,
}

/// <summary>Server to one client: the panel's one-time Discord link, to open in the browser.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorDiscordLinkEvent : EntityEventArgs
{
    public string Url = "";
}

/// <summary>Server to one client: they reached the hours to apply; the client remembers it so this comes once.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorEligibleNoticeEvent : EntityEventArgs
{
}
