using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

// Mentor Help tickets, their messages and the canned answers.
public abstract partial class ServerDbBase
{
    public async Task<int> AddMentorTicketAsync(WolfgateMentorTicket ticket, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        db.DbContext.WolfgateMentorTicket.Add(ticket);
        await db.DbContext.SaveChangesAsync(cancel);
        return ticket.Id;
    }

    public async Task<bool> UpdateMentorTicketAsync(WolfgateMentorTicket ticket, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        return await db.DbContext.WolfgateMentorTicket
            .Where(t => t.Id == ticket.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.CharacterName, ticket.CharacterName)
                .SetProperty(t => t.Topic, ticket.Topic)
                .SetProperty(t => t.SecretRole, ticket.SecretRole)
                .SetProperty(t => t.Newbie, ticket.Newbie)
                .SetProperty(t => t.FirstAnswerAt, ticket.FirstAnswerAt)
                .SetProperty(t => t.ClaimedBy, ticket.ClaimedBy)
                .SetProperty(t => t.ClosedAt, ticket.ClosedAt)
                .SetProperty(t => t.CloseReason, ticket.CloseReason)
                .SetProperty(t => t.Rating, ticket.Rating)
                .SetProperty(t => t.RatingComment, ticket.RatingComment), cancel) > 0;
    }

    public async Task<int> AddMentorTicketMessageAsync(WolfgateMentorTicketMessage message, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        db.DbContext.WolfgateMentorTicketMessage.Add(message);
        await db.DbContext.SaveChangesAsync(cancel);
        return message.Id;
    }

    public async Task<WolfgateMentorTicket?> GetMentorTicketAsync(int id, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var ticket = await db.DbContext.WolfgateMentorTicket
            .AsNoTracking()
            .Include(t => t.Messages.OrderBy(m => m.Id))
            .SingleOrDefaultAsync(t => t.Id == id, cancel);

        if (ticket == null)
            return null;

        foreach (var message in ticket.Messages)
        {
            message.SentAt = NormalizeDatabaseTime(message.SentAt);
        }

        return NormalizeTimes(ticket);
    }

    public async Task<List<WolfgateMentorTicket>> GetMentorTicketsAsync(
        Guid? playerId,
        Guid? mentorId,
        DateTime? since,
        int limit,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var tickets = await db.DbContext.WolfgateMentorTicket
            .AsNoTracking()
            .Where(t => playerId == null || t.PlayerUserId == playerId)
            .Where(t => mentorId == null
                        || t.ClaimedBy == mentorId
                        || t.Messages.Any(m => m.SenderUserId == mentorId))
            .Where(t => since == null || t.OpenedAt >= since)
            .OrderByDescending(t => t.Id)
            .Take(limit)
            .ToListAsync(cancel);

        return tickets.Select(NormalizeTimes).ToList();
    }

    public async Task<int> ClearMentorTranscriptsAsync(DateTime closedBefore, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var cleared = await db.DbContext.WolfgateMentorTicketMessage
            .Where(m => m.Text != "" && m.Ticket.ClosedAt != null && m.Ticket.ClosedAt < closedBefore)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Text, ""), cancel);

        await db.DbContext.WolfgateMentorTicket
            .Where(t => t.RatingComment != null && t.ClosedAt != null && t.ClosedAt < closedBefore)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RatingComment, (string?) null), cancel);

        return cleared;
    }

    public async Task<List<WolfgateMentorCanned>> GetMentorCannedAsync(CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var canned = await db.DbContext.WolfgateMentorCanned
            .AsNoTracking()
            .OrderBy(c => c.Title)
            .ToListAsync(cancel);

        foreach (var answer in canned)
        {
            answer.UpdatedAt = NormalizeDatabaseTime(answer.UpdatedAt);
        }

        return canned;
    }

    public async Task<int> SaveMentorCannedAsync(WolfgateMentorCanned canned, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        if (canned.Id == 0)
            db.DbContext.WolfgateMentorCanned.Add(canned);
        else
            db.DbContext.WolfgateMentorCanned.Update(canned);

        await db.DbContext.SaveChangesAsync(cancel);
        return canned.Id;
    }

    public async Task<bool> DeleteMentorCannedAsync(int id, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        return await db.DbContext.WolfgateMentorCanned.Where(c => c.Id == id).ExecuteDeleteAsync(cancel) > 0;
    }

    private WolfgateMentorTicket NormalizeTimes(WolfgateMentorTicket ticket)
    {
        ticket.OpenedAt = NormalizeDatabaseTime(ticket.OpenedAt);
        ticket.FirstAnswerAt = NormalizeDatabaseTime(ticket.FirstAnswerAt);
        ticket.ClosedAt = NormalizeDatabaseTime(ticket.ClosedAt);
        return ticket;
    }
}
