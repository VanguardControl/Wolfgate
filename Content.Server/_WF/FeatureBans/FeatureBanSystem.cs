using Content.Server.Administration.Managers;
using Content.Shared._WF.FeatureBans;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.FeatureBans;

/// <summary>Answers whether a player is banned from a feature, and tells features when a ban lands.</summary>
public sealed class FeatureBanSystem : EntitySystem
{
    [Dependency] private IBanManager _bans = default!;

    /// <summary>Whether a connected player has a live ban from a feature.</summary>
    public bool IsBanned(NetUserId user, ProtoId<WFFeatureBanPrototype> feature)
    {
        return _bans.GetRoleBans(user)?.Contains(WFFeatureBans.Role(feature)) == true;
    }

    /// <summary>Called by the ban manager after a feature ban on a connected player is stored.</summary>
    public void Banned(ICommonSession session, string role)
    {
        if (!role.StartsWith(WFFeatureBans.Prefix, StringComparison.Ordinal))
            return;

        var ev = new FeatureBannedEvent(session, role[WFFeatureBans.Prefix.Length..]);
        RaiseLocalEvent(ref ev);
    }
}

/// <summary>Raised when a connected player is banned from a feature, so it can take back what they have of it.</summary>
[ByRefEvent]
public readonly record struct FeatureBannedEvent(ICommonSession Session, string Feature);
