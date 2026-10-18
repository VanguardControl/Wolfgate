using Content.Shared._WF.MappingMode;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._WF.MappingMode;

/// <summary>Progress of the player's download, for the lobby panel and the Library tab.</summary>
public sealed class WFMappingDownloadProgress
{
    public string MapName = string.Empty;
    public string Version = string.Empty;
    public int FileIndex;
    public int FileCount;
    public int Chunks;
    public int ChunkCount;

    /// <summary>Still receiving; false once every file is written.</summary>
    public bool Active;
}

public sealed partial class WFMappingClientSystem
{
    [Dependency] private IResourceManager _res = default!;

    private WFMappingDownloadAssembler? _assembler;

    /// <summary>A transfer this client gave up on; its chunks still in flight are dropped quietly.</summary>
    private int? _droppedTransfer;
    private readonly List<ResPath> _written = new();

    /// <summary>The running or last finished download, or null when there is none to show.</summary>
    public WFMappingDownloadProgress? Download { get; private set; }

    public event Action? DownloadUpdated;

    private void InitializeDownload()
    {
        SubscribeNetworkEvent<WFMappingDownloadChunkEvent>(OnChunk);
        SubscribeNetworkEvent<WFMappingDownloadCancelledEvent>(OnDownloadCancelled);
    }

    /// <summary>Asks for every level of a library version; null is the newest.</summary>
    public void RequestDownload(string mapName, string? version)
    {
        RaiseNetworkEvent(new WFMappingDownloadRequest(mapName, version));
    }

    public void CancelDownload()
    {
        RaiseNetworkEvent(new WFMappingDownloadCancelRequest());
    }

    /// <summary>The folder the last download went into.</summary>
    public ResPath? DownloadFolder => Download is { Active: false } done
        ? WFMappingLibraryNames.DownloadRoot / done.MapName / done.Version
        : null;

    /// <summary>Opens the last download's folder in the OS file browser.</summary>
    public void OpenDownloadFolder()
    {
        if (DownloadFolder is not { } folder)
            return;

        try
        {
            _res.UserData.OpenOsWindow(folder);
        }
        catch (Exception e)
        {
            Log.Error($"Opening the mapping download folder {folder} failed: {e}");
        }
    }

    private void OnChunk(WFMappingDownloadChunkEvent ev)
    {
        if (ev.TransferId == _droppedTransfer)
            return;

        if (!WFMappingLibraryNames.IsValidMapName(ev.MapName) ||
            !WFMappingLibraryNames.TryParseVersion(ev.Version, out _) ||
            ev.FileIndex < 0 || ev.FileIndex >= ev.FileCount)
        {
            Fail(ev.TransferId, "wf-mapping-mode-download-bad-data");
            return;
        }

        if (_assembler == null || _assembler.TransferId != ev.TransferId)
        {
            if (ev.Chunk != 0 ||
                !WFMappingDownloadAssembler.TryCreate(ev.TransferId, ev.FileName, ev.ChunkCount, out var assembler))
            {
                Fail(ev.TransferId, "wf-mapping-mode-download-bad-data");
                return;
            }

            if (ev.FileIndex == 0 || Download is not { Active: true } running ||
                running.MapName != ev.MapName || running.Version != ev.Version)
            {
                _written.Clear();
                Download = new WFMappingDownloadProgress
                {
                    MapName = ev.MapName,
                    Version = ev.Version,
                    Active = true,
                };
            }

            _assembler = assembler;
        }

        if (_assembler.Add(ev.Chunk, ev.ChunkCount, ev.Data) != WFMappingChunkResult.Accepted)
        {
            Fail(ev.TransferId, "wf-mapping-mode-download-bad-data");
            return;
        }

        var progress = Download!;
        progress.FileIndex = ev.FileIndex;
        progress.FileCount = ev.FileCount;
        progress.Chunks = _assembler.Received;
        progress.ChunkCount = ev.ChunkCount;

        if (_assembler.TryComplete(out var data))
        {
            var path = WFMappingLibraryNames.DownloadPath(ev.MapName, ev.Version, _assembler.FileName);
            if (!TryWrite(path, data))
            {
                Fail(ev.TransferId, "wf-mapping-mode-download-write-failed");
                return;
            }

            _assembler = null;
            if (ev.FileIndex == ev.FileCount - 1)
                progress.Active = false;
        }

        RaiseNetworkEvent(new WFMappingDownloadAckEvent(ev.TransferId, ev.Chunk));
        DownloadUpdated?.Invoke();
    }

    private bool TryWrite(ResPath path, byte[] data)
    {
        try
        {
            _res.UserData.CreateDir(path.Directory);
            using (var stream = _res.UserData.OpenWrite(path))
            {
                stream.Write(data, 0, data.Length);
            }

            _written.Add(path);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Writing the mapping download {path} failed: {e}");
            return false;
        }
    }

    /// <summary>Stops a download the client can't take, tells the player and drops what it wrote.</summary>
    private void Fail(int transferId, string reason)
    {
        _droppedTransfer = transferId;
        var wasActive = _assembler != null || Download is { Active: true };
        _assembler = null;
        DropWritten();
        Download = null;

        if (wasActive)
            CancelDownload();

        Report(new WFMappingResult(reason, Loc.GetString(reason), false));
        DownloadUpdated?.Invoke();
    }

    private void OnDownloadCancelled(WFMappingDownloadCancelledEvent ev)
    {
        if (Download is not { Active: true } && _assembler == null)
            return;

        _assembler = null;
        DropWritten();
        Download = null;
        DownloadUpdated?.Invoke();
    }

    /// <summary>Deletes the files an unfinished download already wrote.</summary>
    private void DropWritten()
    {
        foreach (var path in _written)
        {
            try
            {
                _res.UserData.Delete(path);
            }
            catch (Exception e)
            {
                Log.Warning($"Removing the partial mapping download {path} failed: {e}");
            }
        }

        _written.Clear();
    }
}
