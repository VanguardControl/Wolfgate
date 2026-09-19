using Content.Shared._WF.ShipPa;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>A grid on an orbit layer with no station-keeping left, counting down to atmospheric entry.</summary>
/// <remarks>
/// Server-only and not networked: the console readout rides <see cref="WFConsoleOrbitTargetComponent"/>, which is
/// networked already and is the one thing a client needs. UnsavedComponent for the reason every other round-state
/// component in this feature carries it - a saved map must not come back mid-decay.
/// </remarks>
[RegisterComponent, UnsavedComponent, AutoGenerateComponentPause]
public sealed partial class WFOrbitDecayComponent : Component
{
    /// <summary>
    /// Warning the crew gets between losing station-keeping and the hull dropping out of orbit. Per grid rather than
    /// per system so a test can shorten it, and so a mapper can give a derelict a different one.
    /// </summary>
    [DataField]
    public TimeSpan Grace = TimeSpan.FromSeconds(60);

    /// <summary>When the hull drops out of orbit; stamped from <see cref="Grace"/> as the warning goes out.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan DecayAt;

    /// <summary>Whether the PA warning has gone out, which is also what latches <see cref="DecayAt"/>.</summary>
    [DataField]
    public bool Announced;

    /// <summary>The situation code the ship was on before the warning took it over, handed back if it recovers.</summary>
    [DataField]
    public ProtoId<ShipAlertCodePrototype>? PriorCode;
}
