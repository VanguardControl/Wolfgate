using System.Globalization;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Shared._WF.MappingMode;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map.Events;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// Takes map files players upload into their own library: one transfer per player in chunks acknowledged one by
/// one, then <see cref="WFMappingUploadValidator"/>, a load onto a scratch map that strips and caps it like a
/// reference, and a library version holding the engine's save of that map, never the uploaded bytes.
/// </summary>
public sealed class WFMappingUploadSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WFMappingLibrarySystem _library = default!;
    [Dependency] private WFMappingModeSystem _mapping = default!;

    /// <summary>An upload whose client sends nothing for this long is dropped.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>One player's upload: the map it goes to and the chunks so far.</summary>
    private sealed class Upload(ICommonSession player, string mapName, WFMappingUploadAssembler assembler)
    {
        public readonly ICommonSession Player = player;
        public readonly string MapName = mapName;
        public readonly WFMappingUploadAssembler Assembler = assembler;
        public TimeSpan LastActivity;
    }

    private readonly Dictionary<NetUserId, Upload> _uploads = new();

    /// <summary>Game time from which each player may start another upload.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _nextUpload = new();

    /// <summary>The Fluent id of each player's last upload outcome, for admins and tests.</summary>
    private readonly Dictionary<NetUserId, string> _lastOutcome = new();

    private readonly WFMappingThrottle<NetUserId> _requestThrottle = new(2, 2);
    private int _nextTransferId = 1;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFMappingUploadBeginRequest>(OnBegin);
        SubscribeNetworkEvent<WFMappingUploadChunkEvent>(OnChunk);
        SubscribeNetworkEvent<WFMappingUploadCancelRequest>(OnCancelRequest);

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

        if (_uploads.Count == 0)
            return;

        var now = _timing.RealTime;
        foreach (var (user, upload) in _uploads.ToList())
        {
            if (now - upload.LastActivity > StallTimeout)
                Cancel(user, "wf-mapping-mode-upload-stalled");
        }
    }

    /// <summary>The transfer id of a player's running upload.</summary>
    public int? ActiveTransfer(NetUserId user)
    {
        return _uploads.TryGetValue(user, out var upload) ? upload.Assembler.TransferId : null;
    }

    /// <summary>The Fluent id of a player's last upload outcome, success or refusal.</summary>
    public string? LastOutcome(NetUserId user)
    {
        return _lastOutcome.GetValueOrDefault(user);
    }

    private void OnBegin(WFMappingUploadBeginRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_requestThrottle.TryTake(player.UserId, _timing.CurTime))
        {
            Log.Debug($"Dropped an upload request from {player}: too many");
            return;
        }

        try
        {
            Begin(player, ev);
        }
        catch (Exception e)
        {
            Log.Error($"Starting a mapping upload for {player} failed: {e}");
            _uploads.Remove(player.UserId);
            Refuse(player, 0, "wf-mapping-mode-upload-failed");
        }
    }

    private void Begin(ICommonSession player, WFMappingUploadBeginRequest ev)
    {
        var user = player.UserId;
        if (!_mapping.Enabled)
        {
            Refuse(player, 0, "wf-mapping-mode-off");
            return;
        }

        var now = _timing.CurTime;
        if (_nextUpload.TryGetValue(user, out var next) && now < next)
        {
            Refuse(player, 0, "wf-mapping-mode-upload-cooldown",
                ("seconds", (int) Math.Ceiling((next - now).TotalSeconds)));
            return;
        }

        if (_uploads.ContainsKey(user))
        {
            Refuse(player, 0, "wf-mapping-mode-upload-busy");
            return;
        }

        if (!WFMappingLibraryNames.TryNormalizeMapName(ev.Name, out var name))
        {
            Refuse(player, 0, "wf-mapping-mode-bad-name", ("max", WFMappingModeLimits.MaxNameLength));
            return;
        }

        var cap = WFMappingUploadAssembler.Cap(_cfg.GetCVar(WFMappingModeCVars.MaxUploadBytes));
        if (ev.Bytes > cap)
        {
            Refuse(player, 0, "wf-mapping-mode-upload-too-large", ("max", FormatMiB(cap)));
            return;
        }

        if (!WFMappingUploadAssembler.TryCreate(_nextTransferId++, ev.Bytes, ev.ChunkCount, cap, out var assembler))
        {
            Refuse(player, 0, "wf-mapping-mode-upload-bad-data");
            return;
        }

        if (!_library.HasRoomForMap(user, name, out var maxMaps))
        {
            Refuse(player, 0, "wf-mapping-mode-library-full-maps", ("max", maxMaps));
            return;
        }

        _uploads[user] = new Upload(player, name, assembler) { LastActivity = _timing.RealTime };
        _nextUpload[user] = now + TimeSpan.FromSeconds(Math.Max(0f, _cfg.GetCVar(WFMappingModeCVars.UploadCooldown)));
        Log.Info($"{player} starts uploading \"{name}\": {ev.Bytes} bytes in {ev.ChunkCount} chunks");
        RaiseNetworkEvent(new WFMappingUploadReadyEvent(assembler.TransferId), player);
    }

    private void OnChunk(WFMappingUploadChunkEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (!_uploads.TryGetValue(user, out var upload) || ev.TransferId != upload.Assembler.TransferId)
            return;

        try
        {
            if (upload.Assembler.Add(ev.Chunk, ev.Data) != WFMappingChunkResult.Accepted)
            {
                Cancel(user, "wf-mapping-mode-upload-bad-data");
                return;
            }

            upload.LastActivity = _timing.RealTime;
            RaiseNetworkEvent(new WFMappingUploadAckEvent(ev.TransferId, ev.Chunk), upload.Player);
            if (upload.Assembler.IsComplete)
                Finish(upload);
        }
        catch (Exception e)
        {
            Log.Error($"Mapping upload from {args.SenderSession} failed: {e}");
            if (_uploads.ContainsKey(user))
                Cancel(user, "wf-mapping-mode-upload-failed");
            else
                Fail(upload, upload.Assembler.Bytes, "wf-mapping-mode-upload-failed");
        }
    }

    /// <summary>Validates the complete file, loads it aside, and stores the result as a library version.</summary>
    private void Finish(Upload upload)
    {
        var player = upload.Player;
        var user = player.UserId;
        var transfer = upload.Assembler.TransferId;
        _uploads.Remove(user);

        if (!upload.Assembler.TryComplete(out var bytes))
        {
            Fail(upload, upload.Assembler.Bytes, "wf-mapping-mode-upload-bad-data");
            return;
        }

        var migrations = new BeforeEntityReadEvent();
        RaiseLocalEvent(migrations);
        if (!WFMappingUploadValidator.TryValidate(bytes, _prototypes, migrations.RenamedPrototypes,
                migrations.DeletedPrototypes, _cfg.GetCVar(WFMappingModeCVars.MaxEntities), out var root,
                out var error, out var errorArgs))
        {
            Fail(upload, bytes.Length, error, errorArgs);
            return;
        }

        // A full load slot isn't the player's fault, so it doesn't cost them their cooldown.
        if (!_mapping.TryReserveLoad())
        {
            _nextUpload.Remove(user);
            Fail(upload, bytes.Length, "wf-mapping-mode-upload-load-busy");
            return;
        }

        if (!_mapping.TryLoadOntoScratch(root, $"upload:{user}", player, null, true, "wf-mapping-mode-upload-load-failed",
                out var load, out var refusal))
        {
            Fail(upload, bytes.Length, refusal.Key, refusal.Args);
            return;
        }

        string? version;
        string saveError;
        (string, object)[] saveArgs;
        try
        {
            _library.SaveUpload(user, upload.MapName, load.Map, out version, out saveError, out saveArgs);
        }
        finally
        {
            if (!TerminatingOrDeleted(load.Map))
                Del(load.Map);
        }

        if (version == null)
        {
            Fail(upload, bytes.Length, saveError, saveArgs);
            return;
        }

        _lastOutcome[user] = "wf-mapping-mode-upload-done";
        Log.Info($"{player} uploaded \"{upload.MapName}\" {version}: {bytes.Length} bytes, {load.Grids.Count} grids, " +
                 $"{load.Entities} entities, {load.Stripped} denied entities removed");
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} uploaded \"{upload.MapName}\" ({bytes.Length} bytes) to their library as {version}");
        RaiseNetworkEvent(new WFMappingUploadDoneEvent(transfer, upload.MapName, version), player);
        _mapping.Notify(player, true, "wf-mapping-mode-upload-done", ("name", upload.MapName), ("version", version));
        _library.SendLibrary(player);
    }

    private void OnCancelRequest(WFMappingUploadCancelRequest ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (_uploads.TryGetValue(user, out var upload) && upload.Assembler.TransferId == ev.TransferId)
            Cancel(user, "wf-mapping-mode-upload-cancelled");
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        var user = args.Session.UserId;
        if (_uploads.Remove(user, out var upload))
            LogOutcome(upload, upload.Assembler.Bytes, "wf-mapping-mode-upload-cancelled", "the player disconnected");

        _requestThrottle.Forget(user);
        _lastOutcome.Remove(user);
    }

    /// <summary>Drops a player's running upload and tells them why.</summary>
    private void Cancel(NetUserId user, string reason, params (string, object)[] args)
    {
        if (_uploads.Remove(user, out var upload))
            Fail(upload, upload.Assembler.Bytes, reason, args);
    }

    /// <summary>Ends an upload that didn't make it: log lines, the cancelled event and the reason.</summary>
    private void Fail(Upload upload, int bytes, string reason, params (string, object)[] args)
    {
        LogOutcome(upload, bytes, reason, null);
        if (upload.Player.Status != SessionStatus.Disconnected)
            Refuse(upload.Player, upload.Assembler.TransferId, reason, args);
    }

    private void LogOutcome(Upload upload, int bytes, string reason, string? note)
    {
        Log.Info($"Upload of \"{upload.MapName}\" from {upload.Player} refused: {note ?? reason}");
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{upload.Player:player} failed to upload \"{upload.MapName}\" ({bytes} bytes): {note ?? reason}");
    }

    /// <summary>Tells the client its upload is over and the player why.</summary>
    private void Refuse(ICommonSession player, int transferId, string reason, params (string, object)[] args)
    {
        _lastOutcome[player.UserId] = reason;
        if (transferId == 0)
            Log.Info($"Refused an upload from {player}: {reason}");

        RaiseNetworkEvent(new WFMappingUploadCancelledEvent(transferId), player);
        _mapping.Notify(player, false, reason, args);
    }

    private static string FormatMiB(long bytes)
    {
        return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture);
    }
}
