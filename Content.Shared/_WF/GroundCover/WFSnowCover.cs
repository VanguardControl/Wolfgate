using Robust.Shared.Serialization;

namespace Content.Shared._WF.GroundCover;

/// <summary>Snow cover layout: one byte of depth per tile, in square chunks.</summary>
public static class WFSnowCover
{
    public const int ChunkSize = 16;

    public const int ChunkArea = ChunkSize * ChunkSize;

    /// <summary>Depth units in one cover level.</summary>
    public const int UnitsPerLevel = 60;

    public const int MaxLevels = 3;

    public const int MaxDepth = UnitsPerLevel * MaxLevels;

    /// <summary>Depth from which snow slows walking, and the depth at which it slows the most.</summary>
    public const int SlowFrom = UnitsPerLevel;

    /// <summary>Walking speed share in the deepest snow.</summary>
    public const float DeepestSpeed = 0.75f;

    public static Vector2i ChunkOf(Vector2i tile)
    {
        return new Vector2i((int) MathF.Floor(tile.X / (float) ChunkSize), (int) MathF.Floor(tile.Y / (float) ChunkSize));
    }

    /// <summary>A tile's index in its chunk's depth array.</summary>
    public static int IndexIn(Vector2i tile)
    {
        var x = tile.X - (int) MathF.Floor(tile.X / (float) ChunkSize) * ChunkSize;
        var y = tile.Y - (int) MathF.Floor(tile.Y / (float) ChunkSize) * ChunkSize;
        return x + y * ChunkSize;
    }

    /// <summary>The tile at an index of a chunk.</summary>
    public static Vector2i TileAt(Vector2i chunk, int index)
    {
        return new Vector2i(chunk.X * ChunkSize + index % ChunkSize, chunk.Y * ChunkSize + index / ChunkSize);
    }

    /// <summary>Walking speed share on snow of a depth, rounded to twentieths.</summary>
    public static float SpeedAt(int depth)
    {
        if (depth <= SlowFrom)
            return 1f;

        var deep = Math.Clamp((depth - SlowFrom) / (float) (MaxDepth - SlowFrom), 0f, 1f);
        return MathF.Round((1f - (1f - DeepestSpeed) * deep) * 20f) / 20f;
    }

    /// <summary>A steady 0.75 to 1.25 per tile, so snow lies in drifts rather than evenly.</summary>
    public static float Drift(Vector2i tile)
    {
        var h = unchecked((uint) (tile.X * 73856093) ^ (uint) (tile.Y * 19349663));
        h ^= h >> 13;
        h = unchecked(h * 0x5bd1e995);
        h ^= h >> 15;
        return 0.75f + (h & 0xFFFF) / 65535f * 0.5f;
    }
}

/// <summary>Snow cover chunks of one grid sent to a client. A chunk without depths was cleared.</summary>
[Serializable, NetSerializable]
public sealed class WFSnowCoverChunksMessage : EntityEventArgs
{
    public NetEntity Grid;

    public List<WFSnowChunkData> Chunks = new();
}

/// <summary>One chunk's tile depths, or null when it has no snow left.</summary>
[Serializable, NetSerializable]
public record struct WFSnowChunkData(Vector2i Index, byte[]? Depths);
