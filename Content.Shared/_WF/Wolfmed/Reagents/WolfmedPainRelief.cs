using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Reagents;

/// <summary>
/// Gives a reagent a painkiller tier and a strength in pain units. Refreshed every metabolism tick, so the
/// dose lasts <see cref="Duration"/> past the last tick and then lapses on its own.
/// </summary>
/// <remarks>
/// This is the whole per-reagent side of the painkiller model: the tier says what the drug is allowed to
/// lift, the strength says how much pain it discounts, and sedation is the price a strong one charges.
/// </remarks>
public sealed partial class WolfmedPainRelief : EntityEffect
{
    /// <summary>What this drug may lift. See <see cref="WolfmedPainReliefTier"/>.</summary>
    [DataField(required: true)]
    public WolfmedPainReliefTier Tier;

    /// <summary>Pain units discounted while the dose is active.</summary>
    [DataField]
    public float Strength;

    /// <summary>
    /// How long one application lasts. For an emergency pen this is the whole window, taken once; for every
    /// other tier it is a rolling refresh, so it only has to outlast the metabolism tick.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(4);

    /// <summary>Sedation accumulated per second while the dose is active. 0 to 1 is the whole meter.</summary>
    [DataField]
    public float Sedation;

    /// <summary>Dose key. Defaults to the reagent id, so one reagent never stacks with itself.</summary>
    [DataField]
    public string Identifier = string.Empty;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wolfmed-pain-relief",
            ("chance", Probability),
            ("tier", Loc.GetString($"wolfmed-pain-relief-tier-{Tier.ToString().ToLowerInvariant()}")),
            ("strength", Strength));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var key = Identifier;
        if (key.Length == 0 && args is EntityEffectReagentArgs { Reagent: { } reagent })
            key = reagent.ID;

        if (key.Length == 0)
            key = Tier.ToString();

        args.EntityManager.System<WolfmedPainReliefSystem>()
            .AddDose(args.TargetEntity, key, Tier, Strength, Duration, Sedation);
    }
}
