namespace Content.Client._WF.CombatConsole;

/// <summary>Maps finite telemetry onto an instrument scale without turning missing readings into zero.</summary>
public static class WFGaugeScale
{
    /// <summary>Returns a bounded needle position, or no needle for missing data and invalid scales.</summary>
    public static float? Fraction(double? value, double minimum, double maximum)
    {
        if (value is not { } number || !double.IsFinite(number) || !double.IsFinite(minimum) ||
            !double.IsFinite(maximum) || !double.IsFinite(maximum - minimum) || maximum <= minimum)
            return null;
        return (float) Math.Clamp((number - minimum) / (maximum - minimum), 0, 1);
    }

    /// <summary>Chooses a labelled 1/2/5 scale for quantities whose capacity is not supplied by telemetry.</summary>
    public static double Ceiling(double value, double floor = 10)
    {
        if (!double.IsFinite(value) || value <= floor)
            return floor;
        var decade = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var steps = value / decade;
        return (steps <= 1 ? 1 : steps <= 2 ? 2 : steps <= 5 ? 5 : 10) * decade;
    }
}
