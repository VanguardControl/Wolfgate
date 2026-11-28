using Content.Shared._WG.AHelpResolve;
using Content.Shared.Administration;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Systems;

public sealed partial class BwoinkSystem
{
    /// <summary>
    /// Players whose ahelp a staffer has answered; their messages no longer bwoink staff until it is resolved.
    /// </summary>
    private readonly HashSet<NetUserId> _claimedAHelps = new();

    /// <summary>The line that last resolved each player's ahelp, so lines before it count as answered.</summary>
    private readonly Dictionary<NetUserId, BwoinkTextMessage> _wgResolvedLine = new();

    /// <summary>
    /// Whether a player has an ahelp that staff have answered and not resolved.
    /// </summary>
    public bool IsAHelpClaimed(NetUserId channel)
    {
        return _claimedAHelps.Contains(channel);
    }

    /// <summary>
    /// Whether staff hear a bwoink for this message. A staff reply claims the ahelp and stays quiet for other staff;
    /// a player message bwoinks only while no staffer has answered.
    /// </summary>
    private bool AHelpStaffSound(NetUserId channel, NetUserId senderId, bool playSound)
    {
        if (senderId != channel)
        {
            _claimedAHelps.Add(channel);
            return false;
        }

        return playSound && !_claimedAHelps.Contains(channel);
    }

    private void OnAHelpResolve(AHelpResolveMessage msg, EntitySessionEventArgs args)
    {
        var admin = _adminManager.GetAdminData(args.SenderSession);
        if (!(admin?.HasFlag(AdminFlags.Adminhelp) ?? false) && !CanEventStaffAnswer(admin, msg.Channel))
            return;

        ResolveAHelp(msg.Channel, args.SenderSession.Name);
    }

    /// <summary>Resolves a player's ahelp and tells them and staff who resolved it.</summary>
    public void ResolveAHelp(NetUserId playerChannel, string adminName)
    {
        _claimedAHelps.Remove(playerChannel);

        var category = GetAHelpCategory(playerChannel);
        var adminText = Loc.GetString("wg-bwoink-resolved-admin", ("admin", FormattedMessage.EscapeText(adminName)));
        var adminMsg = new BwoinkTextMessage(playerChannel, SystemUserId, $"{CategoryTag(category)} {adminText}", playSound: false, category: category);
        var admins = GetTargetAdmins(category);
        foreach (var channel in admins)
        {
            RaiseNetworkEvent(adminMsg, channel);
        }

        var playerMsg = new BwoinkTextMessage(playerChannel, SystemUserId, Loc.GetString("wg-bwoink-resolved-player"), playSound: false);
        if (_playerManager.TryGetSessionById(playerChannel, out var player) && !admins.Contains(player.Channel))
            RaiseNetworkEvent(playerMsg, player.Channel);

        // Kept with the ahelp, so staff who connect later see it was resolved instead of an open ticket.
        WgRecordHistory(playerChannel, adminMsg, playerMsg);
        _wgResolvedLine[playerChannel] = adminMsg;

        _sawmill.Info($"{adminName} resolved the ahelp of {playerChannel}");
    }
}
