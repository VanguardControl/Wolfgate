using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Content.Shared._WG.LogInvestigator;

/// <summary>Reading admin log text: entity ids in messages, round times, and runs of repeated logs.</summary>
public static class LogInvestigatorText
{
    private static readonly Regex EntityIdRegex = new(@"\((\d+)/n(\d+)", RegexOptions.Compiled);

    /// <summary>A run of rows shown as one: <see cref="Count"/> rows from <see cref="Start"/>.</summary>
    public readonly record struct RowRun(int Start, int Count);

    /// <summary>The entities a message names, as server id to NetEntity id ("Name (123/n456, ...)").</summary>
    public static Dictionary<int, int> EntityIds(string message)
    {
        var ids = new Dictionary<int, int>();
        foreach (Match match in EntityIdRegex.Matches(message))
        {
            if (int.TryParse(match.Groups[1].Value, out var uid) && int.TryParse(match.Groups[2].Value, out var net))
                ids.TryAdd(uid, net);
        }

        return ids;
    }

    /// <summary>
    /// Groups consecutive rows of the same type that name the same entities, each within <paramref name="window"/> of
    /// the one before, so a burst of fire reads as one line.
    /// </summary>
    public static List<RowRun> Collapse(IReadOnlyList<LogRow> rows, TimeSpan window)
    {
        var runs = new List<RowRun>();
        var start = 0;
        for (var i = 1; i <= rows.Count; i++)
        {
            if (i < rows.Count && SameEvent(rows[i - 1], rows[i], window))
                continue;

            if (rows.Count > 0)
                runs.Add(new RowRun(start, i - start));
            start = i;
        }

        return runs;
    }

    private static bool SameEvent(LogRow a, LogRow b, TimeSpan window)
    {
        if (a.Type != b.Type || (a.Date - b.Date).Duration() > window || a.Refs.Length != b.Refs.Length)
            return false;

        var ids = a.Refs.Select(r => r.Id).ToHashSet();
        return b.Refs.All(r => ids.Contains(r.Id));
    }

    /// <summary>Round time as h:mm:ss.</summary>
    public static string RoundTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        return $"{(int) time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}";
    }

    /// <summary>Reads a round time typed as h:mm:ss, mm:ss or a number of minutes; null when empty or unreadable.</summary>
    public static double? ParseRoundTime(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;

        var parts = text.Split(':');
        if (parts.Length == 1)
            return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) ? minutes * 60 : null;

        var seconds = 0.0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var value) || value < 0)
                return null;
            seconds = seconds * 60 + value;
        }

        return parts.Length <= 3 ? seconds : null;
    }
}
