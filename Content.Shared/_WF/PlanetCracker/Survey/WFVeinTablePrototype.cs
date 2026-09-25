using System.Numerics;
using Content.Shared.Mining;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// The deep-vein loot table one world rolls every vein from, and the numbers the survey console's rating is derived from.
/// </summary>
// Kind named explicitly: Robust would derive "wFVeinTable".
[Prototype("wfVeinTable")]
public sealed partial class WFVeinTablePrototype : IPrototype
{
    /// <summary>Yield range a table falls back to, and the range the shared examine bands against.</summary>
    public static readonly Vector2 DefaultYieldRange = new(1500f, 4000f);

    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Every ore this world's veins can roll, keyed by ore, with its pick weight and its worth.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<OrePrototype>, WFVeinEntry> Ores = new();

    /// <summary>Min and max total yield of a single vein.</summary>
    [DataField]
    public Vector2 YieldRange = DefaultYieldRange;

    /// <summary>Yield and rating multiplier applied when the world is not sanctioned for cracking.</summary>
    [DataField]
    public float UnsanctionedMultiplier = 2f;

    /// <summary>Ore units per minute a crack miner pulls out of a vein; carried here so one document tunes a world.</summary>
    [DataField]
    public float Rate = 150f;
}

/// <summary>One ore's share of a vein table.</summary>
[DataDefinition]
public sealed partial class WFVeinEntry
{
    /// <summary>Relative pick weight against every other entry in the same table.</summary>
    [DataField]
    public float Weight = 1f;

    /// <summary>The table's own per-ore worth, since OrePrototype has no price field.</summary>
    [DataField]
    public float Value = 1f;
}
