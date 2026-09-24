using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>
/// M4 (plan §3.11): a machine's positronic core temperature. The core soaks up the chassis's heat and the coolant
/// pump takes it off; over wolfmed.ipc_core_heat_k the core loses health and the chassis is in thermal shutdown,
/// Dying with cause CoreHeat, until the core is back under wolfmed.ipc_core_heat_wake_k. Ensured on every
/// mechanical wound host by the server's overheat system, which also makes it the machine marker shared code reads.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedCoreHeatComponent : Component
{
    /// <summary>Core temperature in kelvin. Dirtied when it moves by a whole kelvin.</summary>
    [AutoNetworkedField]
    public float CoreTemperature = 310.15f;

    /// <summary>Core or chassis over wolfmed.ipc_core_heat_warn_k: "smoking, too hot to touch".</summary>
    [AutoNetworkedField]
    public bool Hot;

    /// <summary>Thermal shutdown: the core got past its line and has not cooled back under the wake line.</summary>
    [AutoNetworkedField]
    public bool ThermalShutdown;

    /// <summary>Server: when this thermal shutdown began.</summary>
    [ViewVariables]
    public TimeSpan ThermalShutdownStart;

    /// <summary>Server: the chassis temperature at the last tick, for the analyzer.</summary>
    [ViewVariables]
    public float ChassisTemperature = 310.15f;
}
