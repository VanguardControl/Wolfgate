using System.Linq;
using Content.Server.Administration.Logs;
using Content.Shared._WF.MappingMode;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// Streams a library version's level files to their owner, one transfer per file and one download per player, in
/// chunks paced by the client's acknowledgements.
/// </summary>
public sealed class WFMappingDownloadSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private WFMappingLibrarySystem _library = default!;
    [Dependency] private WFMappingModeSystem _mapping = default!;

    /// <summary>Chunks sent ahead of the last acknowledged one.</summary>
    private const int InFlight = 2;

    /// <summary>A download whose client acknowledges nothing for this long is dropped.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>One player's download: the version's files and where the current one stands.</summary>
    private sealed class Download(ICommonSession player, string mapName, string version, List<(string Name, ResPath Path)> files)
    {
        public readonly ICommonSession Player = player;
        public readonly string MapName = mapName;
        public readonly string Version = version;
        public readonly List<(string Name, ResPath Path)> Files = files;

        public int FileIndex = -1;
        public int TransferId;
        public byte[] Data = Array.Empty<byte>();
        public int ChunkCount;
        public int NextChunk;
        public int Acked;
        public TimeSpan LastActivity;
    }

    private readonly Dictionary<NetUserId, Download> _downloads = new();
    private readonly WFMappingThrottle<NetUserId> _requestThrottle = new(1, 1);
    private int _nextTransferId = 1;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFMappingDownloadRequest>(OnRequest);
        SubscribeNetworkEvent<WFMappingDownloadAckEvent>(OnAck);
        SubscribeNetworkEvent<WFMappingDownloadCancelRequest>(OnCancelRequest);
        SubscribeLocalEvent<WFMappingMembershipEndedEvent>(OnMembershipEnded);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_downloads.Count == 0)
            return;

        var now = _timing.RealTime;
        foreach (var (user, download) in _downloads.ToList())
        {
            if (now - download.LastActivity > StallTimeout)
                Cancel(user, "wf-mapping-mode-download-stalled");
        }
    }

    private void OnRequest(WFMappingDownloadRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_requestThrottle.TryTake(player.UserId, _timing.CurTime))
            return;

        if (_downloads.ContainsKey(player.UserId))
        {
            _mapping.Notify(player, false, "wf-mapping-mode-download-busy");
            return;
        }

        try
        {
            if (!_library.TryFindVersion(player.UserId, ev.MapName, ev.Version, out var map, out var version))
            {
                _mapping.Notify(player, false, "wf-mapping-mode-library-not-found");
                return;
            }

            var files = version.Levels
                .Select(pair => (WFMappingLibraryNames.LevelFile(pair.Key), pair.Value))
                .ToList();

            var download = new Download(player, map.Name, version.Name.Folder, files)
            {
                LastActivity = _timing.RealTime,
            };

            _downloads[player.UserId] = download;
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{player:player} downloads their library map \"{map.Name}\" {version.Name.Folder} ({files.Count} files)");
            NextFile(download);
        }
        catch (Exception e)
        {
            Log.Error($"Starting a mapping library download for {player} failed: {e}");
            if (_downloads.ContainsKey(player.UserId))
                Cancel(player.UserId, "wf-mapping-mode-download-failed");
            else
                _mapping.Notify(player, false, "wf-mapping-mode-download-failed");
        }
    }

    /// <summary>Reads the next file and starts sending it, or finishes the download after the last.</summary>
    private void NextFile(Download download)
    {
        var user = download.Player.UserId;
        download.FileIndex++;
        if (download.FileIndex >= download.Files.Count)
        {
            _downloads.Remove(user);
            _mapping.Notify(download.Player, true, "wf-mapping-mode-download-done", ("name", download.MapName),
                ("version", download.Version), ("count", download.Files.Count));
            return;
        }

        var (name, path) = download.Files[download.FileIndex];
        using (var stream = _res.UserData.OpenRead(path))
        {
            if (stream.Length > WFMappingDownloadAssembler.MaxFileBytes)
            {
                Cancel(user, "wf-mapping-mode-download-too-large",
                    ("max", WFMappingDownloadAssembler.MaxFileBytes / (1024 * 1024)));
                return;
            }

            download.Data = new byte[stream.Length];
            stream.ReadExactly(download.Data);
        }

        download.TransferId = _nextTransferId++;
        download.ChunkCount = Math.Max(1,
            (download.Data.Length + WFMappingDownloadAssembler.ChunkSize - 1) / WFMappingDownloadAssembler.ChunkSize);
        download.NextChunk = 0;
        download.Acked = 0;
        Log.Debug($"Sending {name} of \"{download.MapName}\" {download.Version} to {download.Player} in {download.ChunkCount} chunks");
        Pump(download);
    }

    /// <summary>Sends chunks until <see cref="InFlight"/> are unacknowledged or the file is all sent.</summary>
    private void Pump(Download download)
    {
        var (name, _) = download.Files[download.FileIndex];
        while (download.NextChunk < download.ChunkCount && download.NextChunk - download.Acked < InFlight)
        {
            var offset = download.NextChunk * WFMappingDownloadAssembler.ChunkSize;
            var length = Math.Min(WFMappingDownloadAssembler.ChunkSize, download.Data.Length - offset);
            var data = download.Data.AsSpan(offset, length).ToArray();

            RaiseNetworkEvent(new WFMappingDownloadChunkEvent(download.TransferId, download.MapName, download.Version,
                name, download.FileIndex, download.Files.Count, download.NextChunk, download.ChunkCount, data),
                download.Player);
            download.NextChunk++;
        }

        download.LastActivity = _timing.RealTime;
    }

    private void OnAck(WFMappingDownloadAckEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (!_downloads.TryGetValue(user, out var download) ||
            ev.TransferId != download.TransferId ||
            ev.Chunk != download.Acked ||
            ev.Chunk >= download.NextChunk)
        {
            return;
        }

        try
        {
            download.Acked++;
            if (download.Acked >= download.ChunkCount)
                NextFile(download);
            else
                Pump(download);
        }
        catch (Exception e)
        {
            Log.Error($"Mapping library download for {args.SenderSession} failed: {e}");
            Cancel(user, "wf-mapping-mode-download-failed");
        }
    }

    private void OnCancelRequest(WFMappingDownloadCancelRequest ev, EntitySessionEventArgs args)
    {
        Cancel(args.SenderSession.UserId, "wf-mapping-mode-download-cancelled");
    }

    private void OnMembershipEnded(WFMappingMembershipEndedEvent ev)
    {
        Cancel(ev.User, "wf-mapping-mode-download-cancelled");
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        _downloads.Remove(args.Session.UserId);
        _requestThrottle.Forget(args.Session.UserId);
    }

    /// <summary>Drops a player's download, tells their client to discard it, and tells them why.</summary>
    private void Cancel(NetUserId user, string reason, params (string, object)[] args)
    {
        if (!_downloads.Remove(user, out var download))
            return;

        if (download.Player.Status == SessionStatus.Disconnected)
            return;

        RaiseNetworkEvent(new WFMappingDownloadCancelledEvent(download.TransferId), download.Player);
        _mapping.Notify(download.Player, false, reason, args);
    }
}
