using System.Numerics;
using System.Text;
using Content.Shared.Guidebook;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WF.LoreBook;

/// <summary>
/// Some pages should not be read too closely. Each page's <see cref="GuideEntry.Unease"/> builds up while this book is
/// open. A little in, letters begin to slip. Further on the page will not hold still: every moment or so it rewrites
/// itself, letters trade places, words turn to glyphs and back, and the book writes lines of its own between the
/// paragraphs. Deep in, even its title and page numbers go wrong. Closing the book and opening it again puts it right.
/// </summary>
public sealed partial class WFLoreBookWindow
{
    /// <summary>How unsettled this copy of the book is, 0 to 1. A new window starts clean.</summary>
    private float _unease;

    /// <summary>Unease past which the open page stops holding still.</summary>
    private const float LiveUnease = 0.15f;

    /// <summary>Unease past which the book writes lines of its own into pages.</summary>
    private const float WhisperUnease = 0.35f;

    /// <summary>Unease past which the title, chapter trail and page numbers go wrong too.</summary>
    private const float FrameUnease = 0.6f;

    private const int WhisperLines = 10;

    /// <summary>What a slipped letter turns into: nothing the page markup reads, and no heading marks.</summary>
    private const string Slips = "%*?~^!$";

    /// <summary>The glyph font words turn into.</summary>
    private const string GlyphFont = "WFLoreGlyphs";

    private readonly Random _slip = new();

    /// <summary>The open page as written, kept so the page can be rewritten while it is read.</summary>
    private string? _raw;

    /// <summary>The chapter trail and page number as written, so each rewrite slips them afresh.</summary>
    private string _trailText = string.Empty;
    private string _folioText = string.Empty;

    private float _nextShift;
    private Vector2? _restoreScroll;

    /// <summary>Unease past which whispers come from under the pages.</summary>
    private const float WhisperSoundUnease = 0.1f;

    /// <summary>The whispers' loudness at the first unease and at full, in decibels: always under everything else.</summary>
    private const float WhisperQuiet = -26f;
    private const float WhisperLoud = -12f;

    private static readonly SoundSpecifier Whispers = new SoundCollectionSpecifier("WFLoreBookWhispers");
    private static readonly SoundSpecifier PageTurn = new SoundCollectionSpecifier("WFLoreBookPageTurn");

    private EntityUid? _whisper;
    private float _nextWhisper = 2f;

    /// <summary>Reads a page into the book's unease and returns its markup as this copy of the book shows it.</summary>
    private string Unsettle(string markup, GuideEntryPrototype entry)
    {
        _unease = Math.Clamp(_unease + entry.Unease, 0f, 1f);
        _raw = markup;
        _trailText = ChapterTrail.Text ?? string.Empty;
        _folioText = Folio.Text ?? string.Empty;
        _nextShift = NextShift();
        UnsettleFrame();
        return Corrupt(markup);
    }

    /// <summary>The page as the book shows it now: slipped letters, traded letters, glyphs and whispers, by unease.</summary>
    private string Corrupt(string markup)
    {
        if (_unease <= 0f)
            return markup;

        var slip = 0.02f + 0.4f * MathF.Pow(_unease, 1.5f);
        var trade = _unease >= LiveUnease ? 0.05f + 0.25f * _unease : 0f;
        var glyph = _unease >= LiveUnease ? 0.02f + 0.3f * _unease * _unease : 0f;
        var text = Distort(_slip, markup, slip, trade, glyph);
        return _unease >= WhisperUnease ? Whisper(text) : text;
    }

    /// <summary>
    /// Goes word by word through the text outside tags: a word may turn to glyphs whole, have two letters trade places,
    /// or lose letters to noise.
    /// </summary>
    public static string Distort(Random rng, string markup, float slip, float trade, float glyph)
    {
        var builder = new StringBuilder(markup.Length + 64);
        var word = new StringBuilder();
        var depth = 0;
        // Headings are plain labels, which would print a glyph tag as text, so their words never turn to glyphs.
        var lineStart = true;
        var heading = false;

        void Flush()
        {
            if (word.Length == 0)
                return;

            var text = word.ToString();
            word.Clear();
            if (!heading && text.Length > 2 && rng.NextDouble() < glyph)
            {
                builder.Append($"[font=\"{GlyphFont}\" size=15]{text}[/font]");
                return;
            }

            var letters = text.ToCharArray();
            if (letters.Length > 3 && rng.NextDouble() < trade)
            {
                var i = rng.Next(letters.Length - 1);
                (letters[i], letters[i + 1]) = (letters[i + 1], letters[i]);
            }

            for (var i = 0; i < letters.Length; i++)
            {
                if (rng.NextDouble() < slip)
                    letters[i] = Slips[rng.Next(Slips.Length)];
            }

            builder.Append(letters);
        }

        foreach (var c in markup)
        {
            if (c == '\n')
            {
                lineStart = true;
                heading = false;
            }
            else if (lineStart && !char.IsWhiteSpace(c))
            {
                lineStart = false;
                heading = c == '#';
            }

            if (c is '<' or '[')
            {
                Flush();
                depth++;
            }

            if (depth == 0 && char.IsLetterOrDigit(c))
            {
                word.Append(c);
                continue;
            }

            Flush();
            builder.Append(c);
            if (c is '>' or ']')
                depth = Math.Max(0, depth - 1);
        }

        Flush();
        return builder.ToString();
    }

    /// <summary>Swaps a share of the letters in plain text for noise.</summary>
    private string Slip(string text, float strength)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsLetterOrDigit(c) && _slip.NextDouble() < strength ? Slips[_slip.Next(Slips.Length)] : c);
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
        ChapterTrail.Text = Slip(_trailText, strength);
        Folio.Text = Slip(_folioText, strength);
    }

    /// <summary>Seconds until the page next rewrites itself: sooner the more unsettled the book is.</summary>
    private float NextShift()
    {
        var calm = Math.Clamp(1f - _unease, 0f, 1f);
        return 0.25f + calm * 1.4f + (float) _slip.NextDouble() * (0.3f + calm);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // A rewrite clears the page, so its scroll is put back once the new page has been laid out.
        if (_restoreScroll is { } scroll)
        {
            PageScroll.SetScrollValue(scroll);
            _restoreScroll = null;
        }

        UpdateWhispers(args.DeltaSeconds);
        if (_raw == null || _unease < LiveUnease)
            return;

        _nextShift -= args.DeltaSeconds;
        if (_nextShift > 0f)
            return;

        _nextShift = NextShift();
        _restoreScroll = PageScroll.GetScrollValue();
        PageBody.RemoveAllChildren();
        _parsing.TryAddMarkup(PageBody, Corrupt(_raw));
        UnsettleFrame();
    }

    /// <summary>Plays a whisper now and then, more often and a little louder the more unsettled the book is.</summary>
    private void UpdateWhispers(float delta)
    {
        if (_unease < WhisperSoundUnease)
            return;

        var audio = _entMan.System<SharedAudioSystem>();
        if (_whisper is { } playing && _entMan.EntityExists(playing))
            return;

        _whisper = null;
        _nextWhisper -= delta;
        if (_nextWhisper > 0f)
            return;

        var calm = 1f - _unease;
        _nextWhisper = 1.5f + calm * 10f + (float) _slip.NextDouble() * (2f + calm * 6f);
        var volume = WhisperQuiet + (WhisperLoud - WhisperQuiet) * _unease;
        _whisper = audio.PlayGlobal(Whispers, Filter.Local(), false, AudioParams.Default.WithVolume(volume))?.Entity;
    }

    /// <summary>A page turning: one of the book's turn sounds, picked at random.</summary>
    private void PlayPageTurn()
    {
        _entMan.System<SharedAudioSystem>().PlayGlobal(PageTurn, Filter.Local(), false, AudioParams.Default.WithVolume(-6f));
    }

        public override void Close()
    {
        if (_whisper is { } playing)
            _entMan.System<SharedAudioSystem>().Stop(playing);

        _whisper = null;
        base.Close();
    }
}
