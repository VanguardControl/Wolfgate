using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>
/// Marks the ship's gravitic centrifuge so F4 finds it without a gravity generator query, and carries the spin and
/// load readout the dial draws.
/// These four values travel on two channels on purpose. The machine's own window is opened by a player standing at the
/// machine, which guarantees the entity is in PVS, so that window reads these networked fields; the crack console may
/// sit far away on a capital hull, so it takes the same four values out of WFCrackConsoleState instead. The same system
/// writes both in the same place, and using the same source in both windows keeps one dial rather than two.
/// The fields are fractions of PowerChargeComponent.MaxCharge, never absolute charge.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFCentrifugeComponent : Component
{
    /// <summary>Rotor spin as a fraction of full charge, 0 to 1; written server-side at 4 Hz.</summary>
    [DataField, AutoNetworkedField]
    public float Spin;

    /// <summary>True once the rotor counts as at full, with the design D25 hysteresis applied.</summary>
    [DataField, AutoNetworkedField]
    public bool AtFull;

    /// <summary>Mass the hull's pooled gravgens are currently carrying, including the D11 virtual mass.</summary>
    [DataField, AutoNetworkedField]
    public float Load;

    /// <summary>Mass the hull's pooled gravgens can carry.</summary>
    [DataField, AutoNetworkedField]
    public float Capacity;

    /// <summary>Spin fraction at which the rotor starts counting as at full (design D25).</summary>
    [DataField]
    public float FullOn = 0.98f;

    /// <summary>Spin fraction it must fall below to stop counting as at full (design D25).</summary>
    [DataField]
    public float FullOff = 0.95f;

    /// <summary>The two spin loops, server-side; their volume follows the spin.</summary>
    [ViewVariables]
    public EntityUid? HumStream1;

    [ViewVariables]
    public EntityUid? HumStream2;
}
