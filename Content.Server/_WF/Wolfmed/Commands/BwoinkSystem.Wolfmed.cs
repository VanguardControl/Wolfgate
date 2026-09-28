using Robust.Shared.Player;

namespace Content.Server.Administration.Systems;

/// <summary>
/// TEMPORARY (playtest 5, Wolfmed): the healmeimbroken ticket goes down the ordinary ahelp path, the Discord relay
/// included. Delete with <c>Content.Server/_WF/Wolfmed/Commands/WolfmedBugRescueSystem.cs</c>.
/// </summary>
public sealed partial class BwoinkSystem
{
    /// <summary>Files <paramref name="text"/> as an ahelp from the player, in their own channel, as if they had typed it.</summary>
    public void SendPlayerBwoink(ICommonSession session, string text)
    {
        var message = new BwoinkTextMessage(session.UserId, session.UserId, text);
        OnBwoinkInternal(message, session.UserId, _adminManager.GetAdminData(session), session.Name, session.Channel,
            userOnly: false, sendWebhook: true, fromWebhook: false);
    }
}
