// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._WF.Xenonids.HiveLeader;

public sealed class HiveLeaderSystem : EntitySystem
{
    public bool IsLeader(EntityUid leader, [NotNullWhen(true)] out HiveLeaderComponent? leaderComp)
    {
        return TryComp(leader, out leaderComp);
    }

    public void GrantLeader(EntityUid granter, EntityUid target)
    {
    }

    public void RevokeLeader(EntityUid target)
    {
    }
}
