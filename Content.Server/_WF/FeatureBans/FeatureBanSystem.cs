using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared._WF.FeatureBans;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.FeatureBans;

/// <summary>Answers whether a player is banned from a feature, and tells features when a ban lands.</summary>
public sealed class FeatureBanSystem : EntitySystem
{
    [Dependency] private IBanManager _bans = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>
    /// Whether a CONNECTED player has a live ban from a feature, from the ban manager's cache. An offline account
    /// always reads as not banned here; use <see cref="IsBannedAsync"/> where the player may be offline.
    /// </summary>
    public bool IsBanned(NetUserId user, ProtoId<WFFeatureBanPrototype> feature)
    {
        return _bans.GetRoleBans(user)?.Contains(WFFeatureBans.Role(feature)) == true;
    }

    /// <summary>
    /// Whether an account has a live ban from a feature, connected or not: the ban manager's cache for a connected
    /// player, the database otherwise (the web creator edits offline accounts).
    /// </summary>
    public async Task<bool> IsBannedAsync(NetUserId user, ProtoId<WFFeatureBanPrototype> feature)
    {
        if (_players.TryGetSessionById(user, out _) && _bans.GetRoleBans(user) is { } cached)
            return cached.Contains(WFFeatureBans.Role(feature));

        var role = WFFeatureBans.Role(feature);
        var bans = await _db.GetServerRoleBansAsync(null, user, null, null, includeUnbanned: false);
        var now = DateTimeOffset.Now;
        return bans.Any(ban => ban.Role == role && (ban.ExpirationTime == null || ban.ExpirationTime > now));
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
