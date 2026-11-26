using Content.Server._WG.AHelpTickets;
using Content.Shared._WG.AHelpTickets;
using Content.Shared._WG.EventStaff;
using Content.Shared.Administration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Systems;

/// <summary>The ahelp side of tickets: feeds every line to <see cref="AHelpTicketSystem"/> and announces closes.</summary>
public sealed partial class BwoinkSystem
{
    [Dependency] private AHelpTicketSystem _wgTickets = default!;

    /// <summary>Hands a line of an ahelp to the ticket system.</summary>
    private void WGTicketLine(BwoinkTextMessage message, NetUserId senderId, string senderName, bool fromWebhook)
    {
        var source = senderId == message.UserId
            ? AHelpLineSource.Player
            : fromWebhook
                ? AHelpLineSource.Discord
                : senderId == SystemUserId
                    ? AHelpLineSource.System
                    : AHelpLineSource.Staff;

        _wgTickets.OnLine(message.UserId, source, senderId, senderName, message.Text, message.AdminOnly);
    }

    /// <summary>Staff who receive ahelps of this category.</summary>
    public IList<INetChannel> WGTargetAdmins(AHelpCategory category)
    {
        return GetTargetAdmins(category);
    }

    /// <summary>Whether this staffer may act on the player's ahelp.</summary>
    public bool WGCanAnswer(ICommonSession staff, NetUserId channel)
    {
        var admin = _adminManager.GetAdminData(staff);
        return (admin?.HasFlag(AdminFlags.Adminhelp) ?? false) || CanEventStaffAnswer(admin, channel);
    }

    /// <summary>Sets whether the player's ahelp counts as answered, which mutes their follow-ups for staff.</summary>
    public void WGSetClaimed(NetUserId channel, bool claimed)
    {
        if (claimed)
            _claimedAHelps.Add(channel);
        else
            _claimedAHelps.Remove(channel);
    }

    /// <summary>Sends staff a line in the player's ahelp, optionally with a bwoink.</summary>
    public void WGStaffLine(NetUserId channel, string text, bool playSound)
    {
        var category = GetAHelpCategory(channel);
        var line = new BwoinkTextMessage(channel, SystemUserId, $"{CategoryTag(category)} {text}", playSound: playSound, category: category);
        foreach (var admin in GetTargetAdmins(category))
        {
            RaiseNetworkEvent(line, admin);
        }

        WgRecordHistory(channel, line, null);
    }

    /// <summary>Tells staff and the player that the player's ticket was closed, and clears the answered mark.</summary>
    public void WGAnnounceClosed(NetUserId channel, string staffName, AHelpTicketState state, int number)
    {
        _claimedAHelps.Remove(channel);

        var category = GetAHelpCategory(channel);
        var staffText = Loc.GetString("wg-ahelp-ticket-closed-staff",
            ("staff", FormattedMessage.EscapeText(staffName)),
            ("number", number),
            ("state", state.ToString()));
        var staffMsg = new BwoinkTextMessage(channel, SystemUserId, $"{CategoryTag(category)} {staffText}", playSound: false, category: category);
        var admins = GetTargetAdmins(category);
        foreach (var admin in admins)
        {
            RaiseNetworkEvent(staffMsg, admin);
        }

        var playerText = Loc.GetString(state switch
        {
            AHelpTicketState.Rejected => "wg-ahelp-ticket-closed-player-rejected",
            AHelpTicketState.IcIssue => "wg-ahelp-ticket-closed-player-ic",
            _ => "wg-bwoink-resolved-player",
        });
        var playerMsg = new BwoinkTextMessage(channel, SystemUserId, playerText, playSound: false);
        if (_playerManager.TryGetSessionById(channel, out var player) && !admins.Contains(player.Channel))
            RaiseNetworkEvent(playerMsg, player.Channel);

        WgRecordHistory(channel, staffMsg, playerMsg);
        _sawmill.Info($"{staffName} closed ticket #{number} of {channel} as {state}");
    }
}
