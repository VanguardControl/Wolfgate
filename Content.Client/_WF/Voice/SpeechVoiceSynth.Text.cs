namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    // Pauses, in syllable slots.
    private const float WordGap = 0.6f;
    private const float CommaGap = 3f;
    private const float SentenceGap = 6f;

    private static readonly string[] Ones =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    };

    private static readonly string[] Tens =
    {
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
    };

    /// <summary>The syllables for a message: phrases split at punctuation, numbers said in full.</summary>
    private static List<Blip> Blips(string text)
    {
        var blips = new List<Blip>();
        var phraseStart = 0;
        var wordStart = -1;
        var lastWordStart = 0;
        var letters = 0;
        var upper = 0;
        var wordIndex = 0;

        void AddGap(float length)
        {
            if (blips.Count == 0)
                return;

            // Only a plain gap between words is hummed. A punctuation pause is silent.
            if (blips[^1].Gap)
            {
                var gap = blips[^1];
                gap.Length = MathF.Max(gap.Length, length);
                gap.Hum = gap.Length <= WordGap;
                blips[^1] = gap;
                return;
            }

            blips.Add(new Blip { Gap = true, Length = length, Hum = length <= WordGap });
        }

        void Add(Blip blip)
        {
            if (wordStart < 0)
                wordStart = blips.Count;

            blip.Word = wordIndex;
            blips.Add(blip);
        }

        // A word of two or more letters, all capitals, is shouted.
        void EndWord()
        {
            if (wordStart < 0)
                return;

            if (letters >= 2 && upper == letters)
            {
                for (var i = wordStart; i < blips.Count; i++)
                {
                    var blip = blips[i];
                    blip.Shout = true;
                    blip.Gain = 1.4f;
                    blip.Lift = 1.15f;
                    blips[i] = blip;
                }
            }

            lastWordStart = wordStart;
            wordStart = -1;
            letters = upper = 0;
            wordIndex++;
            AddGap(WordGap);
        }

        void EndPhrase(char ending, float gap)
        {
            EndWord();

            var end = blips.Count > 0 && blips[^1].Gap ? blips.Count - 1 : blips.Count;
            if (end <= phraseStart)
            {
                AddGap(gap);
                return;
            }

            for (var i = phraseStart; i < end; i++)
            {
                var blip = blips[i];
                blip.PhrasePosition = end - phraseStart > 1 ? (i - phraseStart) / (float) (end - phraseStart - 1) : 0f;
                blip.Ending = ending;
                blip.Last = i == end - 1;
                blips[i] = blip;
            }

            if (ending == '…')
            {
                var count = end - lastWordStart;
                for (var i = lastWordStart; i < end; i++)
                {
                    var blip = blips[i];
                    blip.Length = 1.3f;
                    blip.Gain *= 1f - 0.6f * (i - lastWordStart + 1) / count;
                    blips[i] = blip;
                }
            }

            AddGap(gap);
            phraseStart = blips.Count;
        }

        var index = 0;
        while (index < text.Length && blips.Count < MaxBlips)
        {
            var c = text[index];

            if (char.IsAsciiLetter(c))
            {
                var end = index;
                while (end < text.Length && char.IsAsciiLetter(text[end]))
                {
                    end++;
                }

                foreach (var syllable in Syllables(text[index..end]))
                {
                    Add(syllable);
                }

                for (var i = index; i < end; i++)
                {
                    letters++;
                    if (char.IsAsciiLetterUpper(text[i]))
                        upper++;
                }

                index = end;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                EndWord();
                var end = index;
                while (end < text.Length && char.IsAsciiDigit(text[end]))
                {
                    end++;
                }

                foreach (var word in NumberToWords(text[index..end]))
                {
                    foreach (var syllable in NumberWords[word])
                    {
                        Add(syllable);
                    }

                    EndWord();
                }

                index = end;
                continue;
            }

            switch (c)
            {
                case ',':
                case ';':
                case ':':
                    EndPhrase(',', CommaGap);
                    break;
                case '?':
                    EndPhrase('?', SentenceGap);
                    break;
                case '!':
                    EndPhrase('!', SentenceGap);
                    break;
                case '…':
                    EndPhrase('…', SentenceGap);
                    break;
                case '.':
                    if (index + 2 < text.Length && text[index + 1] == '.' && text[index + 2] == '.')
                    {
                        EndPhrase('…', SentenceGap);
                        index += 2;
                    }
                    else
                    {
                        EndPhrase('.', SentenceGap);
                    }

                    break;
                default:
                    EndWord();
                    break;
            }

            index++;
        }

        EndPhrase('.', 0f);

        if (blips.Count > MaxBlips)
            blips.RemoveRange(MaxBlips, blips.Count - MaxBlips);

        while (blips.Count > 0 && blips[^1].Gap)
        {
            blips.RemoveAt(blips.Count - 1);
        }

        return blips;
    }

    /// <summary>Splits a word into consonant-plus-vowel syllables ("he-lo"), with a silent final e dropped.</summary>
    // Each vowel takes the consonant right before it as its onset. Other consonants in a cluster, and those after the
    // last vowel, play alone and short ("cat" is ka + t).
    private static List<Blip> Syllables(string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.Length > 2 && lower[^1] == 'e' && !IsVowelLetter(lower[^2]) && HasVowel(lower[..^2]))
            lower = lower[..^1];

        var syllables = new List<Blip>();
        var pending = new List<Consonant>();

        void Alone(Consonant consonant)
        {
            var blip = Syllable(consonant, Uh);
            blip.ConsonantOnly = true;
            blip.Length = 0.5f;
            syllables.Add(blip);
        }

        var i = 0;
        while (i < lower.Length)
        {
            var c = lower[i];
            var pair = i + 1 < lower.Length ? lower.Substring(i, 2) : string.Empty;

            // A y after a consonant is a vowel, at the start or after a vowel it is a consonant.
            var vowel = IsVowelLetter(c) || c == 'y' && i > 0 && !IsVowelLetter(lower[i - 1]);
            if (vowel)
            {
                var isPair = VowelPairs.TryGetValue(pair, out var paired);
                var sound = isPair ? paired : Vowels[c];
                i += isPair ? 2 : 1;

                for (var k = 0; k < pending.Count - 1; k++)
                {
                    Alone(pending[k]);
                }

                var onset = pending.Count > 0 ? pending[^1] : default;
                syllables.Add(Syllable(onset, sound.Start, sound.End));
                pending.Clear();
                continue;
            }

            if (ConsonantPairs.TryGetValue(pair, out var consonantPair))
            {
                pending.Add(consonantPair);
                i += 2;
                continue;
            }

            // gh is silent, as in "right".
            if (pair == "gh")
            {
                i += 2;
                continue;
            }

            switch (c)
            {
                case 'c':
                    pending.Add(i + 1 < lower.Length && lower[i + 1] is 'e' or 'i' or 'y' ? S : K);
                    break;
                case 'x':
                    pending.Add(K);
                    pending.Add(S);
                    break;
                default:
                    pending.Add(Consonants[c]);
                    break;
            }

            // A doubled consonant is one sound.
            i++;
            if (i < lower.Length && lower[i] == c)
                i++;
        }

        foreach (var consonant in pending)
        {
            Alone(consonant);
        }

        return syllables;
    }

    private static bool IsVowelLetter(char c)
    {
        return c is 'a' or 'e' or 'i' or 'o' or 'u';
    }

    private static bool HasVowel(string text)
    {
        foreach (var c in text)
        {
            if (IsVowelLetter(c))
                return true;
        }

        return false;
    }

    /// <summary>A run of digits as English number words. Long runs and leading zeros are read digit by digit.</summary>
    private static List<string> NumberToWords(string digits)
    {
        var words = new List<string>();

        if (digits.Length > 6 || digits.Length > 1 && digits[0] == '0')
        {
            foreach (var digit in digits)
            {
                words.Add(Ones[digit - '0']);
            }

            return words;
        }

        var number = int.Parse(digits);
        if (number == 0)
        {
            words.Add(Ones[0]);
            return words;
        }

        if (number >= 1000)
        {
            BelowThousand(number / 1000, words);
            words.Add("thousand");
            number %= 1000;
        }

        if (number > 0)
            BelowThousand(number, words);

        return words;
    }

    /// <summary>Adds the words for a number under a thousand.</summary>
    private static void BelowThousand(int number, List<string> words)
    {
        if (number >= 100)
        {
            words.Add(Ones[number / 100]);
            words.Add("hundred");
            number %= 100;
        }

        if (number == 0)
            return;

        if (number < 20)
        {
            words.Add(Ones[number]);
            return;
        }

        words.Add(Tens[number / 10]);
        if (number % 10 > 0)
            words.Add(Ones[number % 10]);
    }
}
