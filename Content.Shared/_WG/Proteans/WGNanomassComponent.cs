using Content.Shared.Alert;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.Proteans;

/// <summary>
/// Spends and refills a Protean's nanomass, which is the charge of its seed's battery: upkeep, healing, slurry,
/// running thin, EMP, absorbing and being fed.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class WGNanomassComponent : Component
{
    /// <summary>Below <see cref="ThinThreshold"/>: slowed, and plates and modules are off.</summary>
    [DataField, AutoNetworkedField]
    public bool Thin;

    /// <summary>Lying as a puddle, which costs a fraction of the upkeep.</summary>
    [DataField, AutoNetworkedField]
    public bool Pooled;

    /// <summary>Mass spent per second while gathered. 0.05 empties a full body in about 33 minutes.</summary>
    [DataField]
    public float Upkeep = 0.05f;

    /// <summary>Upkeep is divided by this while pooled.</summary>
    [DataField]
    public float PooledUpkeepDivisor = 16f;

    /// <summary>Below this much mass the Protean runs thin.</summary>
    [DataField]
    public float ThinThreshold = 5f;

    /// <summary>Walk and sprint multiplier while thin.</summary>
    [DataField]
    public float ThinSpeed = 0.7f;

    /// <summary>Healing runs only above this much mass.</summary>
    [DataField]
    public float HealThreshold = 30f;

    /// <summary>Healing waits this long after the last hit.</summary>
    [DataField]
    public TimeSpan HealDelay = TimeSpan.FromSeconds(30);

    /// <summary>Damage healed per second.</summary>
    [DataField]
    public float HealRate = 1f;

    /// <summary>Mass spent per point of damage healed.</summary>
    [DataField]
    public float HealCost = 0.2f;

    /// <summary>Damage healed at once when someone else feeds the Protean.</summary>
    [DataField]
    public float FedHeal = 20f;

    /// <summary>Mass shed as slurry per point of damage taken.</summary>
    [DataField]
    public float SlurryPerDamage = 0.3f;

    [DataField]
    public EntProtoId SlurryPrototype = "WGProteanSlurry";

    /// <summary>Mass burnt by a heavy EMP.</summary>
    [DataField]
    public float EmpHeavyBurn = 10f;

    /// <summary>Mass burnt by a light EMP.</summary>
    [DataField]
    public float EmpLightBurn = 5f;

    /// <summary>An EMP draining at least this many joules counts as heavy.</summary>
    [DataField]
    public float EmpHeavyEnergy = 1_000_000f;

    /// <summary>How long an EMP knocks the Protean down.</summary>
    [DataField]
    public TimeSpan EmpStagger = TimeSpan.FromSeconds(2);

    [DataField]
    public ProtoId<WGProteanDietPrototype> Diet = "WGProtean";

    /// <summary>Items the Protean refuses to absorb: keys, IDs, money and the like.</summary>
    [DataField]
    public EntityWhitelist? AbsorbBlacklist;

    [DataField]
    public ProtoId<AlertPrototype> MassAlert = "WGProteanMass";

    [DataField]
    public ProtoId<AlertPrototype> ThinAlert = "WGProteanMassLow";

    /// <summary>When the Protean was last hurt; healing waits on it.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastHit;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>Mass the last update left, to tell when it crosses a threshold.</summary>
    [ViewVariables]
    public float LastMass = -1f;
}
