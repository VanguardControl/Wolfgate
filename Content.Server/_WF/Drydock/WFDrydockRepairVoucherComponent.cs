using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>A voucher that pays for one full repair at a drydock that takes credits, used up when the work starts.</summary>
[RegisterComponent]
public sealed partial class WFDrydockRepairVoucherComponent : Component
{
    /// <summary>What is handed back if the job aborts before finishing.</summary>
    [DataField]
    public EntProtoId? Returned;
}
