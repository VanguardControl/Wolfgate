using Content.Shared.Mining;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>
/// A whole asteroid as one rock. A mining beam heats it inside its window until it cracks into smaller rocks and ore;
/// weapons can shatter it for a smaller yield.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFAsteroidComponent : Component
{
    /// <summary>Rock radius in metres; also the radius of the grid's tile disc.</summary>
    [DataField, AutoNetworkedField] public float Radius = 3f;

    [DataField] public float MinMass = 20f;
    [DataField] public float MaxMass = 30f;

    /// <summary>The asteroid grid each fragment spawns as, or null to crack straight into ore.</summary>
    [DataField] public EntProtoId? FragmentPrototype;
    [DataField] public int FragmentCount = 2;

    /// <summary>Share of the mass a clean crack passes on to fragments; the rest drops as ore.</summary>
    [DataField] public float FragmentShare = 0.5f;

    /// <summary>Haulable chunk that part of the ore drops as, for hand mining.</summary>
    [DataField] public EntProtoId? BoulderPrototype;

    /// <summary>Share of the dropped ore that comes out as boulders instead of loose ore.</summary>
    [DataField] public float BoulderShare = 0.25f;
    [DataField] public int MaxBoulders = 6;

    /// <summary>Weapon damage that shatters the rock without a beam.</summary>
    [DataField] public float ShatterDamage = 900f;

    /// <summary>Share of the mass a weapon shatter keeps as ore.</summary>
    [DataField] public float ShatterYield = 0.4f;

    /// <summary>Share of the mass an overcharge blast keeps as ore and fragments.</summary>
    [DataField] public float OverchargeYield = 0.5f;

    /// <summary>Seconds in the window to crack a massless rock, plus <see cref="CrackTimePerMass"/> per unit.</summary>
    [DataField] public float CrackTimeBase = 4f;
    [DataField] public float CrackTimePerMass = 0.1f;

    /// <summary>Added to the composition's resistance; bigger rocks resist more.</summary>
    [DataField] public float ResistanceBonus;

    /// <summary>Number of sprite variants in the rock's RSI.</summary>
    [DataField] public int Variants = 3;

    [AutoNetworkedField] public int Variant;
    [AutoNetworkedField] public Color VeinColor = Color.White;

    /// <summary>Displayed heat, where 1 is the overcharge line.</summary>
    [AutoNetworkedField] public float Charge;
    [AutoNetworkedField] public bool Overcharging;

    /// <summary>Client-side heat, eased toward <see cref="Charge"/>.</summary>
    public float DisplayCharge;

    /// <summary>Ore veins this rock holds, inert rock included.</summary>
    public float Mass;
    public ProtoId<WFAsteroidCompositionPrototype>? CompositionId;
    public List<WFAsteroidOre> Ores = new();
    public float Resistance;
    public float Instability;
    public float WindowCenter = 0.55f;
    public float WindowHalfWidth = 0.1f;
    public float BaseCharge;
    public float Progress;
    public float OverchargeTime;
    public float IncomingPower;
    /// <summary>Widest window scale among the beams on the rock this tick.</summary>
    public float WindowScale = 1f;
    public float NoiseSeed;
    public bool Cracking;
}

/// <summary>One ore in a rock and the share of its mass it makes up.</summary>
public record struct WFAsteroidOre(ProtoId<OrePrototype> Ore, float Fraction, Color Color, float Volatility);

[Serializable, NetSerializable]
public enum WFAsteroidVisualLayers : byte
{
    Rock,
    Veins,
    Heat,
}
