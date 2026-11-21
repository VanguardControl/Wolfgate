using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

/// <summary>A closed ticket as the leaderboard counts it: who held it, how it ended, and everyone who answered.</summary>
public sealed record MentorBoardTicket(
    Guid? ClaimedBy,
    string CloseReason,
    DateTime OpenedAt,
    DateTime? FirstAnswerAt,
    int? Rating,
    List<Guid> Responders);

// The mentor roster and applications.
public abstract partial class ServerDbBase
{
    public async Task<WolfgateMentor?> GetMentorAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var mentor = await db.DbContext.WolfgateMentor
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.UserId == userId, cancel);

        return mentor == null ? null : NormalizeTimes(mentor);
    }

    public async Task<List<WolfgateMentor>> GetMentorsAsync(CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var mentors = await db.DbContext.WolfgateMentor
            .AsNoTracking()
            .OrderBy(m => m.GrantedAt)
            .ToListAsync(cancel);

        return mentors.Select(NormalizeTimes).ToList();
    }

    public async Task SaveMentorAsync(WolfgateMentor mentor, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        if (await db.DbContext.WolfgateMentor.AnyAsync(m => m.UserId == mentor.UserId, cancel))
            db.DbContext.WolfgateMentor.Update(mentor);
        else
            db.DbContext.WolfgateMentor.Add(mentor);

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<bool> SetMentorLastActiveAsync(Guid userId, DateTime when, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        return await db.DbContext.WolfgateMentor
            .Where(m => m.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.LastActiveAt, when), cancel) > 0;
    }

    public async Task<bool> DeleteMentorAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        return await db.DbContext.WolfgateMentor.Where(m => m.UserId == userId).ExecuteDeleteAsync(cancel) > 0;
    }

    public async Task<int> AddMentorApplicationAsync(WolfgateMentorApplication application, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        db.DbContext.WolfgateMentorApplication.Add(application);
        await db.DbContext.SaveChangesAsync(cancel);
        return application.Id;
    }

    public async Task UpdateMentorApplicationAsync(WolfgateMentorApplication application, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        db.DbContext.WolfgateMentorApplication.Update(application);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<List<WolfgateMentorApplication>> GetMentorApplicationsAsync(
        string? state,
        Guid? userId,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var applications = await db.DbContext.WolfgateMentorApplication
            .AsNoTracking()
            .Where(a => state == null || a.State == state)
            .Where(a => userId == null || a.UserId == userId)
            .OrderByDescending(a => a.Id)
            .ToListAsync(cancel);

        return applications.Select(NormalizeTimes).ToList();
    }

    public async Task<(int Resolved, int Rated, int Helpful)> GetMentorCreditAsync(Guid mentor, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var ratings = await db.DbContext.WolfgateMentorTicket
            .AsNoTracking()
            .Where(t => t.ClaimedBy == mentor && t.ClosedAt != null)
            .Where(t => t.CloseReason == "resolved" || (t.CloseReason == "player" && t.FirstAnswerAt != null))
            .Select(t => t.Rating)
            .ToListAsync(cancel);

        return (ratings.Count, ratings.Count(r => r != null), ratings.Count(r => r > 0));
    }

    public async Task<List<MentorBoardTicket>> GetMentorBoardTicketsAsync(DateTime? since, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var tickets = await db.DbContext.WolfgateMentorTicket
            .AsNoTracking()
            .Where(t => t.ClosedAt != null && (since == null || t.ClosedAt >= since))
            .Select(t => new
            {
                t.ClaimedBy,
                t.CloseReason,
                t.OpenedAt,
                t.FirstAnswerAt,
                t.Rating,
                Responders = t.Messages
                    .Where(m => m.SenderRole == "mentor" || m.SenderRole == "staff")
                    .Select(m => m.SenderUserId)
                    .ToList(),
            })
            .ToListAsync(cancel);

        return tickets.Select(t => new MentorBoardTicket(
                t.ClaimedBy,
                t.CloseReason ?? "",
                NormalizeDatabaseTime(t.OpenedAt),
                NormalizeDatabaseTime(t.FirstAnswerAt),
                t.Rating,
                t.Responders.OfType<Guid>().Distinct().ToList()))
            .ToList();
    }

    private WolfgateMentor NormalizeTimes(WolfgateMentor mentor)
    {
        mentor.GrantedAt = NormalizeDatabaseTime(mentor.GrantedAt);
        mentor.LastActiveAt = NormalizeDatabaseTime(mentor.LastActiveAt);
        mentor.PromotedAt = NormalizeDatabaseTime(mentor.PromotedAt);
        return mentor;
    }

    private WolfgateMentorApplication NormalizeTimes(WolfgateMentorApplication application)
    {
        application.CreatedAt = NormalizeDatabaseTime(application.CreatedAt);
        application.ReviewedAt = NormalizeDatabaseTime(application.ReviewedAt);
        application.ReapplyAfter = NormalizeDatabaseTime(application.ReapplyAfter);
        return application;
    }
}
