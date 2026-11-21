using Robust.Shared.Serialization;

namespace Content.Shared._WG.Mentors;

public enum MentorBoardPeriod : byte
{
    Week,
    Month,
    All,
}

/// <summary>One mentor's numbers for a period.</summary>
[Serializable, NetSerializable]
public sealed class MentorBoardRow
{
    public string Name = "";
    public int Resolved;

    /// <summary>Resolved tickets rated helpful.</summary>
    public int Helpful;

    /// <summary>Resolved tickets with any rating.</summary>
    public int Rated;

    /// <summary>Median seconds from opening to the first answer, over the tickets they resolved; null with none.</summary>
    public float? MedianFirstAnswer;

    public int Assists;

    /// <summary>Tickets they sent on to staff. Staff only.</summary>
    public int Escalations;
}

/// <summary>Client to server: the leaderboard for a period. Mentors and staff.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorBoardRequestEvent : EntityEventArgs
{
    public MentorBoardPeriod Period;
}

/// <summary>Server to one client: the ranked rows for a period and the asker's own place.</summary>
[Serializable, NetSerializable]
public sealed class WGMentorBoardEvent : EntityEventArgs
{
    public MentorBoardPeriod Period;
    public List<MentorBoardRow> Rows = new();

    /// <summary>The asker's row and 1-based rank, when they have one this period.</summary>
    public MentorBoardRow? Mine;

    public int MyRank;

    /// <summary>Whether the staff columns are filled.</summary>
    public bool Staff;

    /// <summary>Tickets that fell back to staff unanswered this period. Staff only.</summary>
    public int Fallbacks;
}

/// <summary>Ratings a mentor needs before the rating share is shown.</summary>
public static class MentorBoardRules
{
    public const int MinRatingsShown = 5;
}
