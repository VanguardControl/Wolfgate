using System.Linq;
using Content.Server.Database;
using Content.Shared._WG.Mentors;

namespace Content.Server._WG.Mentors;

/// <summary>One mentor's counted tickets for a period, before names are looked up.</summary>
public sealed class MentorBoardStats
{
    public Guid Mentor;
    public int Resolved;
    public int Helpful;
    public int Rated;
    public int Assists;
    public int Escalations;
    public int Watches;
    public int CritterVisits;
    public readonly List<double> FirstAnswerSeconds = new();

    public double? MedianFirstAnswer
    {
        get
        {
            if (FirstAnswerSeconds.Count == 0)
                return null;

            var sorted = FirstAnswerSeconds.OrderBy(s => s).ToList();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }
    }
}

/// <summary>
/// The leaderboard rules (feature plan 4.13). The mentor holding the claim at close gets the resolve; every other
/// mentor who posted gets an assist. A ticket the asker ended after an answer counts as resolved; idle, round-end,
/// fallback and escalated closes don't. Ranked by helpful resolves, then resolves, then the faster median first answer.
/// </summary>
public static class MentorBoard
{
    public static bool IsResolved(MentorBoardTicket ticket)
    {
        return ticket.CloseReason == "resolved" || (ticket.CloseReason == "player" && ticket.FirstAnswerAt != null);
    }

    /// <summary>Counts each mentor's tickets and ranks them. The asker never appears unless they answered someone.</summary>
    public static List<MentorBoardStats> Compute(IEnumerable<MentorBoardTicket> tickets)
    {
        var stats = new Dictionary<Guid, MentorBoardStats>();

        MentorBoardStats Get(Guid mentor)
        {
            if (!stats.TryGetValue(mentor, out var row))
                stats[mentor] = row = new MentorBoardStats { Mentor = mentor };

            return row;
        }

        foreach (var ticket in tickets)
        {
            var holder = ticket.ClaimedBy;
            if (holder is { } claimer)
            {
                if (IsResolved(ticket))
                {
                    var row = Get(claimer);
                    row.Resolved++;
                    if (ticket.Rating != null)
                        row.Rated++;
                    if (ticket.Rating > 0)
                        row.Helpful++;
                    if (ticket.FirstAnswerAt is { } first)
                        row.FirstAnswerSeconds.Add(Math.Max(0, (first - ticket.OpenedAt).TotalSeconds));
                }
                else if (ticket.CloseReason == "escalated")
                {
                    Get(claimer).Escalations++;
                }
            }

            foreach (var responder in ticket.Responders)
            {
                if (responder != holder)
                    Get(responder).Assists++;
            }

            foreach (var watcher in ticket.Watches)
            {
                Get(watcher).Watches++;
            }

            foreach (var visitor in ticket.CritterVisits)
            {
                Get(visitor).CritterVisits++;
            }
        }

        return stats.Values
            .OrderByDescending(s => s.Helpful)
            .ThenByDescending(s => s.Resolved)
            .ThenBy(s => s.MedianFirstAnswer ?? double.MaxValue)
            .ThenByDescending(s => s.Assists)
            .ToList();
    }

    /// <summary>When a period starts: the week from Monday 00:00 UTC, the month from its first day, all time as null.</summary>
    public static DateTime? Since(MentorBoardPeriod period, DateTime now)
    {
        return period switch
        {
            MentorBoardPeriod.Week =>
                now.Date.AddDays(-(((int) now.DayOfWeek + 6) % 7)),
            MentorBoardPeriod.Month => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => null,
        };
    }
}
