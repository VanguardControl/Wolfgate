using System.Threading.Tasks;
using Content.Shared._WG.AHelpTickets;
using Robust.Shared.Network;

namespace Content.Server.Discord.DiscordLink;

/// <summary>One forum thread per ahelp ticket: created once, archived when the ticket closes.</summary>
public sealed partial class DiscordChatLink
{
    private readonly object _wgThreadLock = new();

    /// <summary>Threads being created, so messages sent meanwhile wait for them.</summary>
    private readonly Dictionary<NetUserId, Task<ulong?>> _wgPendingThreads = new();

    /// <summary>
    /// The player's thread: its id, or the creation to wait for. Starts the creation when there is neither, and then
    /// says so with <c>Creating</c>. Messages are sent from the thread pool, so this is locked.
    /// </summary>
    private (ulong ThreadId, Task<ulong?>? Pending, bool Creating) WGThreadFor(NetUserId userId, Func<Task<ulong?>> create)
    {
        lock (_wgThreadLock)
        {
            if (_ahelpThreads.TryGetValue(userId, out var threadId))
                return (threadId, null, false);

            if (_wgPendingThreads.TryGetValue(userId, out var pending))
                return (0, pending, false);

            pending = create();
            _wgPendingThreads[userId] = pending;
            return (0, pending, true);
        }
    }

    private void WGThreadCreated(NetUserId userId, Task<ulong?> pending, ulong? threadId)
    {
        lock (_wgThreadLock)
        {
            // A ticket closed while its thread was being made leaves nothing pending; that thread is not reused.
            if (!_wgPendingThreads.Remove(userId, out var current) || current != pending)
                return;

            if (threadId is { } id)
                _ahelpThreads[userId] = id;
        }
    }

    /// <summary>Renames the player's thread to the closed ticket and archives it; their next ticket gets a new one.</summary>
    public void WGCloseAhelpThread(NetUserId userId, int number, AHelpTicketState state)
    {
        ulong threadId;
        lock (_wgThreadLock)
        {
            _wgPendingThreads.Remove(userId);
            if (!_ahelpThreads.Remove(userId, out threadId))
                return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _discordLink.WGArchiveThreadAsync(threadId, number, state.ToString());
            }
            catch (Exception e)
            {
                _sawmill.Error($"Error while archiving ahelp thread: {e}");
            }
        });
    }
}
