using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Shared._WG.LogInvestigator;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.LogInvestigator;

/// <summary>
/// The log investigator: pages through a round's logs by players, entities, categories, impact, text and round time,
/// and builds the encounter, timeline and nearby reports.
/// </summary>
public sealed class LogInvestigatorEui : BaseEui
{
    [Dependency] private IAdminLogManager _adminLogs = default!;
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private ILogManager _log = default!;

    /// <summary>The most rows a report reads of each kind.</summary>
    private const int ReportRows = 5000;
    private const int ReportPages = 10;

    /// <summary>The window around a moment that "who was nearby" looks at.</summary>
    private static readonly TimeSpan NearbyWindow = TimeSpan.FromSeconds(60);

    private readonly ISawmill _sawmill;
    private readonly LogQuery? _prefill;
    private readonly LogReportKind? _prefillReport;
    private CancellationTokenSource _cancel = new();

    /// <summary>The report being built read as many rows as it may of some kind, so it may be missing some.</summary>
    private bool _reportCut;

    /// <summary>Cancels the live poll for newer logs, apart from searches and reports.</summary>
    private CancellationTokenSource _tailCancel = new();

    /// <summary>The round load in progress, if any; queries and reports wait for it.</summary>
    private Task? _roundLoad;

    /// <summary>The last round load failed, so the round shown isn't the one asked for.</summary>
    private bool _roundLoadFailed;

    private int _roundId;
    private DateTime? _roundStart;
    private List<LogPlayer> _roundPlayers = new();
    private bool _loading = true;

    private LogInvestigatorEui(LogQuery? prefill, LogReportKind? report)
    {
        IoCManager.InjectDependencies(this);
        _sawmill = _log.GetSawmill(AdminLogManager.SawmillId);
        _prefill = prefill;
        _prefillReport = report;
    }

    /// <summary>Opens the investigator for an admin, filled in with the query, if the investigator is on.</summary>
    public static bool TryOpen(ICommonSession admin, LogQuery? prefill, LogReportKind? report = null)
    {
        if (!IoCManager.Resolve<IConfigurationManager>().GetCVar(LogInvestigatorCVars.Investigator))
            return false;

        IoCManager.Resolve<EuiManager>().OpenEui(new LogInvestigatorEui(prefill, report), admin);
        return true;
    }

    /// <summary>Opens the investigator on one player's logs this round.</summary>
    public static bool TryOpenForPlayer(ICommonSession admin, Guid player)
    {
        return TryOpen(admin, new LogQuery { RoundId = CurrentRound(), Players = new[] { player } });
    }

    /// <summary>Opens the investigator on the logs that name an entity this round.</summary>
    public static bool TryOpenForEntity(ICommonSession admin, EntityUid entity)
    {
        return TryOpen(admin, new LogQuery { RoundId = CurrentRound(), Entities = new[] { (int) entity } });
    }

    private static int CurrentRound()
    {
        return IoCManager.Resolve<IEntityManager>().System<GameTicker>().RoundId;
    }

    public override async void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermsChanged;

        try
        {
            await (_roundLoad = LoadRound(_prefill is { RoundId: > 0 } prefill ? prefill.RoundId : CurrentRound()));
            if (_prefill != null && !IsShutDown)
            {
                _prefill.RoundId = _roundId;
                SendMessage(new LogInvestigatorPrefillMsg(_prefill, _prefillReport));
            }
        }
        catch (OperationCanceledException)
        {
            // Closed, or another round was picked, before the first load finished.
        }
        catch (Exception e)
        {
            _sawmill.Error($"Log investigator failed to open: {e}");
        }
    }

    public override void Closed()
    {
        base.Closed();
        _admins.OnPermsChanged -= OnPermsChanged;
        _cancel.Cancel();
        _cancel.Dispose();
        _tailCancel.Cancel();
        _tailCancel.Dispose();
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !_admins.HasAdminFlag(Player, AdminFlags.Logs))
            Close();
    }

    public override EuiStateBase GetNewState()
    {
        return new LogInvestigatorEuiState
        {
            RoundId = _roundId,
            CurrentRound = CurrentRound(),
            RoundStart = _roundStart,
            Players = _roundPlayers,
            Loading = _loading,
        };
    }

    public override async void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_admins.HasAdminFlag(Player, AdminFlags.Logs))
            return;

        try
        {
            switch (msg)
            {
                case LogInvestigatorRoundMsg round:
                    await (_roundLoad = LoadRound(round.RoundId));
                    break;
                case LogInvestigatorQueryMsg query:
                    await RunQuery(query);
                    break;
                case LogInvestigatorReportRequest report:
                    await BuildReport(report);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer request.
        }
        catch (Exception e)
        {
            _sawmill.Error($"Log investigator request from {Player.Name} failed: {e}");
        }
    }

    private CancellationToken Restart()
    {
        _cancel.Cancel();
        _cancel.Dispose();
        _cancel = new CancellationTokenSource();
        return _cancel.Token;
    }

    /// <summary>Switches to a round. A newer load, query or report cancels this one; nothing is written after close.</summary>
    private async Task LoadRound(int roundId)
    {
        var cancel = Restart();
        _loading = true;
        StateDirty();

        Round round;
        try
        {
            round = await _adminLogs.Round(roundId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // No round to search: say so instead of leaving the window loading, and refuse queries until one loads.
            // A load already replaced by a newer one changes nothing.
            if (!cancel.IsCancellationRequested && !IsShutDown)
            {
                _roundLoadFailed = true;
                _loading = false;
                StateDirty();
            }

            throw;
        }

        cancel.ThrowIfCancellationRequested();
        _roundLoadFailed = false;
        if (IsShutDown)
            return;

        _roundId = roundId;
        _roundStart = round.StartDate is { } start ? DateTime.SpecifyKind(start, DateTimeKind.Utc) : null;

        // Character names are only known for players still here; past rounds list accounts.
        var minds = _entities.System<SharedMindSystem>();
        var current = roundId == CurrentRound();
        _roundPlayers = round.Players
            .Select(p => new LogPlayer(p.UserId, p.LastSeenUserName,
                current ? minds.GetCharacterName(new Robust.Shared.Network.NetUserId(p.UserId)) : null))
            .OrderBy(p => p.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _loading = false;
        StateDirty();
    }

    /// <summary>Waits out a round load in progress, so a query or report neither cancels it nor reads the old round.</summary>
    private async Task WaitForRound()
    {
        while (_roundLoad is { IsCompleted: false } loading)
        {
            try
            {
                await loading;
            }
            catch (OperationCanceledException)
            {
                // Replaced by a newer load, which the loop waits for next.
            }
            catch (Exception)
            {
                // Failed; checked below.
            }
        }

        // A failed load leaves the previous round's id and players behind; nothing runs against them.
        if (_roundLoadFailed)
            throw new InvalidOperationException($"Round {_roundId} could not be loaded; pick a round again.");
    }

    private async Task RunQuery(LogInvestigatorQueryMsg msg)
    {
        if (msg is { Newer: true, AfterId: { } newest })
        {
            await RunNewer(msg.Query, newest);
            return;
        }

        await WaitForRound();
        var cancel = Restart();
        var query = msg.Query;
        query.RoundId = _roundId;
        var first = msg.AfterId == null;

        _sawmill.Info($"Log investigator query from {Player.Name}: round {query.RoundId}, {query.Players.Length} players, {query.Entities.Length} entities, {query.Types?.Count.ToString() ?? "all"} types, text \"{query.Text}\"");
        var stopwatch = new Robust.Shared.Timing.Stopwatch();
        stopwatch.Start();

        if (!await ApplyNear(query, cancel))
        {
            if (!IsShutDown && Player.Status != SessionStatus.Disconnected)
                SendMessage(new LogInvestigatorPageMsg(new List<LogRow>(), first, false, first ? 0 : null));
            return;
        }

        var page = _cfg.GetCVar(LogInvestigatorCVars.PageSize);
        var (rows, hasMore, total) = await _db.GetInvestigatorLogsAsync(query, _roundStart, msg.AfterId, page, first, cancel);
        cancel.ThrowIfCancellationRequested();

        _sawmill.Info($"Sent {rows.Count} logs to {Player.Name} in {stopwatch.Elapsed.TotalMilliseconds} ms");
        if (!IsShutDown && Player.Status != SessionStatus.Disconnected)
            SendMessage(new LogInvestigatorPageMsg(rows, first, hasMore, total));
    }

    /// <summary>
    /// The logs newer than the top of a newest-first list, so it stays live. It doesn't cancel a search or report in
    /// progress; a newer poll replaces an older one.
    /// </summary>
    private async Task RunNewer(LogQuery query, int newest)
    {
        if (IsShutDown || _roundLoad is { IsCompleted: false } || _roundLoadFailed || query.RoundId != _roundId)
            return;

        _tailCancel.Cancel();
        _tailCancel.Dispose();
        _tailCancel = new CancellationTokenSource();
        var cancel = _tailCancel.Token;

        if (!await ApplyNear(query, cancel))
            return;

        var page = _cfg.GetCVar(LogInvestigatorCVars.PageSize);
        var (rows, hasMore, _) = await _db.GetInvestigatorLogsAsync(query, _roundStart, newest, page, false, cancel, newer: true);
        cancel.ThrowIfCancellationRequested();

        if (rows.Count > 0 && !IsShutDown && Player.Status != SessionStatus.Disconnected)
            SendMessage(new LogInvestigatorPageMsg(rows, false, hasMore, null, newer: true));
    }

    private async Task BuildReport(LogInvestigatorReportRequest request)
    {
        await WaitForRound();
        var cancel = Restart();
        var query = request.Query;
        query.RoundId = _roundId;
        _reportCut = false;

        var report = request.Kind switch
        {
            LogReportKind.Encounter => await Encounter(query, Math.Clamp(request.Radius, 1, 200), cancel),
            LogReportKind.Timeline => await Timeline(query, cancel),
            LogReportKind.Nearby when request.Point is { } point => await Nearby(point, Math.Clamp(request.Radius, 1, 200), cancel),
            _ => null,
        };

        cancel.ThrowIfCancellationRequested();

        // A report that hit the row cap says so first, so nobody reads a partial fight as the whole of it.
        if (report != null && _reportCut)
        {
            var sections = new List<LogReportSection> { new(Loc.GetString("wg-log-report-cut-title"), new List<string> { Loc.GetString("wg-log-report-cut", ("rows", ReportRows)) }) };
            sections.AddRange(report.Sections);
            report = report with { Sections = sections };
        }

        if (report != null && !IsShutDown && Player.Status != SessionStatus.Disconnected)
            SendMessage(new LogInvestigatorReportMsg(report));
    }

    private async Task<LogReport> Encounter(LogQuery query, int radius, CancellationToken cancel)
    {
        var combat = await Rows(Narrow(query, LogReports.CombatTypes), cancel);

        var guns = Narrow(query, new HashSet<LogType> { LogType.ShipgunFired }, players: false);
        guns.FromSeconds -= LogReports.GunnerWindow.TotalSeconds;
        guns.ToSeconds += LogReports.GunnerWindow.TotalSeconds;
        var hits = LogReports.Hits(combat, await Rows(guns, cancel));

        var deathQuery = Narrow(query, new HashSet<LogType> { LogType.Damaged });
        deathQuery.Text = " to Dead";
        var deaths = await Rows(deathQuery, cancel);

        var chat = new List<LogRow>();
        List<LogReports.LogPlayerNearby>? nearby = null;
        var parties = query.Players.ToHashSet();
        var firstHit = hits
            .Where(h => h.VictimPlayer != null && (parties.Count == 0 || parties.Contains(h.VictimPlayer.Value) || (h.AttackerPlayer is { } a && parties.Contains(a))))
            .MinBy(h => h.At);

        if (firstHit != null)
        {
            var from = Seconds(firstHit.At - LogReports.ChatLookBack);
            var to = Seconds(firstHit.At);

            // What the parties said, and hails to or from the grids they were on.
            var said = new LogQuery { RoundId = query.RoundId, Players = query.Players, FromSeconds = from, ToSeconds = to, Types = new HashSet<LogType> { LogType.Chat }, Ascending = true };
            chat.AddRange(await Rows(said, cancel));

            var grids = combat.SelectMany(r => r.Refs)
                .Where(r => r.Grid != null && r.Player is { } p && (parties.Count == 0 || parties.Contains(p)))
                .Select(r => r.Grid!.Value)
                .ToHashSet();
            if (grids.Count > 0)
            {
                var hails = new LogQuery { RoundId = query.RoundId, FromSeconds = from, ToSeconds = to, Types = new HashSet<LogType> { LogType.Chat }, Text = "Hail from", Ascending = true };
                chat.AddRange(await Matching(hails, r => r.Refs.Any(x => x.Key.StartsWith("grid") && grids.Contains(x.Id)), cancel));
            }

            chat = chat.DistinctBy(r => r.Id).ToList();

            if (firstHit.VictimRef is { Map: { } map, X: { } x, Y: { } y } victim)
                nearby = await NearbyList(new LogPoint(map, victim.Grid, x, y, firstHit.At), radius, cancel);
        }

        return LogReports.Encounter(hits, deaths, chat, _roundStart, nearby);
    }

    /// <summary>The most players one timeline report covers, each in a block of its own.</summary>
    private const int TimelinePlayers = 5;

    private async Task<LogReport> Timeline(LogQuery query, CancellationToken cancel)
    {
        if (query.Players.Length == 0)
        {
            return new LogReport(LogReportKind.Timeline, Loc.GetString("wg-log-report-timeline-title", ("player", "?")),
                new List<LogReportSection> { new(Loc.GetString("wg-log-report-phases"), new List<string> { Loc.GetString("wg-log-report-pick-player") }) });
        }

        var categories = new Dictionary<LogType, string>();
        foreach (var category in _proto.EnumeratePrototypes<WGLogCategoryPrototype>())
        {
            foreach (var type in category.Types)
            {
                categories[type] = Loc.GetString(category.Name);
            }
        }

        // Each chosen player gets their own block, headed by their name; past the limit the report says who was left out.
        var players = query.Players.Take(TimelinePlayers).ToList();
        var names = new List<string>();
        var sections = new List<LogReportSection>();
        foreach (var player in players)
        {
            var one = new LogQuery
            {
                RoundId = query.RoundId,
                Players = new[] { player },
                FromSeconds = query.FromSeconds,
                ToSeconds = query.ToSeconds,
                Types = query.Types,
                Ascending = true,
            };

            var name = _roundPlayers.FirstOrDefault(p => p.Id == player) is { } known
                ? known.Character != null ? $"{known.Character} ({known.Username})" : known.Username
                : player.ToString();
            names.Add(name);

            var timeline = LogReports.Timeline(name, await Rows(one, cancel), categories, _roundStart);
            if (players.Count > 1)
                sections.Add(new LogReportSection(Loc.GetString("wg-log-report-timeline-player", ("player", name)), new List<string>()));
            sections.AddRange(timeline.Sections);
        }

        if (query.Players.Length > players.Count)
        {
            sections.Add(new LogReportSection(Loc.GetString("wg-log-report-timeline-more-title"),
                new List<string> { Loc.GetString("wg-log-report-timeline-more", ("count", query.Players.Length - players.Count)) }));
        }

        return new LogReport(LogReportKind.Timeline, Loc.GetString("wg-log-report-timeline-title", ("player", string.Join(", ", names))), sections);
    }

    private async Task<LogReport> Nearby(LogPoint point, int radius, CancellationToken cancel)
    {
        var nearby = await NearbyList(point, radius, cancel);
        var filter = new LogQuery
        {
            RoundId = _roundId,
            Players = nearby.Select(n => n.Player).ToArray(),
            FromSeconds = Seconds(point.At - NearbyWindow),
            ToSeconds = Seconds(point.At + NearbyWindow),
        };
        return LogReports.NearbyReport(point, radius, nearby, _roundStart) with { Filter = nearby.Count > 0 ? filter : null };
    }

    private async Task<List<LogReports.LogPlayerNearby>> NearbyList(LogPoint point, int radius, CancellationToken cancel)
    {
        var from = point.At - NearbyWindow;
        var to = point.At + NearbyWindow;
        var positions = await _db.GetPlayerPositionsAsync(_roundId, from, to, cancel);

        var spokeQuery = new LogQuery { RoundId = _roundId, FromSeconds = Seconds(from), ToSeconds = Seconds(to), Types = new HashSet<LogType> { LogType.Chat }, Ascending = true };
        var spoke = (await Rows(spokeQuery, cancel))
            .SelectMany(r => r.Refs)
            .Where(r => r.Player != null)
            .Select(r => r.Player!.Value)
            .ToHashSet();

        var names = _roundPlayers.ToDictionary(p => p.Id, p => p.Username);
        return LogReports.Nearby(point, radius, positions, names, spoke);
    }

    /// <summary>The query limited to these types, oldest first; without players when <paramref name="players"/> is false.</summary>
    private static LogQuery Narrow(LogQuery query, HashSet<LogType> types, bool players = true)
    {
        return new LogQuery
        {
            RoundId = query.RoundId,
            FromSeconds = query.FromSeconds,
            ToSeconds = query.ToSeconds,
            Players = players ? query.Players : Array.Empty<Guid>(),
            Entities = players ? query.Entities : Array.Empty<int>(),
            Types = types,
            Ascending = true,
        };
    }

    /// <summary>Up to <see cref="ReportRows"/> logs for a report; hitting the cap marks the report as cut short.</summary>
    private async Task<List<LogRow>> Rows(LogQuery query, CancellationToken cancel)
    {
        var (rows, more, _) = await _db.GetInvestigatorLogsAsync(query, _roundStart, null, ReportRows, false, cancel);
        if (more)
            _reportCut = true;

        return rows;
    }

    /// <summary>
    /// Rows of the query that pass a filter the database can't apply, paged so the filter sees past the first page.
    /// Stops at <see cref="ReportRows"/> matches or <see cref="ReportPages"/> pages.
    /// </summary>
    private async Task<List<LogRow>> Matching(LogQuery query, Func<LogRow, bool> filter, CancellationToken cancel)
    {
        var found = new List<LogRow>();
        int? after = null;
        for (var page = 0; page < ReportPages && found.Count < ReportRows; page++)
        {
            var (rows, hasMore, _) = await _db.GetInvestigatorLogsAsync(query, _roundStart, after, ReportRows, false, cancel);
            found.AddRange(rows.Where(filter));
            if (!hasMore || rows.Count == 0)
                break;
            after = rows[^1].Id;
        }

        return found.Take(ReportRows).ToList();
    }

    /// <summary>
    /// Narrows a query with a Near point to the players who came within its radius of that point: over the query's
    /// time window, or the minute either side of the point when it has none. False when nobody did.
    /// </summary>
    private async Task<bool> ApplyNear(LogQuery query, CancellationToken cancel)
    {
        if (query.Near is not { } near || _roundStart is not { } start)
            return true;

        var from = query.FromSeconds is { } fromSeconds ? start.AddSeconds(fromSeconds) : near.At - NearbyWindow;
        var to = query.ToSeconds is { } toSeconds
            ? start.AddSeconds(toSeconds)
            : query.FromSeconds != null ? DateTime.UtcNow : near.At + NearbyWindow;
        var positions = await _db.GetPlayerPositionsAsync(_roundId, from, to, cancel);
        var radius = Math.Clamp(query.NearRadius, 1, 200);
        var found = LogReports.Nearby(near, radius, positions, new Dictionary<Guid, string>(), new HashSet<Guid>())
            .Select(n => n.Player)
            .ToHashSet();

        if (query.Players.Length > 0)
            found.IntersectWith(query.Players);

        query.Players = found.ToArray();
        query.AllPlayers = false;
        return found.Count > 0;
    }

    /// <summary>Seconds since the round started, or null when the start isn't known.</summary>
    private double? Seconds(DateTime at)
    {
        return _roundStart is { } start ? (at - start).TotalSeconds : null;
    }
}
