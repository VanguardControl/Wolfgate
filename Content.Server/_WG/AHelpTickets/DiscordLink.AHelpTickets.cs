using System.Threading.Tasks;
using Discord;

namespace Content.Server.Discord.DiscordLink;

public sealed partial class DiscordLink
{
    /// <summary>Puts the ticket number and state in a forum thread's title, then archives and locks it.</summary>
    public async Task WGArchiveThreadAsync(ulong threadId, int number, string state)
    {
        if (_client?.GetChannel(threadId) is not IThreadChannel thread)
            return;

        var prefix = number > 0 ? $"#{number} " : string.Empty;
        var name = $"{prefix}{thread.Name} [{state}]";
        if (name.Length > 100)
            name = name[..100];

        await thread.ModifyAsync(p =>
        {
            p.Name = name;
            p.Archived = true;
            p.Locked = true;
        });
    }
}
