using System.Linq;
using Content.Shared._WG.EventStaff;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Systems;

public sealed partial class BwoinkSystem
{
    /// <summary>
    /// Puts text into a player's ahelp as if they had written it, under a category: a Mentor Help ticket that went
    /// to staff.
    /// </summary>
    public void SendFromMentorHelp(NetUserId player, string playerName, INetChannel? channel, string text, AHelpCategory category)
    {
        _ahelpCategories[player] = category;
        var message = new BwoinkTextMessage(player, player, text, category: category);
        OnBwoinkInternal(message, player, null, playerName, channel, false, true, false);
    }

    /// <summary>
    /// Shows staff a Mentor Help ticket in a player's ahelp, with a sound, under a category and a ticket tag; kept in
    /// the round's ahelp history and copied to the ahelp webhook. The player doesn't see it.
    /// </summary>
    public void RelayMentorTicket(NetUserId player, AHelpCategory category, string tag, string text)
    {
        _ahelpCategories[player] = category;
        _claimedAHelps.Remove(player);

        var staffText = $"{CategoryTag(category)} {FormattedMessage.EscapeText(tag)} {FormattedMessage.EscapeText(text)}";
        var staffMsg = new BwoinkTextMessage(player, SystemUserId, staffText, playSound: true, category: category);
        foreach (var channel in GetTargetAdmins(category))
        {
            RaiseNetworkEvent(staffMsg, channel);
        }

        WgRecordHistory(player, staffMsg, null);
        LogBwoink(staffMsg);

        if (_webhookUrl == string.Empty)
            return;

        var queue = _messageQueues.GetOrNew(player);
        var messageParams = new AHelpMessageParams(
            tag,
            FormattedMessage.EscapeText($"{CategoryTag(category)} {text}"),
            true,
            _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss"),
            _gameTicker.RunLevel,
            playedSound: true,
            noReceivers: GetNonAfkAdmins().Count == 0);
        queue.Enqueue(GenerateAHelpMessage(messageParams));
    }

    /// <summary>A system line in a player's ahelp: one text for staff, another (or none) for the player.</summary>
    public void PostAHelpNote(NetUserId player, string staffText, string? playerText)
    {
        var category = GetAHelpCategory(player);
        var staffMsg = new BwoinkTextMessage(player, SystemUserId, $"{CategoryTag(category)} {staffText}", playSound: false, category: category);
        var admins = GetTargetAdmins(category);
        foreach (var channel in admins)
        {
            RaiseNetworkEvent(staffMsg, channel);
        }

        BwoinkTextMessage? playerMsg = null;
        if (playerText != null)
        {
            playerMsg = new BwoinkTextMessage(player, SystemUserId, playerText, playSound: false);
            if (_playerManager.TryGetSessionById(player, out var session) && !admins.Contains(session.Channel))
                RaiseNetworkEvent(playerMsg, session.Channel);
        }

        WgRecordHistory(player, staffMsg, playerMsg);
    }

    /// <summary>
    /// The text of the latest line a player wrote in their open ahelp, or null. Lines from before the ahelp was last
    /// resolved don't count.
    /// </summary>
    public string? LastPlayerAHelpText(NetUserId player)
    {
        if (!_wgHistory.TryGetValue(player, out var lines))
            return null;

        _wgResolvedLine.TryGetValue(player, out var resolved);
        BwoinkTextMessage? last = null;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var staff = lines[i].Staff;
            if (resolved != null && ReferenceEquals(staff, resolved))
                break;

            if (staff.TrueSender == player)
            {
                last = staff;
                break;
            }
        }

        if (last == null)
            return null;

        // Staff lines read "(TAG) name: text"; the text is everything after the first ": ".
        var plain = FormattedMessage.RemoveMarkupPermissive(last.Text);
        var split = plain.IndexOf(": ", StringComparison.Ordinal);
        return split < 0 ? plain.Trim() : plain[(split + 2)..].Trim();
    }
}
