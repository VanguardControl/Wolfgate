using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WG.AHelpTickets;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>Stores a new ticket and returns its number.</summary>
    Task<int> AddAhelpTicketAsync(WolfgateAhelpTicket ticket, CancellationToken cancel = default);

    /// <summary>Writes a stored ticket's state, assignee, times and macros.</summary>
    Task UpdateAhelpTicketAsync(WolfgateAhelpTicket ticket, CancellationToken cancel = default);

    /// <summary>Stores lines of tickets that already have a number.</summary>
    Task AddAhelpMessagesAsync(List<WolfgateAhelpMessage> messages, CancellationToken cancel = default);

    /// <summary>How many tickets a player has had, and the most recent ones, newest first.</summary>
    Task<(int Total, List<AHelpTicketHistoryEntry> Recent)> GetAhelpTicketsForPlayerAsync(Guid player, int limit, CancellationToken cancel = default);

    /// <summary>Tickets whose lines, player name or character name contain the text, newest first.</summary>
    Task<List<AHelpTicketHistoryEntry>> SearchAhelpTicketsAsync(string text, int limit, CancellationToken cancel = default);

    /// <summary>A stored ticket and every line of it, oldest first.</summary>
    Task<(AHelpTicketHistoryEntry? Ticket, List<AHelpTicketLine> Lines)> GetAhelpTicketTranscriptAsync(int number, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<int> AddAhelpTicketAsync(WolfgateAhelpTicket ticket, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddAhelpTicketAsync(ticket, cancel));
    }

    public Task UpdateAhelpTicketAsync(WolfgateAhelpTicket ticket, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.UpdateAhelpTicketAsync(ticket, cancel));
    }

    public Task AddAhelpMessagesAsync(List<WolfgateAhelpMessage> messages, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddAhelpMessagesAsync(messages, cancel));
    }

    public Task<(int Total, List<AHelpTicketHistoryEntry> Recent)> GetAhelpTicketsForPlayerAsync(Guid player, int limit, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetAhelpTicketsForPlayerAsync(player, limit, cancel));
    }

    public Task<List<AHelpTicketHistoryEntry>> SearchAhelpTicketsAsync(string text, int limit, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.SearchAhelpTicketsAsync(text, limit, cancel));
    }

    public Task<(AHelpTicketHistoryEntry? Ticket, List<AHelpTicketLine> Lines)> GetAhelpTicketTranscriptAsync(int number, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetAhelpTicketTranscriptAsync(number, cancel));
    }
}
