using Content.Shared.Alert;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.Proteans;

/// <summary>
/// Tuning and state for a Protean's nanomass. The mass itself is not stored here: it is the charge of the seed
/// organ's battery (<see cref="WGProteanSeedComponent"/>), so it stays with the seed. The server's
/// <c>WGNanomassSystem</c> spends and refills it; <see cref="WGProteanSystem"/> applies the thin slowdown on both sides.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class WGNanomassComponent : Component
{
    /// <summary>
    /// Mass is below <see cref="ThinThreshold"/>. Set by the server and networked so the client predicts the
    /// <see cref="ThinSpeed"/> slowdown. The harness PR also switches plates and modules off while it is set.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Thin;

    /// <summary>
    /// Lying as a puddle, which divides upkeep by <see cref="PooledUpkeepDivisor"/>. Nothing sets it yet; the
    /// forms PR does, and tests set it by hand.
    /// </summary>
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

    /// <summary>Spawned to carry shed mass when no slurry lies on the spot already.</summary>
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

    /// <summary>The materials the Protean can absorb, what each yields and the trace each leaves.</summary>
    [DataField]
    public ProtoId<WGProteanDietPrototype> Diet = "WGProtean";

    /// <summary>
    /// Items refused as owned (<c>wg-protean-absorb-refuse-owned</c>): keys, IDs, money and the like. Paid augments are
    /// refused in code as well.
    /// </summary>
    [DataField]
    public EntityWhitelist? AbsorbBlacklist;

    /// <summary>The nanomass alert, severity 0 to 10 by mass over capacity; hidden while thin.</summary>
    [DataField]
    public ProtoId<AlertPrototype> MassAlert = "WGProteanMass";

    /// <summary>Shown in place of <see cref="MassAlert"/> while thin.</summary>
    [DataField]
    public ProtoId<AlertPrototype> ThinAlert = "WGProteanMassLow";

    /// <summary>When the Protean was last hurt; healing waits <see cref="HealDelay"/> after it.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastHit;

    /// <summary>When the server next runs upkeep and healing.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate;

    /// <summary>How often upkeep and healing run; each run covers the whole interval.</summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
}
