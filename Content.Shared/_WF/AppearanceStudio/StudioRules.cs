using System.Globalization;
using System.Text;
using Content.Shared._WF.CustomMarkings;
using Content.Shared._WF.FeatureBans;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.AppearanceStudio;

/// <summary>The limits on studio tweaks, and how they are stored.</summary>
public static class StudioRules
{
    /// <summary>The feature ban that takes the studio and custom markings away from a player.</summary>
    public static readonly ProtoId<WFFeatureBanPrototype> Feature = "WFAppearanceStudio";

    /// <summary>South, north, east and west, in the order RSIs and custom marking art use.</summary>
    public const int Facings = 4;

    public const int MaxTweaks = 64;
    public const int MaxKeyLength = 64;

    /// <summary>Hard cap on an offset whatever the cvar says: half a tile past the sprite's edge.</summary>
    public const int OffsetCap = 48;

    public const float ScaleCapMin = 0.25f;
    public const float ScaleCapMax = 4f;

    /// <summary>Scales snap to this step, which keeps stored and networked values exact.</summary>
    public const float ScaleStep = 0.05f;

    /// <summary>The limits a server puts on tweaks.</summary>
    public readonly record struct Limits(int MaxOffset, float MinScale, float MaxScale, bool Warps)
    {
        /// <summary>The hard caps, for data whose server limits aren't known.</summary>
        public static readonly Limits Caps = new(OffsetCap, ScaleCapMin, ScaleCapMax, true);
    }

    public static Limits GetLimits(IConfigurationManager cfg)
    {
        return new Limits(
            cfg.GetCVar(StudioCVars.MaxOffset),
            cfg.GetCVar(StudioCVars.MinScale),
            cfg.GetCVar(StudioCVars.MaxScale),
            cfg.GetCVar(StudioCVars.Warps));
    }

    /// <summary>Keys are prototype ids, slot and layer names: letters, digits, '_', '-' and '.'.</summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
            return false;

        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-' && c != '.')
                return false;
        }

        return true;
    }

    public static bool CanHide(StudioTarget target)
    {
        return target is StudioTarget.Body or StudioTarget.Marking;
    }

    public static bool CanWarp(StudioTarget target)
    {
        return target is StudioTarget.Worn or StudioTarget.Held;
    }

    public static float SnapScale(float scale)
    {
        if (!float.IsFinite(scale))
            return 1f;

        return MathF.Round(scale / ScaleStep) * ScaleStep;
    }

    /// <summary>
    /// Tweaks held to the limits: bad keys and empty tweaks dropped, one per part (the last), offsets and scales
    /// clamped, hiding and warps kept only where they apply. Always a new list of new tweaks.
    /// </summary>
    public static List<StudioTweak> Clean(IEnumerable<StudioTweak?>? tweaks, Limits limits)
    {
        var result = new List<StudioTweak>();
        if (tweaks == null)
            return result;

        var maxOffset = Math.Clamp(limits.MaxOffset, 0, OffsetCap);
        var minScale = Math.Clamp(limits.MinScale, ScaleCapMin, 1f);
        var maxScale = Math.Clamp(limits.MaxScale, 1f, ScaleCapMax);

        foreach (var tweak in tweaks)
        {
            if (tweak == null || !Enum.IsDefined(tweak.Target) || !IsValidKey(tweak.Key))
                continue;

            var clean = new StudioTweak(tweak.Target, tweak.Key);
            for (var i = 0; i < Facings; i++)
            {
                var offset = tweak.Offsets != null && i < tweak.Offsets.Length ? tweak.Offsets[i] : Vector2i.Zero;
                clean.Offsets[i] = new Vector2i(
                    Math.Clamp(offset.X, -maxOffset, maxOffset),
                    Math.Clamp(offset.Y, -maxOffset, maxOffset));
            }

            clean.Scale = Math.Clamp(SnapScale(tweak.Scale), minScale, maxScale);
            clean.Hidden = tweak.Hidden && CanHide(tweak.Target);
            clean.Warp = limits.Warps && CanWarp(tweak.Target) && CustomMarkingRules.IsValidHash(tweak.Warp)
                ? tweak.Warp
                : null;

            result.RemoveAll(t => t.Same(clean.Target, clean.Key));
            if (!clean.IsEmpty())
                result.Add(clean);
        }

        if (result.Count > MaxTweaks)
            result.RemoveRange(MaxTweaks, result.Count - MaxTweaks);

        return result;
    }

    public static StudioTweak? Find(IReadOnlyList<StudioTweak> tweaks, StudioTarget target, string key)
    {
        foreach (var tweak in tweaks)
        {
            if (tweak.Same(target, key))
                return tweak;
        }

        return null;
    }

    /// <summary>A copy of the list with one tweak replaced, added or, when empty, removed.</summary>
    public static List<StudioTweak> With(IReadOnlyList<StudioTweak> tweaks, StudioTweak changed)
    {
        var result = new List<StudioTweak>(tweaks.Count + 1);
        var placed = false;
        foreach (var tweak in tweaks)
        {
            if (!tweak.Same(changed.Target, changed.Key))
            {
                result.Add(tweak);
                continue;
            }

            placed = true;
            if (!changed.IsEmpty())
                result.Add(changed);
        }

        if (!placed && !changed.IsEmpty())
            result.Add(changed);

        return result;
    }

    public static bool SameList(IReadOnlyList<StudioTweak> a, IReadOnlyList<StudioTweak> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The text a profile stores: one tweak per ';', its fields split by '|': target, key, the four offsets as
    /// x,y pairs, the scale in hundredths, 1 when hidden, and the warp hash. Empty for none.
    /// </summary>
    public static string ToStored(IReadOnlyList<StudioTweak> tweaks)
    {
        var text = new StringBuilder();
        foreach (var tweak in tweaks)
        {
            if (text.Length > 0)
                text.Append(';');

            text.Append((int) tweak.Target).Append('|').Append(tweak.Key).Append('|');
            for (var i = 0; i < Facings; i++)
            {
                var offset = tweak.GetOffset(i);
                if (i > 0)
                    text.Append(',');

                text.Append(offset.X).Append(',').Append(offset.Y);
            }

            text.Append('|').Append((int) MathF.Round(tweak.Scale * 100f))
                .Append('|').Append(tweak.Hidden ? '1' : '0')
                .Append('|').Append(tweak.Warp ?? string.Empty);
        }

        return text.ToString();
    }

    /// <summary>Reads what <see cref="ToStored"/> wrote, skipping anything malformed, held to the hard caps.</summary>
    public static List<StudioTweak> FromStored(string? stored)
    {
        var tweaks = new List<StudioTweak>();
        if (string.IsNullOrEmpty(stored))
            return tweaks;

        foreach (var entry in stored.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = entry.Split('|');
            if (fields.Length != 6
                || !byte.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var target)
                || !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var scale))
                continue;

            var numbers = fields[2].Split(',');
            if (numbers.Length != Facings * 2)
                continue;

            var tweak = new StudioTweak((StudioTarget) target, fields[1])
            {
                Scale = scale / 100f,
                Hidden = fields[4] == "1",
                Warp = fields[5].Length == 0 ? null : fields[5],
            };

            var valid = true;
            for (var i = 0; i < Facings && valid; i++)
            {
                valid = int.TryParse(numbers[i * 2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
                        & int.TryParse(numbers[i * 2 + 1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y);
                tweak.Offsets[i] = new Vector2i(x, y);
            }

            if (valid)
                tweaks.Add(tweak);
        }

        return Clean(tweaks, Limits.Caps);
    }

    /// <summary>Every warp hash the tweaks use.</summary>
    public static IEnumerable<string> Warps(IEnumerable<StudioTweak> tweaks)
    {
        foreach (var tweak in tweaks)
        {
            if (tweak.Warp != null)
                yield return tweak.Warp;
        }
    }
}
