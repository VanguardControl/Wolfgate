using System.Linq;
using Content.Server._WG.Mentors;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Content.Shared.Database;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Managers;

internal sealed partial class ChatManager
{
    /// <summary>
    /// Sends a message to active mentors and to staff with Adminhelp or Event. Mentors never see admin chat; this is
    /// the other way round.
    /// </summary>
    private void SendMentorChat(ICommonSession player, string message)
    {
        var mentors = _entityManager.System<WGMentorSystem>();
        if (!mentors.IsMentor(player.UserId) && !IsMentorChatStaff(player))
        {
            _adminLogger.Add(LogType.Chat, LogImpact.Extreme, $"{player:Player} attempted to send mentor chat without being a mentor");
            return;
        }

        var clients = mentors.ActiveSessions()
            .Concat(_adminManager.ActiveAdmins.Where(IsMentorChatStaff))
            .Distinct()
            .Select(p => p.Channel);

        var wrappedMessage = Loc.GetString("wg-mentor-chat-wrap",
            ("name", player.Name),
            ("message", FormattedMessage.EscapeText(message)));

        ChatMessageToMany(ChatChannel.MentorChat, message, wrappedMessage, default, false, false, clients, author: player.UserId);
        _adminLogger.Add(LogType.Chat, $"Mentor chat from {player:Player}: {message}");
    }

    private bool IsMentorChatStaff(ICommonSession session)
    {
        return _adminManager.HasAdminFlag(session, AdminFlags.Adminhelp) || _adminManager.HasAdminFlag(session, AdminFlags.Event);
    }
}
