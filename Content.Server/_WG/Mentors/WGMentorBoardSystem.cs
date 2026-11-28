using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Database;
using Content.Shared._WG.Mentors;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.Mentors;

/// <summary>
/// The mentor leaderboard and My stats: counted from the ticket tables by period, cached five minutes per period,
/// for mentors (their window), staff (the Mentors tab, with escalations and fallbacks) and the panel.
/// </summary>
public sealed class WGMentorBoardSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private WGMentorSystem _mentors = default!;
    [Dependency] private WGMentorStaffSystem _staff = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(5);

    /// <summary>The ranked board for a period with mentors' names, and the period's fallbacks.</summary>
    public sealed record Board(List<(Guid User, MentorBoardRow Row)> Rows, int Fallbacks);

    private readonly Dictionary<MentorBoardPeriod, (TimeSpan At, Task<Board> Board)> _cache = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMentorBoardRequestEvent>((ev, args) => _ = SendAsync(args.SenderSession, ev.Period));
    }

    /// <summary>The board for a period, from the cache when it is under five minutes old.</summary>
    public Task<Board> GetAsync(MentorBoardPeriod period)
    {
        var now = _timing.RealTime;
        if (_cache.TryGetValue(period, out var cached) && now - cached.At < CacheTime && !cached.Board.IsFaulted)
            return cached.Board;

        var board = BuildAsync(period);
        _cache[period] = (now, board);
        return board;
    }

    private async Task<Board> BuildAsync(MentorBoardPeriod period)
    {
        var since = MentorBoard.Since(period, DateTime.UtcNow);
        var tickets = await _db.GetMentorBoardTicketsAsync(since);
        var mentors = (await _db.GetMentorsAsync()).Select(m => m.UserId).ToHashSet();

        var rows = new List<(Guid, MentorBoardRow)>();
        foreach (var stats in MentorBoard.Compute(tickets).Where(s => mentors.Contains(s.Mentor)))
        {
            rows.Add((stats.Mentor, new MentorBoardRow
            {
                Name = (await _locator.LookupIdAsync(new NetUserId(stats.Mentor)))?.Username ?? stats.Mentor.ToString(),
                Resolved = stats.Resolved,
                Helpful = stats.Helpful,
                Rated = stats.Rated,
                MedianFirstAnswer = stats.MedianFirstAnswer is { } median ? (float) median : null,
                Assists = stats.Assists,
                Escalations = stats.Escalations,
            }));
        }

        return new Board(rows, tickets.Count(t => t.CloseReason == "fallback"));
    }

    private async Task SendAsync(ICommonSession session, MentorBoardPeriod period)
    {
        var staff = _staff.IsStaff(session);
        if (!staff && !_mentors.IsMentor(session.UserId))
            return;

        Board board;
        try
        {
            board = await GetAsync(period);
        }
        catch (Exception e)
        {
            Log.Error($"The mentor leaderboard for {period} failed: {e}");
            return;
        }

        var ev = new WGMentorBoardEvent { Period = period, Staff = staff, Fallbacks = staff ? board.Fallbacks : 0 };
        for (var i = 0; i < board.Rows.Count; i++)
        {
            var (user, row) = board.Rows[i];
            var copy = new MentorBoardRow
            {
                Name = row.Name,
                Resolved = row.Resolved,
                Helpful = row.Helpful,
                Rated = row.Rated,
                MedianFirstAnswer = row.MedianFirstAnswer,
                Assists = row.Assists,
                Escalations = staff ? row.Escalations : 0,
            };

            ev.Rows.Add(copy);
            if (user == session.UserId.UserId)
            {
                ev.Mine = copy;
                ev.MyRank = i + 1;
            }
        }

        if (session.Status != SessionStatus.Disconnected)
            RaiseNetworkEvent(ev, session);
    }
}
