using Robust.Shared.Serialization;

namespace Content.Shared._WF.AppearanceStudio;

/// <summary>
/// Client to server: store this warp. <see cref="Pixels"/> is one frame of raw custom marking art as
/// <see cref="StudioWarp.ToArt"/> lays it out.
/// </summary>
[Serializable, NetSerializable]
public sealed class StudioWarpSaveEvent(int request, byte[] pixels) : EntityEventArgs
{
    /// <summary>Echoed in the result, so the editor can tell which save it answers.</summary>
    public readonly int Request = request;

    public readonly byte[] Pixels = pixels;
}

/// <summary>Server to client: the warp is stored under <see cref="Hash"/>, or it failed with <see cref="Error"/>.</summary>
[Serializable, NetSerializable]
public sealed class StudioWarpSaveResultEvent(int request, string? hash, string? error) : EntityEventArgs
{
    public readonly int Request = request;
    public readonly string? Hash = hash;

    /// <summary>A locale id saying why it failed.</summary>
    public readonly string? Error = error;
}
