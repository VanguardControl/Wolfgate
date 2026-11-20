using Robust.Shared.Serialization;

namespace Content.Shared._WG.Mentors;

/// <summary>A mentor's rank. Stored in lowercase in <c>wolfgate_mentor.tier</c>.</summary>
public enum MentorTier : byte
{
    Trainee,
    Mentor,
    Senior,
}

/// <summary>Server to one client: whether they are an active mentor, their tier and whether they are on duty.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorStatusEvent : EntityEventArgs
{
    public bool IsMentor;
    public MentorTier Tier;
    public bool OnDuty;
}

/// <summary>Server to every client: how many mentors are on duty.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorCountEvent : EntityEventArgs
{
    public int OnDuty;
}

/// <summary>Client to server: a mentor goes on or off duty.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorSetDutyEvent : EntityEventArgs
{
    public bool OnDuty;
}
