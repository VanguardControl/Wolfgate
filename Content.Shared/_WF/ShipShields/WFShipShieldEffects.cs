namespace Content.Shared._WF.ShipShields;

/// <summary>Pure sampling rules for shield health, cooling and impact waves.</summary>
public static class WFShipShieldEffects
{
    /// <summary>Returns the shield color as health falls from cyan through amber to red.</summary>
    public static Color HealthColor(float health)
    {
        health = float.IsFinite(health) ? Math.Clamp(health, 0f, 1f) : 0f;
        return health > 0.45f
            ? Color.InterpolateBetween(new Color(1f, 0.53f, 0.12f), new Color(0.12f, 0.72f, 1f), (health - 0.45f) / 0.55f)
            : Color.InterpolateBetween(new Color(1f, 0.05f, 0.08f), new Color(1f, 0.53f, 0.12f), health / 0.45f);
    }

    /// <summary>Returns local heat with a three-second cooling time and finite spatial falloff.</summary>
    public static float Heat(float distance, float age, float strength)
    {
        if (age < 0f || age > 8f)
            return 0f;
        return strength * 0.48f * MathF.Exp(-age / 3f) * MathF.Exp(-distance * distance / 14f);
    }

    /// <summary>Returns the main expanding wave and a faint trailing echo.</summary>
    public static float Wave(float distance, float age, float strength)
    {
        if (age < 0f || age > 2.5f)
            return 0f;
        var radius = age * 16f;
        var ring = MathF.Abs(distance - radius);
        var echo = MathF.Abs(distance - MathF.Max(0f, radius - 2f));
        return strength * (1f - age / 2.5f) *
            (MathF.Exp(-ring * ring / 0.32f) + 0.35f * MathF.Exp(-echo * echo / 0.2f));
    }
}
