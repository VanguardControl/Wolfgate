namespace Content.Shared._WF.ShipShields;

/// <summary>Pure sampling rules for shield health, cooling and impact waves.</summary>
public static class WFShipShieldEffects
{
    /// <summary>Returns the default health indicator colour.</summary>
    public static Color HealthColor(float health) => HealthColor(health, new Color(0.12f, 0.72f, 1f));

    /// <summary>Blends the generator colour through amber to red as shield health falls.</summary>
    public static Color HealthColor(float health, Color generatorColor)
    {
        health = float.IsFinite(health) ? Math.Clamp(health, 0f, 1f) : 0f;
        return health > 0.45f
            ? Color.InterpolateBetween(new Color(1f, 0.53f, 0.12f), generatorColor.WithAlpha(1f), (health - 0.45f) / 0.55f)
            : Color.InterpolateBetween(new Color(1f, 0.05f, 0.08f), new Color(1f, 0.53f, 0.12f), health / 0.45f);
    }

    /// <summary>Returns local heat with a three-second cooling time and finite spatial falloff.</summary>
    public static float Heat(float distance, float age, float strength)
    {
        if (age < 0f || age > 8f)
            return 0f;
        return strength * 0.48f * MathF.Exp(-age / 3f) * MathF.Exp(-distance * distance / 14f) *
            Math.Clamp((8f - age) / 2f, 0f, 1f);
    }

    /// <summary>Briefly reveals nearby hexes before the traveling ripple leaves the impact.</summary>
    public static float Flash(float distance, float age, float strength)
    {
        if (age < 0f || age >= 0.65f)
            return 0f;
        var fade = 1f - age / 0.65f;
        return strength * fade * fade * MathF.Exp(-distance * distance / 10f);
    }

    /// <summary>Keeps the surface health colour readable while impacts tint and brighten the hexes.</summary>
    public static (Color Tint, float Surface, float Hexes, Color SurfaceTint) Appearance(Color health, float heat, float wave, float flash, float integrity = 1f)
    {
        heat = Math.Clamp(heat, 0f, 1.5f);
        wave = Math.Clamp(wave, 0f, 1.5f);
        flash = Math.Clamp(flash, 0f, 1f);
        var damage = 1f - Math.Clamp(float.IsFinite(integrity) ? integrity : 0f, 0f, 1f);
        var hot = Math.Clamp((heat - 0.12f) * 1.1f, 0f, 1f);
        var tint = Color.InterpolateBetween(health, new Color(1f, 0.025f, 0.06f), hot);
        tint = Color.InterpolateBetween(tint, new Color(0.7f, 0.92f, 1f), Math.Clamp(flash * 0.8f + wave * 0.6f, 0f, 0.85f));
        return (Color.FromSrgb(tint),
            0.14f + damage * 0.38f + flash * 1.25f + wave * 0.9f + heat * 0.32f,
            0.38f + damage * 0.62f + flash * 3.2f + wave * 2.4f + heat * 1.1f,
            Color.FromSrgb(health));
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
