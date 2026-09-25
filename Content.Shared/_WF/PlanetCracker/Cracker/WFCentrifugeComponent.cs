using Robust.Shared.GameStates;

namespace Content.Shared._WF.PlanetCracker.Cracker;

/// <summary>
/// Marks the ship's gravitic centrifuge and carries the spin and load readout its own window draws.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFCentrifugeComponent : Component
{
    /// <summary>Rotor spin as a fraction of full charge, 0 to 1; written server-side at 4 Hz.</summary>
    [DataField, AutoNetworkedField]
    public float Spin;

    /// <summary>True once the rotor counts as at full, with hysteresis applied.</summary>
    [DataField, AutoNetworkedField]
    public bool AtFull;

    /// <summary>Mass the hull's pooled gravgens are currently carrying, including anchor virtual mass.</summary>
    [DataField, AutoNetworkedField]
    public float Load;

    /// <summary>Mass the hull's pooled gravgens can carry.</summary>
    [DataField, AutoNetworkedField]
    public float Capacity;

    /// <summary>Spin fraction at which the rotor starts counting as at full.</summary>
    [DataField]
    public float FullOn = 0.98f;

    /// <summary>Spin fraction it must fall below to stop counting as at full.</summary>
    [DataField]
    public float FullOff = 0.95f;

    /// <summary>The two spin loops, server-side; their volume follows the spin.</summary>
    [ViewVariables]
    public EntityUid? HumStream1;

    [ViewVariables]
    public EntityUid? HumStream2;
}
