namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>
/// The analyzer's line after a successful shock (M1a, plan §7.1). One place for the wording, so the panel
/// and the tests read the same text.
/// </summary>
public static class WolfmedPostShockText
{
    /// <summary>
    /// "Transfuse ≈ N u within S s to stop the heart stopping again; ≈ M u to L% to stop the brain injury",
    /// or only the second half once the grace has run out.
    /// </summary>
    public static string Format(float bloodFraction, float units, float safeUnits, float graceSeconds,
        float safeLine)
    {
        var percent = MathF.Round(bloodFraction * 100f);
        var safe = MathF.Ceiling(safeUnits);
        var line = MathF.Round(safeLine);

        if (graceSeconds <= 0f)
        {
            return Loc.GetString("wolfmed-analyzer-post-shock-late",
                ("percent", percent), ("safe", safe), ("line", line));
        }

        return Loc.GetString("wolfmed-analyzer-post-shock",
            ("percent", percent),
            ("units", MathF.Ceiling(units)),
            ("seconds", MathF.Ceiling(graceSeconds)),
            ("safe", safe),
            ("line", line));
    }
}
