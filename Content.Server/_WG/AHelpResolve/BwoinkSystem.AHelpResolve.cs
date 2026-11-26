using Content.Shared._WG.AHelpResolve;
using Content.Shared._WG.AHelpTickets;
using Content.Shared.Administration;
using Robust.Shared.Network;

namespace Content.Server.Administration.Systems;

public sealed partial class BwoinkSystem
{
    /// <summary>
    /// Players whose ahelp a staffer has answered; their messages no longer bwoink staff until it is resolved.
    /// </summary>
    private readonly HashSet<NetUserId> _claimedAHelps = new();

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

    /// <summary>The Resolve button closes the player's ticket (AHelpTickets announces it).</summary>
    private void OnAHelpResolve(AHelpResolveMessage msg, EntitySessionEventArgs args)
    {
        if (!WGCanAnswer(args.SenderSession, msg.Channel))
            return;

        _wgTickets.StaffActionOnPlayer(args.SenderSession, msg.Channel, AHelpTicketAction.Resolve);
    }
}
