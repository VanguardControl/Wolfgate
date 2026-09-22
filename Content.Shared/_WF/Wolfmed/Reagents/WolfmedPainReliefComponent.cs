using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Reagents;

/// <summary>
/// What a painkiller is allowed to do to consciousness. Ordered: a body reports its strongest active tier.
/// </summary>
public enum WolfmedPainReliefTier : byte
{
    None = 0,

    /// <summary>Subtracts pain. Can lift Downed, never Unconscious.</summary>
    Weak = 1,

    /// <summary>
    /// Subtracts more, counts against the unconscious threshold too, masks the wound slowdowns and sedates.
    /// </summary>
    Strong = 2,

    /// <summary>Lifts Downed outright however bad the pain is, and nothing else. No crash.</summary>
    Stimulant = 3,

    /// <summary>Lifts Downed and Unconscious outright for its window, then crashes.</summary>
    Emergency = 4,
}

/// <summary>One reagent's contribution. Doses stack across reagents and are keyed by reagent id.</summary>
public record struct WolfmedPainReliefDose(
    WolfmedPainReliefTier Tier,
    float Strength,
    float SedationPerSecond,
    TimeSpan Ends);

/// <summary>
/// Painkillers in a body. Written by the <c>WolfmedPainRelief</c> metabolism effect, read by consciousness
/// (how much pain to discount and whether a tier may lift a state) and by the analyzer.
/// </summary>
/// <remarks>
/// The tuning below is the whole-body side of the model; the per-reagent side (tier, strength, sedation) is
/// on the reagent's own effect. Listing this component on a mob prototype overrides the tuning for it.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedPainReliefComponent : Component
{
    [ViewVariables]
    public Dictionary<string, WolfmedPainReliefDose> Doses = new();

    /// <summary>Strongest tier currently active. What the analyzer names.</summary>
    [AutoNetworkedField]
    public WolfmedPainReliefTier Tier;

    /// <summary>Pain units discounted from the Downed test (every tier).</summary>
    [AutoNetworkedField]
    public float Relief;

    /// <summary>Pain units discounted from the Unconscious test (Strong and up only).</summary>
    [AutoNetworkedField]
    public float StrongRelief;

    /// <summary>When the longest-lasting dose runs out. What the analyzer counts down.</summary>
    [AutoNetworkedField]
    public TimeSpan? Ends;

    /// <summary>End of an emergency window, while which pain decides nothing at all.</summary>
    [AutoNetworkedField]
    public TimeSpan? EmergencyEnds;

    /// <summary>End of the crash that follows an emergency window. Downed for its duration.</summary>
    [AutoNetworkedField]
    public TimeSpan? CrashEnds;

    /// <summary>0 to 1. Slows reactions, and past <see cref="SedationAirlossThreshold"/> depresses breathing.</summary>
    [AutoNetworkedField]
    public float Sedation;

    /// <summary>Sedation lost per second with no strong painkiller in the body.</summary>
    [DataField]
    public float SedationDecayPerSecond = 0.035f;

    /// <summary>Movement speed multiplier at full sedation.</summary>
    [DataField]
    public float SedationSlowdown = 0.55f;

    /// <summary>Sedation past which breathing is depressed.</summary>
    [DataField]
    public float SedationAirlossThreshold = 0.6f;

    /// <summary>
    /// Respiratory depression, applied per second past the threshold and scaled by how far past it. A seam:
    /// BRAIN replaces this with oxygenation pushed through SetExternalPressure("sedation", ...).
    /// </summary>
    [DataField]
    public DamageSpecifier SedationDamage = new()
    {
        DamageDict = { ["Asphyxiation"] = 0.8 },
    };

    /// <summary>Pain is multiplied by this when an emergency window ends.</summary>
    [DataField]
    public float CrashPainMultiplier = 1.3f;

    /// <summary>How long the crash holds the body Downed, unless the other inputs say worse.</summary>
    [DataField]
    public TimeSpan CrashDuration = TimeSpan.FromSeconds(10);
}
