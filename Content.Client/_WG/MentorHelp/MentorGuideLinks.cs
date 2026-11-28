using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WG.MentorHelp;

/// <summary>
/// Guidebook pages in Mentor Help: matching pages suggested from a question, and the <c>{guide:ENTRYID}</c> tokens
/// mentors put in replies, shown as clickable page names.
/// </summary>
public static class MentorGuideLinks
{
    /// <summary>The tags a ticket line may hold.</summary>
    public static readonly Type[] LineTags = { typeof(BoldTag), typeof(WGGuideLinkTag) };

    private static readonly Regex Token = new(@"\{guide:([A-Za-z0-9_\-]+)\}");

    /// <summary>The token a mentor puts in a reply to link a page.</summary>
    public static string TokenFor(string entryId)
    {
        return $"{{guide:{entryId}}}";
    }

    /// <summary>Adds text to a message with its guide tokens turned into links; unknown pages stay as text.</summary>
    public static void AddText(FormattedMessage message, string text, IPrototypeManager prototypes)
    {
        var last = 0;
        foreach (Match match in Token.Matches(text))
        {
            if (!prototypes.HasIndex<GuideEntryPrototype>(match.Groups[1].Value))
                continue;

            if (match.Index > last)
                message.AddText(text[last..match.Index]);

            message.PushTag(new MarkupNode(WGGuideLinkTag.TagName, new MarkupParameter(match.Groups[1].Value), null), selfClosing: true);
            last = match.Index + match.Length;
        }

        if (last < text.Length)
            message.AddText(text[last..]);
    }

    /// <summary>Up to <paramref name="max"/> pages whose names share the most words with a question.</summary>
    public static List<GuideEntryPrototype> Suggest(string question, IPrototypeManager prototypes, int max = 3)
    {
        var words = question.ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '?', '!', ';', ':', '\'', '"', '(', ')', '/', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4)
            .Distinct()
            .ToList();

        if (words.Count == 0)
            return new List<GuideEntryPrototype>();

        return prototypes.EnumeratePrototypes<GuideEntryPrototype>()
            .Select(entry => (entry, score: Score(Loc.GetString(entry.Name).ToLowerInvariant(), words)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.entry.Priority)
            .Take(max)
            .Select(x => x.entry)
            .ToList();
    }

    /// <summary>Pages whose names contain a search text, for the mentor's link picker.</summary>
    public static List<GuideEntryPrototype> Search(string search, IPrototypeManager prototypes, int max = 8)
    {
        var needle = search.Trim().ToLowerInvariant();
        if (needle.Length < 2)
            return new List<GuideEntryPrototype>();

        return prototypes.EnumeratePrototypes<GuideEntryPrototype>()
            .Where(entry => Loc.GetString(entry.Name).ToLowerInvariant().Contains(needle))
            .OrderBy(entry => Loc.GetString(entry.Name))
            .Take(max)
            .ToList();
    }

    private static int Score(string name, List<string> words)
    {
        var score = 0;
        foreach (var word in words)
        {
            // A word's first four letters are enough: "docking" finds "Docking", "ships" finds "Ship".
            var stem = word[..4];
            if (name.Contains(stem))
                score++;
        }

        return score;
    }
}
