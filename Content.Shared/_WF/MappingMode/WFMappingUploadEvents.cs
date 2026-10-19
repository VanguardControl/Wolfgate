using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode;

/// <summary>
/// Asks to upload a map file of <see cref="Bytes"/> bytes in <see cref="ChunkCount"/> chunks into the player's library
/// under <see cref="Name"/>. The server answers <see cref="WFMappingUploadReadyEvent"/> or refuses.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadBeginRequest(string name, int bytes, int chunkCount) : EntityEventArgs
{
    public readonly string Name = name;
    public readonly int Bytes = bytes;
    public readonly int ChunkCount = chunkCount;
}

/// <summary>Server to client: the upload is accepted; send its chunks under this transfer id.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadReadyEvent(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}

/// <summary>Client to server: one chunk of the file being uploaded.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadChunkEvent(int transferId, int chunk, byte[] data) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int Chunk = chunk;
    public readonly byte[] Data = data;
}

/// <summary>Server to client: a chunk arrived; the client sends the next.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadAckEvent(int transferId, int chunk) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int Chunk = chunk;
}

/// <summary>Server to client: the upload was checked and stored as a version of a library map.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadDoneEvent(int transferId, string mapName, string version) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly string MapName = mapName;
    public readonly string Version = version;
}

/// <summary>Stops the player's upload.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadCancelRequest(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}

/// <summary>
/// Server to client: the upload was refused or stopped; 0 for a refused begin. A <see cref="WFMappingResultEvent"/>
/// says why.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadCancelledEvent(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}
