using Content.Shared._WG.EventStaff;
using Robust.Shared.Network;

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
}
