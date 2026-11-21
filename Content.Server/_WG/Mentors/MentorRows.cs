using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Content.Server.Database;
using Content.Shared._WG.Mentors;

namespace Content.Server._WG.Mentors;

/// <summary>The values stored in <c>wolfgate_mentor</c> and the history list kept on each row.</summary>
public static class MentorRows
{
    public const string StateActive = "active";
    public const string StateInactive = "inactive";
    public const string StateSuspended = "suspended";
    public const string StateRevoked = "revoked";
    public const string StateResigned = "resigned";

    public const string SourceAuto = "auto";
    public const string SourceStaff = "staff";
    public const string SourcePanel = "panel";

    /// <summary>Whether a row's player is on the roster, active or not.</summary>
    public static bool OnRoster(WolfgateMentor row)
    {
        return OnRoster(row.State);
    }

    /// <summary>Whether a stored state is on the roster.</summary>
    public static bool OnRoster(string state)
    {
        return state is StateActive or StateInactive or StateSuspended;
    }

    public static string TierToDb(MentorTier tier)
    {
        return tier switch
        {
            MentorTier.Senior => "senior",
            MentorTier.Mentor => "mentor",
            _ => "trainee",
        };
    }

    public static MentorTier TierFromDb(string tier)
    {
        return tier switch
        {
            "senior" => MentorTier.Senior,
            "mentor" => MentorTier.Mentor,
            _ => MentorTier.Trainee,
        };
    }

    /// <summary>Reads a tier typed in a command or sent by the panel.</summary>
    public static bool TryParseTier(string text, out MentorTier tier)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "trainee":
                tier = MentorTier.Trainee;
                return true;
            case "mentor":
                tier = MentorTier.Mentor;
                return true;
            case "senior":
                tier = MentorTier.Senior;
                return true;
            default:
                tier = default;
                return false;
        }
    }

    /// <summary>The tier's player-facing name.</summary>
    public static string TierName(MentorTier tier)
    {
        return Loc.GetString($"wg-mentor-tier-{TierToDb(tier)}");
    }

    /// <summary>One entry of a row's history.</summary>
    public readonly record struct HistoryEntry(DateTime At, Guid? By, string What, string? Why);

    /// <summary>A row's history, oldest first. Entries that don't parse are skipped.</summary>
    public static List<HistoryEntry> History(WolfgateMentor row)
    {
        var entries = new List<HistoryEntry>();
        JsonArray? list;
        try
        {
            list = JsonNode.Parse(row.History) as JsonArray;
        }
        catch (JsonException)
        {
            return entries;
        }

        if (list == null)
            return entries;

        foreach (var node in list)
        {
            if (node is not JsonObject entry
                || entry["what"]?.GetValue<string>() is not { } what
                || !DateTime.TryParse(entry["at"]?.GetValue<string>(), null, DateTimeStyles.RoundtripKind, out var at))
            {
                continue;
            }

            Guid? by = Guid.TryParse(entry["by"]?.GetValue<string>(), out var parsed) ? parsed : null;
            entries.Add(new HistoryEntry(at.ToUniversalTime(), by, what, entry["why"]?.GetValue<string>()));
        }

        return entries;
    }

    /// <summary>When the latest history entry with this text was made, or null.</summary>
    public static DateTime? LastEntryTime(WolfgateMentor row, string what)
    {
        var history = History(row);
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].What == what)
                return history[i].At;
        }

        return null;
    }

    /// <summary>Adds an entry to a row's history: when, who (user id, or null for the server), what and why.</summary>
    public static void AddHistory(WolfgateMentor row, DateTime when, Guid? by, string what, string? why = null)
    {
        JsonArray list;
        try
        {
            list = JsonNode.Parse(row.History) as JsonArray ?? new JsonArray();
        }
        catch (JsonException)
        {
            list = new JsonArray();
        }

        var entry = new JsonObject
        {
            ["at"] = when.ToUniversalTime().ToString("O"),
            ["by"] = by?.ToString(),
            ["what"] = what,
        };

        if (!string.IsNullOrWhiteSpace(why))
            entry["why"] = why;

        list.Add(entry);
        row.History = list.ToJsonString();
    }
}
