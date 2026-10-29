using Content.Shared._WF.GroundCover;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.GroundCover;

public sealed partial class WFGroundCoverSystem
{
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>Half the side of the square around a player whose snow chunks are worked and sent, in tiles.</summary>
    public const float ViewRange = 40f;

    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(30);

    /// <summary>The chunk versions each session was last sent; a chunk it holds that's gone is sent as cleared.</summary>
    private readonly Dictionary<ICommonSession, Dictionary<(EntityUid Grid, Vector2i Chunk), uint>> _sent = new();

    private readonly Dictionary<EntityUid, WFSnowCoverChunksMessage> _messages = new();
    private readonly List<(EntityUid Grid, Vector2i Chunk)> _stale = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private TimeSpan _nextSend;
    private TimeSpan _nextPrune;

    private void InitializeNetwork()
    {
        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    private void ShutdownNetwork()
    {
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
        _sent.Clear();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus is SessionStatus.Disconnected or SessionStatus.Connected)
            _sent.Remove(args.Session);
    }

    private void UpdateNetwork(TimeSpan now)
    {
        if (now < _nextSend)
            return;

        _nextSend = now + SendInterval;
        var prune = now >= _nextPrune;
        if (prune)
            _nextPrune = now + PruneInterval;

        foreach (var session in _player.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            if (!_sent.TryGetValue(session, out var sent))
            {
                sent = new Dictionary<(EntityUid, Vector2i), uint>();
                _sent[session] = sent;
            }

            if (prune)
                Prune(sent);

            _view.Clear();
            CollectView(session, _view);
            SendView(session, sent);
        }
    }

    /// <summary>Sends a session the chunks in its view that changed since it was last sent them.</summary>
    private void SendView(ICommonSession session, Dictionary<(EntityUid Grid, Vector2i Chunk), uint> sent)
    {
        _messages.Clear();
        foreach (var key in _view)
        {
            WFSnowChunk? chunk = null;
            if (TryComp<WFSnowCoverComponent>(key.Grid, out var cover))
                cover.Chunks.TryGetValue(key.Chunk, out chunk);

            var had = sent.TryGetValue(key, out var version);
            byte[]? depths;
            if (chunk == null)
            {
                if (!had)
                    continue;

                sent.Remove(key);
                depths = null;
            }
            else
            {
                if (had && version == chunk.Version)
                    continue;

                sent[key] = chunk.Version;
                depths = (byte[]) chunk.Depths.Clone();
            }

            if (!_messages.TryGetValue(key.Grid, out var message))
            {
                message = new WFSnowCoverChunksMessage { Grid = GetNetEntity(key.Grid) };
                _messages[key.Grid] = message;
            }

            message.Chunks.Add(new WFSnowChunkData(key.Chunk, depths));
        }

        foreach (var message in _messages.Values)
        {
            RaiseNetworkEvent(message, session);
        }
    }

    /// <summary>Forgets what a session was sent of grids that are gone.</summary>
    private void Prune(Dictionary<(EntityUid Grid, Vector2i Chunk), uint> sent)
    {
        _stale.Clear();
        foreach (var key in sent.Keys)
        {
            if (TerminatingOrDeleted(key.Grid))
                _stale.Add(key);
        }

        foreach (var key in _stale)
        {
            sent.Remove(key);
        }
    }
}
