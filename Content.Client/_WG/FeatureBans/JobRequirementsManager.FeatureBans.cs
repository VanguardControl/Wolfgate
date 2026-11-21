using Content.Shared._WG.FeatureBans;
using Robust.Shared.Prototypes;

namespace Content.Client.Players.PlayTimeTracking;

public sealed partial class JobRequirementsManager
{
    /// <summary>Whether the local player is banned from a feature, from the role bans the server sent.</summary>
    public bool IsFeatureBanned(ProtoId<WGFeatureBanPrototype> feature)
    {
        return _roleBans.Contains(WGFeatureBans.Role(feature));
    }
}
