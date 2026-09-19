namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Throttle state for one body's wound feedback. Ensured on the body the first time it is wounded and
/// never read anywhere else, so the dictionary a per-body throttle would otherwise need cannot outlive
/// the entity.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedWoundSfxComponent : Component
{
    /// <summary>Earliest time the next wound sound may play.</summary>
    [DataField]
    public TimeSpan NextSound;

    /// <summary>Earliest time the next debris effect may spawn.</summary>
    [DataField]
    public TimeSpan NextDebris;

    /// <summary>
    /// When this body last took a hit. A wound that lands or worsens while this is the current tick came
    /// from damage; anything else is an infection, a necrosis clock or a heal, and stays silent.
    /// </summary>
    [DataField]
    public TimeSpan LastHit = TimeSpan.MinValue;
}
