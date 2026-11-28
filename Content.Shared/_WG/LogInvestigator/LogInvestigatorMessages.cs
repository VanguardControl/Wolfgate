using Content.Shared.Database;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.LogInvestigator;

/// <summary>A player who appears in a round's logs.</summary>
[Serializable, NetSerializable]
public sealed record LogPlayer(Guid Id, string Username, string? Character);

/// <summary>
/// An entity a log names, from its JSON: the key it was logged under, its id, name and player, and where it was when
/// the log was written, if the type records positions.
/// </summary>
[Serializable, NetSerializable]
public sealed record LogRef(string Key, int Id, string? Name, Guid? Player, int? Map, int? Grid, float? X, float? Y);

/// <summary>One admin log as the investigator shows it; <see cref="Damage"/> is the JSON's "damage", if any.</summary>
[Serializable, NetSerializable]
public sealed record LogRow(int Id, LogType Type, LogImpact Impact, DateTime Date, string Message, LogRef[] Refs, float? Damage);

/// <summary>What to look for in a round's logs.</summary>
[Serializable, NetSerializable]
public sealed class LogQuery
{
    public int RoundId;

    /// <summary>Round time window, in seconds from the round's start.</summary>
    public double? FromSeconds;

    public double? ToSeconds;

    /// <summary>Null for every type.</summary>
    public HashSet<LogType>? Types;

    /// <summary>Null for every impact.</summary>
    public HashSet<LogImpact>? Impacts;

    public Guid[] Players = Array.Empty<Guid>();

    /// <summary>Only logs that involve every one of <see cref="Players"/>, instead of any of them.</summary>
    public bool AllPlayers;

    /// <summary>Entity ids (as in "Name (123/n123)") a log must name, any of them.</summary>
    public int[] Entities = Array.Empty<int>();

    /// <summary>Text the message contains, ignoring case.</summary>
    public string? Text;

    /// <summary>With no player filter, whether logs with no player in them are shown.</summary>
    public bool IncludeNonPlayers = true;

    public bool Ascending;

    /// <summary>Only the players who came within <see cref="NearRadius"/> of this point in the time window.</summary>
    public LogPoint? Near;

    public int NearRadius = 15;
}

/// <summary>The scripted reports the investigator can build.</summary>
[Serializable, NetSerializable]
public enum LogReportKind : byte
{
    /// <summary>Who hit whom, who struck first, deaths, and what was said before the first hit.</summary>
    Encounter,

    /// <summary>One player's round in phases.</summary>
    Timeline,

    /// <summary>Who was near a place during a window.</summary>
    Nearby,
}

/// <summary>A place and moment, for "who was nearby".</summary>
[Serializable, NetSerializable]
public sealed record LogPoint(int Map, int? Grid, float X, float Y, DateTime At);

/// <summary>A titled block of report lines, and the logs behind it shown like the result list.</summary>
[Serializable, NetSerializable]
public sealed record LogReportSection(string Title, List<string> Lines, List<LogRow>? Rows = null);

/// <summary>A finished report; <see cref="Filter"/> is a search that narrows the results to what it found.</summary>
[Serializable, NetSerializable]
public sealed record LogReport(LogReportKind Kind, string Title, List<LogReportSection> Sections, LogQuery? Filter = null);

[Serializable, NetSerializable]
public sealed class LogInvestigatorEuiState : EuiStateBase
{
    public int RoundId;
    public int CurrentRound;

    /// <summary>UTC; null when the round has no recorded start.</summary>
    public DateTime? RoundStart;

    public List<LogPlayer> Players = new();
    public bool Loading;
}

/// <summary>
/// Client to server: run a query, from the start or after the last log shown; with <see cref="Newer"/>, the logs
/// newer than <see cref="AfterId"/> instead, for keeping a newest-first list live.
/// </summary>
[Serializable, NetSerializable]
public sealed class LogInvestigatorQueryMsg(LogQuery query, int? afterId, bool newer = false) : EuiMessageBase
{
    public readonly LogQuery Query = query;
    public readonly int? AfterId = afterId;
    public readonly bool Newer = newer;
}

/// <summary>Server to client: a page of results; <see cref="Newer"/> rows go above the ones shown.</summary>
[Serializable, NetSerializable]
public sealed class LogInvestigatorPageMsg(List<LogRow> rows, bool replace, bool hasMore, int? total, bool newer = false) : EuiMessageBase
{
    public readonly List<LogRow> Rows = rows;
    public readonly bool Replace = replace;
    public readonly bool HasMore = hasMore;
    public readonly bool Newer = newer;

    /// <summary>How many logs match, counted with the first page only.</summary>
    public readonly int? Total = total;
}

/// <summary>Client to server: look at another round.</summary>
[Serializable, NetSerializable]
public sealed class LogInvestigatorRoundMsg(int roundId) : EuiMessageBase
{
    public readonly int RoundId = roundId;
}

/// <summary>Server to client: fill in the query (and build a report) when opened from a ticket, verb or panel.</summary>
[Serializable, NetSerializable]
public sealed class LogInvestigatorPrefillMsg(LogQuery query, LogReportKind? report) : EuiMessageBase
{
    public readonly LogQuery Query = query;
    public readonly LogReportKind? Report = report;
}

/// <summary>Client to server: build a report.</summary>
[Serializable, NetSerializable]
public sealed class LogInvestigatorReportRequest(LogReportKind kind, LogQuery query, LogPoint? point, int radius) : EuiMessageBase
{
    public readonly LogReportKind Kind = kind;
    public readonly LogQuery Query = query;
    public readonly LogPoint? Point = point;
    public readonly int Radius = radius;
}

/// <summary>Server to client: a built report.</summary>
[Serializable, NetSerializable]
public sealed class LogInvestigatorReportMsg(LogReport report) : EuiMessageBase
{
    public readonly LogReport Report = report;
}
