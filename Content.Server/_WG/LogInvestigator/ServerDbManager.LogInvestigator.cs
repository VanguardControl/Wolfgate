using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WG.LogInvestigator;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>
    /// A page of a round's logs matching the query, newest or oldest first by id, after <paramref name="afterId"/>.
    /// With <paramref name="count"/> also how many match, up to <see cref="ServerDbBase.InvestigatorCountCap"/>.
    /// With <paramref name="newer"/>, the logs newer than <paramref name="afterId"/> instead, newest first.
    /// </summary>
    Task<(List<LogRow> Rows, bool HasMore, int? Total)> GetInvestigatorLogsAsync(
        LogQuery query, DateTime? roundStart, int? afterId, int limit, bool count, CancellationToken cancel = default, bool newer = false);

    /// <summary>Stores samples of where players were.</summary>
    Task AddPlayerPositionsAsync(List<WolfgatePlayerPosition> positions, CancellationToken cancel = default);

    /// <summary>Where players were in a round between two moments (UTC), oldest first.</summary>
    Task<List<WolfgatePlayerPosition>> GetPlayerPositionsAsync(int roundId, DateTime from, DateTime to, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<(List<LogRow> Rows, bool HasMore, int? Total)> GetInvestigatorLogsAsync(
        LogQuery query, DateTime? roundStart, int? afterId, int limit, bool count, CancellationToken cancel = default, bool newer = false)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetInvestigatorLogsAsync(query, roundStart, afterId, limit, count, cancel, newer));
    }

    public Task AddPlayerPositionsAsync(List<WolfgatePlayerPosition> positions, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddPlayerPositionsAsync(positions, cancel));
    }

    public Task<List<WolfgatePlayerPosition>> GetPlayerPositionsAsync(int roundId, DateTime from, DateTime to, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetPlayerPositionsAsync(roundId, from, to, cancel));
    }
}
