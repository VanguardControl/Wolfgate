using System.Threading;
using System.Threading.Tasks;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>Stores a new ticket without messages. Returns its id.</summary>
    Task<int> AddMentorTicketAsync(WolfgateMentorTicket ticket, CancellationToken cancel = default);

    /// <summary>Writes a ticket's state (claim, close, rating). Its messages are left alone. False when there is none.</summary>
    Task<bool> UpdateMentorTicketAsync(WolfgateMentorTicket ticket, CancellationToken cancel = default);

    /// <summary>Stores one message of a ticket. Returns its id.</summary>
    Task<int> AddMentorTicketMessageAsync(WolfgateMentorTicketMessage message, CancellationToken cancel = default);

    /// <summary>One ticket with its messages in order, or null.</summary>
    Task<WolfgateMentorTicket?> GetMentorTicketAsync(int id, CancellationToken cancel = default);

    /// <summary>
    /// Tickets without messages, newest first: of a player, of a mentor (claimed or posted in) and opened since a time,
    /// each filter applied when given.
    /// </summary>
    Task<List<WolfgateMentorTicket>> GetMentorTicketsAsync(
        Guid? playerId,
        Guid? mentorId,
        DateTime? since,
        int limit,
        CancellationToken cancel = default);

    /// <summary>
    /// Clears the message text and rating comments of tickets closed before a time; the rows stay for the numbers.
    /// Returns how many messages were cleared.
    /// </summary>
    Task<int> ClearMentorTranscriptsAsync(DateTime closedBefore, CancellationToken cancel = default);

    /// <summary>The canned answers, by title.</summary>
    Task<List<WolfgateMentorCanned>> GetMentorCannedAsync(CancellationToken cancel = default);

    /// <summary>Adds a canned answer (id 0) or replaces its row. Returns its id.</summary>
    Task<int> SaveMentorCannedAsync(WolfgateMentorCanned canned, CancellationToken cancel = default);

    /// <summary>Deletes a canned answer. False when there was none.</summary>
    Task<bool> DeleteMentorCannedAsync(int id, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<int> AddMentorTicketAsync(WolfgateMentorTicket ticket, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddMentorTicketAsync(ticket, cancel));
    }

    public Task<bool> UpdateMentorTicketAsync(WolfgateMentorTicket ticket, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.UpdateMentorTicketAsync(ticket, cancel));
    }

    public Task<int> AddMentorTicketMessageAsync(WolfgateMentorTicketMessage message, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddMentorTicketMessageAsync(message, cancel));
    }

    public Task<WolfgateMentorTicket?> GetMentorTicketAsync(int id, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetMentorTicketAsync(id, cancel));
    }

    public Task<List<WolfgateMentorTicket>> GetMentorTicketsAsync(
        Guid? playerId,
        Guid? mentorId,
        DateTime? since,
        int limit,
        CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetMentorTicketsAsync(playerId, mentorId, since, limit, cancel));
    }

    public Task<int> ClearMentorTranscriptsAsync(DateTime closedBefore, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.ClearMentorTranscriptsAsync(closedBefore, cancel));
    }

    public Task<List<WolfgateMentorCanned>> GetMentorCannedAsync(CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetMentorCannedAsync(cancel));
    }

    public Task<int> SaveMentorCannedAsync(WolfgateMentorCanned canned, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SaveMentorCannedAsync(canned, cancel));
    }

    public Task<bool> DeleteMentorCannedAsync(int id, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteMentorCannedAsync(id, cancel));
    }
}
