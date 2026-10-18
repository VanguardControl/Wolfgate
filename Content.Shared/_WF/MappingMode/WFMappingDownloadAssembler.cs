using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._WF.MappingMode;

/// <summary>What became of a chunk handed to a <see cref="WFMappingDownloadAssembler"/>.</summary>
public enum WFMappingChunkResult : byte
{
    Accepted,

    /// <summary>The index is outside the transfer's chunk count, or the count doesn't match.</summary>
    OutOfRange,

    /// <summary>That chunk already arrived.</summary>
    Duplicate,

    /// <summary>The chunk, or the file so far, is over its size cap.</summary>
    TooLarge,
}

/// <summary>
/// Puts one downloaded level file back together from its chunks: a transfer is one file, sent as up to
/// <see cref="MaxChunks"/> chunks of at most <see cref="ChunkSize"/> bytes.
/// </summary>
public sealed class WFMappingDownloadAssembler
{
    public const int ChunkSize = 256 * 1024;
    public const int MaxFileBytes = 32 * 1024 * 1024;
    public const int MaxChunks = MaxFileBytes / ChunkSize;

    public readonly int TransferId;
    public readonly string FileName;
    public readonly int ChunkCount;

    private readonly byte[]?[] _chunks;
    private int _received;
    private long _bytes;

    private WFMappingDownloadAssembler(int transferId, string fileName, int chunkCount)
    {
        TransferId = transferId;
        FileName = fileName;
        ChunkCount = chunkCount;
        _chunks = new byte[]?[chunkCount];
    }

    /// <summary>
    /// Starts a transfer; false when the file name isn't a level file or the chunk count is outside 1 to
    /// <see cref="MaxChunks"/>.
    /// </summary>
    public static bool TryCreate(int transferId, string fileName, int chunkCount,
        [NotNullWhen(true)] out WFMappingDownloadAssembler? assembler)
    {
        assembler = null;
        if (chunkCount < 1 || chunkCount > MaxChunks || !WFMappingLibraryNames.TryParseLevelFile(fileName, out _))
            return false;

        assembler = new WFMappingDownloadAssembler(transferId, fileName, chunkCount);
        return true;
    }

    /// <summary>Chunks received so far.</summary>
    public int Received => _received;

    public bool IsComplete => _received == ChunkCount;

    /// <summary>Stores one chunk; <paramref name="chunkCount"/> must match the transfer's.</summary>
    public WFMappingChunkResult Add(int index, int chunkCount, byte[] data)
    {
        if (chunkCount != ChunkCount || index < 0 || index >= ChunkCount)
            return WFMappingChunkResult.OutOfRange;

        if (_chunks[index] != null)
            return WFMappingChunkResult.Duplicate;

        if (data.Length > ChunkSize || _bytes + data.Length > MaxFileBytes)
            return WFMappingChunkResult.TooLarge;

        _chunks[index] = data;
        _received++;
        _bytes += data.Length;
        return WFMappingChunkResult.Accepted;
    }

    /// <summary>The whole file once every chunk has arrived.</summary>
    public bool TryComplete(out byte[] data)
    {
        data = Array.Empty<byte>();
        if (!IsComplete)
            return false;

        data = new byte[_bytes];
        var offset = 0;
        foreach (var chunk in _chunks)
        {
            chunk!.CopyTo(data, offset);
            offset += chunk.Length;
        }

        return true;
    }
}
