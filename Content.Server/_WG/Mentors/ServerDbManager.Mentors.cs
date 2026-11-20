using System.Threading;
using System.Threading.Tasks;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>A player's roster row, in any state, or null.</summary>
    Task<WolfgateMentor?> GetMentorAsync(Guid userId, CancellationToken cancel = default);

    /// <summary>Every roster row, in any state, oldest grant first.</summary>
    Task<List<WolfgateMentor>> GetMentorsAsync(CancellationToken cancel = default);

    /// <summary>Adds the row or replaces the one with the same user.</summary>
    Task SaveMentorAsync(WolfgateMentor mentor, CancellationToken cancel = default);

    /// <summary>Sets last_active_at. False when the player has no row.</summary>
    Task<bool> SetMentorLastActiveAsync(Guid userId, DateTime when, CancellationToken cancel = default);

    /// <summary>Removes the row and its history. False when there was none.</summary>
    Task<bool> DeleteMentorAsync(Guid userId, CancellationToken cancel = default);

    /// <summary>Stores a new application. Returns its id.</summary>
    Task<int> AddMentorApplicationAsync(WolfgateMentorApplication application, CancellationToken cancel = default);

    /// <summary>Replaces an application's row, as after a review.</summary>
    Task UpdateMentorApplicationAsync(WolfgateMentorApplication application, CancellationToken cancel = default);

    /// <summary>Applications, newest first, filtered by state and player when given.</summary>
    Task<List<WolfgateMentorApplication>> GetMentorApplicationsAsync(
        string? state,
        Guid? userId,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<WolfgateMentor?> GetMentorAsync(Guid userId, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetMentorAsync(userId, cancel));
    }

    public Task<List<WolfgateMentor>> GetMentorsAsync(CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetMentorsAsync(cancel));
    }

    public Task SaveMentorAsync(WolfgateMentor mentor, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SaveMentorAsync(mentor, cancel));
    }

    public Task<bool> SetMentorLastActiveAsync(Guid userId, DateTime when, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetMentorLastActiveAsync(userId, when, cancel));
    }

    public Task<bool> DeleteMentorAsync(Guid userId, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteMentorAsync(userId, cancel));
    }

    public Task<int> AddMentorApplicationAsync(WolfgateMentorApplication application, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddMentorApplicationAsync(application, cancel));
    }

    public Task UpdateMentorApplicationAsync(WolfgateMentorApplication application, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.UpdateMentorApplicationAsync(application, cancel));
    }

    public Task<List<WolfgateMentorApplication>> GetMentorApplicationsAsync(
        string? state,
        Guid? userId,
        CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetMentorApplicationsAsync(state, userId, cancel));
    }
}
