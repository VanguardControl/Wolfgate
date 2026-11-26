using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WG.AHelpTickets;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

// Ahelp tickets and their transcripts.
public abstract partial class ServerDbBase
{
    public async Task<int> AddAhelpTicketAsync(WolfgateAhelpTicket ticket, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        db.DbContext.WolfgateAhelpTicket.Add(ticket);
        await db.DbContext.SaveChangesAsync(cancel);
        return ticket.Id;
    }

    public async Task UpdateAhelpTicketAsync(WolfgateAhelpTicket ticket, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        await db.DbContext.WolfgateAhelpTicket
            .Where(t => t.Id == ticket.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.State, ticket.State)
                .SetProperty(t => t.CharacterName, ticket.CharacterName)
                .SetProperty(t => t.AssigneeUserId, ticket.AssigneeUserId)
                .SetProperty(t => t.AssigneeName, ticket.AssigneeName)
                .SetProperty(t => t.ClaimedAt, ticket.ClaimedAt)
                .SetProperty(t => t.FirstReplyAt, ticket.FirstReplyAt)
                .SetProperty(t => t.ClosedAt, ticket.ClosedAt)
                .SetProperty(t => t.ClosedBy, ticket.ClosedBy)
                .SetProperty(t => t.Macros, ticket.Macros), cancel);
    }

    public async Task AddAhelpMessagesAsync(List<WolfgateAhelpMessage> messages, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        db.DbContext.WolfgateAhelpMessage.AddRange(messages);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<(int Total, List<AHelpTicketHistoryEntry> Recent)> GetAhelpTicketsForPlayerAsync(Guid player, int limit, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var total = await db.DbContext.WolfgateAhelpTicket.CountAsync(t => t.PlayerUserId == player, cancel);
        var recent = await HistoryEntries(db.DbContext.WolfgateAhelpTicket
                .Where(t => t.PlayerUserId == player)
                .OrderByDescending(t => t.Id)
                .Take(limit), cancel);

        return (total, recent);
    }

    public async Task<List<AHelpTicketHistoryEntry>> SearchAhelpTicketsAsync(string text, int limit, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var needle = text.Trim().ToLower();
        var ids = db.DbContext.WolfgateAhelpMessage
            .Where(m => m.Text.ToLower().Contains(needle))
            .Select(m => m.TicketId);

        return await HistoryEntries(db.DbContext.WolfgateAhelpTicket
                .Where(t => ids.Contains(t.Id) || t.PlayerName.ToLower().Contains(needle) || (t.CharacterName != null && t.CharacterName.ToLower().Contains(needle)))
                .OrderByDescending(t => t.Id)
                .Take(limit), cancel);
    }

    public async Task<(AHelpTicketHistoryEntry? Ticket, List<AHelpTicketLine> Lines)> GetAhelpTicketTranscriptAsync(int number, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var entry = (await HistoryEntries(db.DbContext.WolfgateAhelpTicket.Where(t => t.Id == number), cancel)).FirstOrDefault();
        if (entry == null)
            return (null, new List<AHelpTicketLine>());

        var lines = await db.DbContext.WolfgateAhelpMessage
            .Where(m => m.TicketId == number)
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .Select(m => new { m.SentAt, m.AuthorName, m.Source, m.AdminOnly, m.Text })
            .ToListAsync(cancel);

        return (entry, lines
            .Select(m => new AHelpTicketLine(NormalizeDatabaseTime(m.SentAt), m.AuthorName, m.Source, m.AdminOnly, m.Text))
            .ToList());
    }

    /// <summary>Tickets as list entries, each with its first player line as the snippet.</summary>
    private async Task<List<AHelpTicketHistoryEntry>> HistoryEntries(IQueryable<WolfgateAhelpTicket> tickets, CancellationToken cancel)
    {
        var rows = await tickets
            .Select(t => new
            {
                t.Id,
                t.RoundId,
                t.ServerName,
                t.PlayerName,
                t.OpenedAt,
                t.State,
                t.AssigneeName,
                Snippet = t.Messages
                    .Where(m => m.Source == "player")
                    .OrderBy(m => m.Id)
                    .Select(m => m.Text)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancel);

        return rows
            .Select(r => new AHelpTicketHistoryEntry(
                r.Id,
                r.RoundId,
                r.ServerName,
                r.PlayerName,
                NormalizeDatabaseTime(r.OpenedAt),
                Enum.TryParse<AHelpTicketState>(r.State, out var state) ? state : AHelpTicketState.Open,
                r.AssigneeName,
                Shorten(r.Snippet ?? string.Empty)))
            .ToList();
    }

    private static string Shorten(string text)
    {
        return text.Length <= 140 ? text : text[..140] + "…";
    }
}
