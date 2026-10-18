// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.Map;

namespace Content.Shared._WF.Xenonids.Eye;

public sealed class QueenEyeSystem : EntitySystem
{
    public bool IsInQueenEye(EntityUid queen)
    {
        return false;
    }

    public bool CanSeeTarget(EntityUid queen, EntityCoordinates target)
    {
        return true;
    }
}
