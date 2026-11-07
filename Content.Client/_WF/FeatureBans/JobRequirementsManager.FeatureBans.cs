using Content.Shared._WF.FeatureBans;
using Robust.Shared.Prototypes;

namespace Content.Client.Players.PlayTimeTracking;

public sealed partial class JobRequirementsManager
{
    /// <summary>Whether the local player is banned from a feature, from the role bans the server sent.</summary>
    public bool IsFeatureBanned(ProtoId<WFFeatureBanPrototype> feature)
    {
        return _roleBans.Contains(WFFeatureBans.Role(feature));
    }
}
