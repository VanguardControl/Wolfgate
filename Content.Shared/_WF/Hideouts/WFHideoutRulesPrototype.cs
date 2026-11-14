using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Hideouts;

/// <summary>The hideout's own numbers, in one prototype so they are tuned in YAML.</summary>
[Prototype("wfHideoutRules")]
public sealed partial class WFHideoutRulesPrototype : IPrototype
{
    public const string Default = "WFHideoutRules";

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// What a hideout costs to land each round, as a share of what its parts are worth, in place of the outpost load
    /// markup: the upkeep of a home.
    /// </summary>
    [DataField]
    public float HomeMarkup = 0.1f;

    /// <summary>What moving an outpost's home world costs.</summary>
    [DataField]
    public int MoveFee = 250000;

    /// <summary>What founding an outpost from the registry at Central costs, in credits only.</summary>
    [DataField]
    public int FoundPrice = 15000;

    /// <summary>What the registry's drop down to a standing outpost costs.</summary>
    [DataField]
    public int RideFee = 2500;

    /// <summary>Floor tiles a hideout may have past an outpost's own cap.</summary>
    [DataField]
    public int Tiles;

    /// <summary>Objects a hideout may hold past an outpost's own cap.</summary>
    [DataField]
    public int Entities;

    /// <summary>Tiles a hideout may spread past an outpost's own span.</summary>
    [DataField]
    public int Span;

    /// <summary>Cryopods a hideout may hold past an outpost's own limit.</summary>
    [DataField]
    public int Pods;

    /// <summary>What a company's hideout gets on top: floor, objects and cryopods.</summary>
    [DataField]
    public int CompanyTiles;

    [DataField]
    public int CompanyEntities;

    [DataField]
    public int CompanyPods;

    /// <summary>The share of a hangar ship's value an insurance policy on it costs.</summary>
    [DataField]
    public float InsurancePremium = 0.2f;

    /// <summary>The share of the insured value a claim costs.</summary>
    [DataField]
    public float InsuranceDeductible = 0.15f;

    /// <summary>Storage points a hideout has for safes; only what a safe within them holds is saved.</summary>
    [DataField]
    public int StashPoints = 10;
}
