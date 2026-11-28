using System.Text.Json.Nodes;
using Content.Shared._WG.Mentors;

namespace Content.Server._WG.Mentors;

/// <summary>What the application checks know about a player, gathered from the database.</summary>
public sealed class MentorRecord
{
    public TimeSpan Playtime;

    /// <summary>A server ban, or a role ban other than a mentor ban, in force now.</summary>
    public bool ActiveBan;

    public bool MentorBanned;

    /// <summary>When the mentor ban ends; null for a permanent one.</summary>
    public DateTime? MentorBanUntil;

    /// <summary>Bans of any kind placed inside the clean-record window, in force or not.</summary>
    public int RecentBans;

    /// <summary>Notes of Medium or worse inside the window.</summary>
    public int SeriousNotes;

    /// <summary>Minor notes inside the window: no block, but the record isn't spotless.</summary>
    public int MinorNotes;

    /// <summary>Whether they were on the roster before and were revoked.</summary>
    public bool RevokedBefore;

    /// <summary>The latest of a denial's reapply date and the end of the wait after leaving the roster.</summary>
    public DateTime? ReapplyAfter;
}

/// <summary>The outcome of the application checks.</summary>
public sealed class MentorEligibilityResult
{
    public readonly List<MentorBlock> Blocks = new();

    /// <summary>No blocks and nothing for a reviewer to look at: approved at once in hybrid mode.</summary>
    public bool Spotless;

    /// <summary>Why it isn't spotless, for reviewers, when nothing blocks it.</summary>
    public readonly List<string> Concerns = new();

    public bool CanApply => Blocks.Count == 0;
}

/// <summary>The rules for applying to be a mentor (feature plan 4.8, D1 and D2). Pure, so it is unit tested.</summary>
public static class MentorEligibility
{
    public static MentorEligibilityResult Check(MentorRecord record, float minHours, DateTime now)
    {
        var result = new MentorEligibilityResult();

        if (record.Playtime < TimeSpan.FromHours(minHours))
            result.Blocks.Add(MentorBlock.Hours);

        if (record.ActiveBan)
            result.Blocks.Add(MentorBlock.Ban);

        if (record.MentorBanned && (record.MentorBanUntil == null || record.MentorBanUntil > now))
            result.Blocks.Add(MentorBlock.MentorBan);

        if (record.RecentBans > 0 || record.SeriousNotes > 0)
            result.Blocks.Add(MentorBlock.Record);

        if (record.ReapplyAfter > now)
            result.Blocks.Add(MentorBlock.Reapply);

        if (record.MinorNotes > 0)
            result.Concerns.Add($"minor notes in the window: {record.MinorNotes}");

        if (record.RevokedBefore)
            result.Concerns.Add("revoked before");

        result.Spotless = result.CanApply && result.Concerns.Count == 0;
        return result;
    }

    /// <summary>The record as stored with an application, for reviewers and the panel.</summary>
    public static string Summary(MentorRecord record, MentorEligibilityResult result)
    {
        var concerns = new JsonArray();
        foreach (var concern in result.Concerns)
        {
            concerns.Add(concern);
        }

        return new JsonObject
        {
            ["hours"] = Math.Round(record.Playtime.TotalHours, 1),
            ["recent_bans"] = record.RecentBans,
            ["serious_notes"] = record.SeriousNotes,
            ["minor_notes"] = record.MinorNotes,
            ["revoked_before"] = record.RevokedBefore,
            ["spotless"] = result.Spotless,
            ["concerns"] = concerns,
        }.ToJsonString();
    }

    /// <summary>Whether a Trainee has earned promotion to Mentor (D3).</summary>
    public static bool Promotable(int resolved, int rated, int helpful, TimeSpan onRoster,
        int needTickets, float needRating, int needDays)
    {
        if (resolved < needTickets || onRoster < TimeSpan.FromDays(needDays))
            return false;

        // With no ratings at all there is nothing to fall short of.
        return rated == 0 || (float) helpful / rated >= needRating;
    }
}
