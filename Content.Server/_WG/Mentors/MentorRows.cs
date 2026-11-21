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
        return row.State is StateActive or StateInactive or StateSuspended;
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
