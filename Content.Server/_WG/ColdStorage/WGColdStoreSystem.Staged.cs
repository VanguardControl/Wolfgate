using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Server._WG.ColdStorage;

/// <summary>
/// A restore spread over ticks: the bytes are unpacked and parsed on a worker thread, then the engine builds the grid
/// a few milliseconds a tick on a paused map of its own. One runs at a time.
/// </summary>
public sealed partial class WGColdStoreSystem
{
    /// <summary>Metres within which what woke a stored grid is too near to wait: the rest of the load is done at once.</summary>
    public const float UrgentRange = 150f;

    private sealed class StagedRestore(WGStoredGrid record, float priority)
    {
        public readonly WGStoredGrid Record = record;
        public float Priority = priority;

        /// <summary>Unpacks and parses the bytes on a worker thread; <see cref="Data"/> is set when it is done.</summary>
        public Task Parse = Task.CompletedTask;

        public MappingDataNode? Data;
        public StagedGridLoad? Load;
        public double WorstMs;
        public int Ticks;
    }

    private StagedRestore? _active;
    private float _restoreBudgetMs;

    /// <summary>Whether a restore is part-way through.</summary>
    public bool Restoring => _active != null;

    /// <summary>Ticks the last restore's load was spread over; 1 for one done in a single call.</summary>
    public int LastRestoreTicks { get; private set; }

    /// <summary>Starts unpacking a stored grid off the main thread. False if its bytes are gone.</summary>
    private bool BeginStagedRestore(WGStoredGrid record, float priority)
    {
        record.RetryOverride = false;
        if (!Backend.TryGet(record.Key, out var bytes))
        {
            Fail(record, "its bytes are missing", priority);
            return false;
        }

        var active = new StagedRestore(record, priority);
        active.Parse = Task.Run(() =>
        {
            if (MapLoaderSystem.TryParse(new StringReader(Decompress(bytes)), out var data))
                active.Data = data;
        });
        _active = active;
        return true;
    }

    /// <summary>Gives the restore in progress this tick's share of the budget, and finishes it when the load is done.</summary>
    private void StepStagedRestore()
    {
        var active = _active!;
        var record = active.Record;

        // Whatever keeps waking it keeps queueing it; the nearest of those decides how urgent it is.
        if (_restoreQueue.Remove(record.StandIn, out var queued) && queued < active.Priority)
            active.Priority = queued;

        var urgent = active.Priority <= UrgentRange;
        if (!active.Parse.IsCompleted)
        {
            if (!urgent)
                return;

            try
            {
                active.Parse.Wait();
            }
            catch (AggregateException)
            {
                // Read from the task below.
            }
        }

        if (active.Parse.IsFaulted || active.Data is not { } data)
        {
            _active = null;
            var why = active.Parse.Exception?.GetBaseException() is { } e
                ? $"{e.GetType().Name}: {e.Message}"
                : "its bytes hold no grid";
            Fail(record, why, active.Priority);
            return;
        }

        var watch = Stopwatch.StartNew();
        active.Ticks++;
        active.Load ??= record.Hollow
            ? _loader.BeginLoadOntoGrid(data, record.Key, record.StandIn)
            : _loader.BeginLoadGrid(data, record.Key, new DeserializationOptions { InitializeMaps = true });
        var budget = urgent ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(_restoreBudgetMs);
        if (!_loader.StepLoad(active.Load, budget))
        {
            active.WorstMs = Math.Max(active.WorstMs, watch.Elapsed.TotalMilliseconds);
            return;
        }

        _active = null;
        var load = active.Load;
        if (load.State != StagedLoadState.Done || load.Grid is not { } grid)
        {
            Fail(record, "the load failed", active.Priority);
            return;
        }

        var ok = FinishRestore(record, grid.Owner, active.Priority, load.Elapsed.TotalMilliseconds, watch, active.Ticks, active.WorstMs);
        if (record.Hollow)
            return;

        if (Exists(load.Map))
            Del(load.Map);

        if (!ok && Exists(grid.Owner))
            Del(grid.Owner);
    }

    /// <summary>Throws away the restore in progress, if it is this record's or any; its stand-in and bytes stay.</summary>
    private void AbortStagedRestore(WGStoredGrid? record = null)
    {
        if (_active is not { } active || record != null && active.Record != record)
            return;

        _active = null;
        if (active.Load != null)
            _loader.AbortLoad(active.Load);
    }
}
