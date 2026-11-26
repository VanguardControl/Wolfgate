using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WG.LogInvestigator;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

// The log investigator's queries, and the player position samples behind "who was nearby".
public abstract partial class ServerDbBase
{
    /// <summary>The most a total is counted to; past it the investigator shows "this many or more".</summary>
    public const int InvestigatorCountCap = 100_000;

    public async Task<(List<LogRow> Rows, bool HasMore, int? Total)> GetInvestigatorLogsAsync(
        LogQuery query, DateTime? roundStart, int? afterId, int limit, bool count, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var logs = InvestigatorQuery(db.DbContext, query, roundStart);
        int? total = count ? await logs.Take(InvestigatorCountCap).CountAsync(cancel) : null;

        if (afterId is { } after)
            logs = query.Ascending ? logs.Where(l => l.Id > after) : logs.Where(l => l.Id < after);

        logs = query.Ascending ? logs.OrderBy(l => l.Id) : logs.OrderByDescending(l => l.Id);

        var rows = await logs
            .Take(limit + 1)
            .Select(l => new { l.Id, l.Type, l.Impact, l.Date, l.Message, l.Json })
            .ToListAsync(cancel);

        var hasMore = rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var result = rows
            .Select(l => new LogRow(l.Id, l.Type, l.Impact, NormalizeDatabaseTime(l.Date), l.Message, LogRefs(l.Json), LogDamage(l.Json)))
            .ToList();

        return (result, hasMore, total);
    }

    public async Task AddPlayerPositionsAsync(List<WolfgatePlayerPosition> positions, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        db.DbContext.WolfgatePlayerPosition.AddRange(positions);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<List<WolfgatePlayerPosition>> GetPlayerPositionsAsync(int roundId, DateTime from, DateTime to, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var rows = await db.DbContext.WolfgatePlayerPosition
            .AsNoTracking()
            .Where(p => p.RoundId == roundId && p.SampledAt >= from && p.SampledAt <= to)
            .OrderBy(p => p.SampledAt)
            .ToListAsync(cancel);

        foreach (var row in rows)
        {
            row.SampledAt = NormalizeDatabaseTime(row.SampledAt);
        }

        return rows;
    }

    private IQueryable<AdminLog> InvestigatorQuery(ServerDbContext db, LogQuery query, DateTime? roundStart)
    {
        var logs = db.AdminLog.Where(l => l.RoundId == query.RoundId);

        if (roundStart is { } start)
        {
            if (query.FromSeconds is { } fromSeconds)
            {
                var from = start.AddSeconds(fromSeconds);
                logs = logs.Where(l => l.Date >= from);
            }

            if (query.ToSeconds is { } toSeconds)
            {
                var to = start.AddSeconds(toSeconds);
                logs = logs.Where(l => l.Date <= to);
            }
        }

        if (query.Types != null)
        {
            var types = query.Types.ToList();
            logs = logs.Where(l => types.Contains(l.Type));
        }

        if (query.Impacts != null)
        {
            var impacts = query.Impacts.ToList();
            logs = logs.Where(l => impacts.Contains(l.Impact));
        }

        if (query.Players.Length > 0)
        {
            if (query.AllPlayers)
            {
                foreach (var player in query.Players)
                {
                    var id = player;
                    logs = logs.Where(l => l.Players.Any(p => p.PlayerUserId == id));
                }
            }
            else
            {
                var ids = query.Players.ToList();
                logs = logs.Where(l => l.Players.Any(p => ids.Contains(p.PlayerUserId)));
            }
        }
        else if (!query.IncludeNonPlayers)
        {
            logs = logs.Where(l => l.Players.Count != 0);
        }

        if (query.Entities.Length > 0)
            logs = logs.Where(MentionsAny(query.Entities));

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim().ToLower();
            logs = logs.Where(l => l.Message.ToLower().Contains(text));
        }

        return logs;
    }

    /// <summary>Logs whose message names any of these entities, as "(123/n".</summary>
    private static Expression<Func<AdminLog, bool>> MentionsAny(IEnumerable<int> entities)
    {
        var log = Expression.Parameter(typeof(AdminLog), "l");
        var message = Expression.Property(log, nameof(AdminLog.Message));
        var contains = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;

        Expression? body = null;
        foreach (var id in entities.Distinct().Take(20))
        {
            var test = Expression.Call(message, contains, Expression.Constant($"({id}/n"));
            body = body == null ? test : Expression.OrElse(body, test);
        }

        return Expression.Lambda<Func<AdminLog, bool>>(body ?? Expression.Constant(true), log);
    }

    /// <summary>The entities a log's JSON names, with the positions written beside them.</summary>
    public static LogRef[] LogRefs(JsonDocument json)
    {
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return Array.Empty<LogRef>();

        root.TryGetProperty(AdminLogPositionKey, out var positions);
        var refs = new List<LogRef>();
        foreach (var property in root.EnumerateObject())
        {
            var value = property.Value;
            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("id", out var idElement)
                || !idElement.TryGetInt32(out var id))
                continue;

            var name = value.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
            Guid? player = value.TryGetProperty("player", out var playerElement)
                           && playerElement.ValueKind == JsonValueKind.String
                           && Guid.TryParse(playerElement.GetString(), out var guid)
                ? guid
                : null;

            int? map = null, grid = null;
            float? x = null, y = null;
            if (positions.ValueKind == JsonValueKind.Object
                && positions.TryGetProperty(property.Name, out var pos)
                && pos.ValueKind == JsonValueKind.Object)
            {
                map = pos.TryGetProperty("map", out var m) ? m.GetInt32() : null;
                grid = pos.TryGetProperty("grid", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetInt32() : null;
                x = pos.TryGetProperty("x", out var px) ? px.GetSingle() : null;
                y = pos.TryGetProperty("y", out var py) ? py.GetSingle() : null;
            }

            refs.Add(new LogRef(property.Name, id, name, player, map, grid, x, y));
        }

        return refs.ToArray();
    }

    /// <summary>The "damage" a log's JSON records, if any.</summary>
    public static float? LogDamage(JsonDocument json)
    {
        return json.RootElement.ValueKind == JsonValueKind.Object
               && json.RootElement.TryGetProperty("damage", out var damage)
               && damage.ValueKind == JsonValueKind.Number
            ? damage.GetSingle()
            : null;
    }

    /// <summary>The JSON key admin logs keep entity positions under (AdminLogManager.WGPositions).</summary>
    public const string AdminLogPositionKey = "wgPos";
}
