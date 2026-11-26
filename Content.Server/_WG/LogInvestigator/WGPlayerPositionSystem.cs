using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._WG.LogInvestigator;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Server._WG.LogInvestigator;

/// <summary>
/// Samples where every player's body is every few seconds and stores it, so the investigator can say who was near an
/// event even when they never appear in its logs. The rows are pruned with the admin logs, outside the game.
/// </summary>
public sealed class WGPlayerPositionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Samples are written after this many have queued up, or a minute at the latest.</summary>
    private const int BatchSize = 500;

    /// <summary>Most samples held while the database refuses them, about an hour of a full server.</summary>
    private const int MaxQueued = 30_000;

    private static readonly TimeSpan MaxBatchAge = TimeSpan.FromMinutes(1);

    private readonly List<WolfgatePlayerPosition> _queue = new();
    private TimeSpan _nextSample;
    private TimeSpan _nextWrite;
    private bool _writing;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Write());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var interval = _cfg.GetCVar(LogInvestigatorCVars.PositionInterval);
        if (interval <= 0 || _timing.RealTime < _nextSample)
            return;

        _nextSample = _timing.RealTime + TimeSpan.FromSeconds(interval);
        Sample();

        if (_queue.Count >= BatchSize || _timing.RealTime >= _nextWrite)
            Write();
    }

    /// <summary>Queues where every player in game is now.</summary>
    public void Sample()
    {
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        var now = DateTime.UtcNow;
        var round = _ticker.RoundId;
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame
                || session.AttachedEntity is not { } body
                || !TryComp(body, out TransformComponent? xform)
                || xform.MapID == default)
                continue;

            var world = _transform.GetWorldPosition(xform);
            _queue.Add(new WolfgatePlayerPosition
            {
                RoundId = round,
                SampledAt = now,
                PlayerUserId = session.UserId.UserId,
                EntityId = (int) body,
                EntityName = Truncate(Name(body), 128),
                MapId = (int) xform.MapID,
                GridId = xform.GridUid is { } grid ? (int) grid : null,
                X = MathF.Round(world.X, 1),
                Y = MathF.Round(world.Y, 1),
                Alive = !_mobState.IsDead(body),
            });
        }
    }

    /// <summary>Writes the queued samples off the main thread.</summary>
    public async Task WriteAsync()
    {
        if (_writing || _queue.Count == 0)
            return;

        _writing = true;
        _nextWrite = _timing.RealTime + MaxBatchAge;
        var batch = new List<WolfgatePlayerPosition>(_queue);
        _queue.Clear();
        try
        {
            await _db.AddPlayerPositionsAsync(batch);
        }
        catch (Exception e)
        {
            // Back in front of newer samples for the next write; oldest dropped past the cap during a long outage.
            // Rows are fresh objects with no id, so a retry inserts each once more only if this write half-committed.
            foreach (var row in batch)
            {
                row.Id = 0;
            }

            _queue.InsertRange(0, batch);
            if (_queue.Count > MaxQueued)
                _queue.RemoveRange(0, _queue.Count - MaxQueued);

            Log.Error($"Writing {batch.Count} player positions failed; they are retried: {e}");
        }
        finally
        {
            _writing = false;
        }
    }

    private void Write()
    {
        _ = WriteAsync();
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text[..max];
    }
}
