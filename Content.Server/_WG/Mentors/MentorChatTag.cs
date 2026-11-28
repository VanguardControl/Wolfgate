using Content.Shared.Chat;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WG.Mentors;

/// <summary>Puts the mentor tag before an on-duty mentor's name on OOC and LOOC lines.</summary>
public static class MentorChatTag
{
    /// <summary>The wrapped line with the tag before the first place the name appears, if the speaker is on duty.</summary>
    public static string Apply(WGMentorSystem mentors, ICommonSession player, string name, string wrapped)
    {
        if (!mentors.IsOnDuty(player.UserId) || name.Length == 0)
            return wrapped;

        var index = wrapped.IndexOf(name, StringComparison.Ordinal);
        if (index < 0)
            return wrapped;

        var tag = $"[color={ChatChannelExtensions.MentorColor.ToHex()}]{FormattedMessage.EscapeText(Loc.GetString("wg-mentor-chat-tag"))}[/color] ";
        return wrapped.Insert(index, tag);
    }
}
