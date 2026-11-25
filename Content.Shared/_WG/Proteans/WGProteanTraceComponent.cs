namespace Content.Shared._WG.Proteans;

/// <summary>On a trace status effect: how it bends the Protean's nanomass and damage while it lasts.</summary>
[RegisterComponent]
public sealed partial class WGProteanTraceComponent : Component
{
    /// <summary>Multiplies upkeep.</summary>
    [DataField]
    public float Upkeep = 1f;

    /// <summary>Multiplies the healing rate.</summary>
    [DataField]
    public float Heal = 1f;

    /// <summary>Multiplies all damage taken, easing the species' fragility.</summary>
    [DataField]
    public float Damage = 1f;

    /// <summary>Multiplies Heat damage taken.</summary>
    [DataField]
    public float Heat = 1f;

    /// <summary>Gives the Protean a faint light of this colour while it lasts, if it has none of its own.</summary>
    [DataField]
    public Color? Glow;

    [DataField]
    public float GlowRadius = 1.5f;
}
