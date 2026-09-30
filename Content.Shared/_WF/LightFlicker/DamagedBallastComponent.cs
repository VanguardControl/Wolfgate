using Content.Shared.Tools;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.LightFlicker;

/// <summary>
/// A light fixture with a damaged ballast. Clients flicker it like a failing tube until a multitool repairs it.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(LightBallastSystem))]
public sealed partial class DamagedBallastComponent : Component
{
    /// <summary>Seconds a repair takes.</summary>
    [DataField]
    public float RepairTime = 2f;

    /// <summary>Tool quality that repairs the ballast.</summary>
    [DataField]
    public ProtoId<ToolQualityPrototype> RepairQuality = "Pulsing";
}
