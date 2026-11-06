using System.Text;
using Content.Shared.Guidebook;

namespace Content.Client._WF.LoreBook;

/// <summary>
/// Some pages should not be read too closely. Each page's <see cref="GuideEntry.Unease"/> builds up while this book is
/// open: past a little, letters in every page begin to slip; further on, the book starts adding lines of its own; deep
/// in, even its title and page numbers go wrong. Closing the book and opening it again puts everything back.
/// </summary>
public sealed partial class WFLoreBookWindow
{
    /// <summary>How unsettled this copy of the book is, 0 to 1. A new window starts clean.</summary>
    private float _unease;

    /// <summary>Unease past which the book writes lines of its own into pages.</summary>
    private const float WhisperUnease = 0.35f;

    /// <summary>Unease past which the title, chapter trail and page numbers go wrong too.</summary>
    private const float FrameUnease = 0.6f;

    private const int WhisperLines = 10;

    /// <summary>What a slipped letter turns into. Nothing the page markup uses.</summary>
    private const string Slips = "#%*?~^!$";

    private readonly Random _slip = new();

    /// <summary>Reads a page into the book's unease and returns its markup as this copy of the book shows it.</summary>
    private string Unsettle(string markup, GuideEntryPrototype entry)
    {
        _unease = Math.Clamp(_unease + entry.Unease, 0f, 1f);
        UnsettleFrame();
        if (_unease <= 0f)
            return markup;

        var strength = 0.03f + 0.45f * MathF.Pow(_unease, 1.5f);
        var text = Slip(markup, strength);
        return _unease >= WhisperUnease ? Whisper(text) : text;
    }

    /// <summary>Swaps a share of the letters for noise, leaving tags and their attributes alone.</summary>
    private string Slip(string markup, float strength)
    {
        var builder = new StringBuilder(markup.Length);
        var inTag = false;
        var inBracket = false;
        foreach (var c in markup)
        {
            if (c == '<')
                inTag = true;
            else if (c == '[')
                inBracket = true;

            if (!inTag && !inBracket && char.IsLetterOrDigit(c) && _slip.NextDouble() < strength)
                builder.Append(Slips[_slip.Next(Slips.Length)]);
            else
                builder.Append(c);

            if (c == '>')
                inTag = false;
            else if (c == ']')
                inBracket = false;
        }

        return builder.ToString();
    }

    /// <summary>Writes a line or two of the book's own between paragraphs.</summary>
    private string Whisper(string markup)
    {
        var lines = new List<string>(markup.Replace("\r\n", "\n").Split('\n'));
        var count = 1 + (int) (_unease * 3f);
        for (var i = 0; i < count; i++)
        {
            // Only between paragraphs inside the document, never inside a tag's lines.
            var gaps = new List<int>();
            for (var l = 1; l < lines.Count - 1; l++)
            {
                if (lines[l].Trim().Length == 0)
                    gaps.Add(l);
            }

            if (gaps.Count == 0)
                break;

            var line = Loc.GetString($"wf-lore-book-whisper-{_slip.Next(1, WhisperLines + 1)}");
            lines.Insert(gaps[_slip.Next(gaps.Count)], $"  [color=#8a2a36][italic]{line}[/italic][/color]\n");
        }

        return string.Join("\n", lines);
    }

    /// <summary>Deep in, the book's own furniture slips too.</summary>
    private void UnsettleFrame()
    {
        if (_unease < FrameUnease)
            return;

        var strength = (_unease - FrameUnease) / (1f - FrameUnease) * 0.6f + 0.1f;
        BookTitle.Text = Slip(Loc.GetString("wf-lore-book-title"), strength);
        BookSubtitle.Text = Slip(Loc.GetString("wf-lore-book-subtitle"), strength);
        ChapterTrail.Text = Slip(ChapterTrail.Text ?? string.Empty, strength);
        Folio.Text = Slip(Folio.Text ?? string.Empty, strength);
    }
}
