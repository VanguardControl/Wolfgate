using System.Numerics;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;

namespace Content.Server._WG.ColdStorage;

/// <summary>Raised broadcast when a grid has been saved and its stand-in placed, before the grid is deleted.</summary>
[ByRefEvent]
public readonly record struct WGGridStoredEvent(EntityUid OldGrid, EntityUid StandIn);

/// <summary>Raised broadcast when a stored grid has been rebuilt in its stand-in's place, just before the stand-in goes.</summary>
[ByRefEvent]
public readonly record struct WGGridRestoredEvent(EntityUid StandIn, EntityUid NewGrid);

/// <summary>
/// Raised broadcast when a grid would be stored. A system that holds the grid's uid and can't follow it to a new one
/// sets <see cref="Reason"/> to a Fluent id saying why it must stay.
/// </summary>
[ByRefEvent]
public record struct WGColdStoreCheckEvent(EntityUid Grid, string? Reason = null);

/// <summary>Where a stored grid is in its life.</summary>
public enum WGStoredState : byte
{
    Stored,

    /// <summary>Its last load failed; it keeps its stand-in and bytes and is retried now and then.</summary>
    Failed,
}

/// <summary>What is known about a stored grid without loading it.</summary>
public readonly record struct WGColdStoreMeta(string Name, int Entities, TimeSpan Stored);

/// <summary>Where stored grids' bytes are kept. In memory for now; a database table later, for restarts.</summary>
public interface IWGColdStoreBackend
{
    void Put(string key, byte[] bytes, WGColdStoreMeta meta);

    bool TryGet(string key, out byte[] bytes);

    void Remove(string key);

    /// <summary>Drops everything, at round end.</summary>
    void Clear();

    long TotalBytes { get; }
}

/// <summary>Keeps stored grids' compressed YAML in memory; it is lost at a restart.</summary>
public sealed class WGColdStoreMemoryBackend : IWGColdStoreBackend
{
    private readonly Dictionary<string, (byte[] Bytes, WGColdStoreMeta Meta)> _entries = new();

    public long TotalBytes { get; private set; }

    public void Put(string key, byte[] bytes, WGColdStoreMeta meta)
    {
        Remove(key);
        _entries[key] = (bytes, meta);
        TotalBytes += bytes.Length;
    }

    public bool TryGet(string key, out byte[] bytes)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            bytes = entry.Bytes;
            return true;
        }

        bytes = Array.Empty<byte>();
        return false;
    }

    public void Remove(string key)
    {
        if (_entries.Remove(key, out var old))
            TotalBytes -= old.Bytes.Length;
    }

    public void Clear()
    {
        _entries.Clear();
        TotalBytes = 0;
    }
}

/// <summary>Everything kept about a stored grid besides its bytes: where it was, how it looked and its cleanup clock.</summary>
public sealed class WGStoredGrid
{
    public string Key = string.Empty;
    public EntityUid StandIn;
    public string Name = string.Empty;
    public WGStoredState State;

    public EntityUid Map;
    public MapId MapId;

    /// <summary>The grid's own position and rotation on its map.</summary>
    public Vector2 Position;
    public Angle Rotation;

    public Box2 LocalBounds;
    public Box2 WorldBounds;
    public float Mass;
    public IFFFlags IffFlags;
    public Color? IffColor;
    public int Entities;
    public int Bytes;
    public TimeSpan StoredAt;

    /// <summary>Grid cleanup would have been counting down on it; the clock keeps running while nobody is near.</summary>
    public bool CleanupCandidate;
    public TimeSpan CleanupAccumulator;
    public float CleanupRate;
    public float CleanupDistance;

    /// <summary>The distance is the grid's own override, which entity pressure doesn't scale.</summary>
    public bool CleanupDistanceFixed;

    /// <summary>When the clock was last advanced or reset.</summary>
    public TimeSpan CleanupSeen;

    public int Failures;
    public TimeSpan NextRetry;
    public string? LastError;

    /// <summary>An admin asked for it back: the next try ignores the retry delay and the failure cap.</summary>
    public bool RetryOverride;
}
