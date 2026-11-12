namespace Content.Server.Decals;

public sealed partial class DecalSystem
{
    /// <summary>Ticks between decal sends to players; chunks dirtied in between wait and go in one message.</summary>
    public const int WfDecalFlushTicks = 3;

    /// <summary>Chunks dirtied since the last send, per grid.</summary>
    private readonly Dictionary<NetEntity, HashSet<Vector2i>> _wfPendingChunks = new();

    private int _wfDecalTicks;

    /// <summary>
    /// Moves this tick's dirty chunks into the pending set. On a send tick moves every pending chunk back into
    /// <c>_dirtyChunks</c> and returns true, so the players' update sends them; otherwise clears it and returns false.
    /// </summary>
    private bool WfGatherDirtyChunks()
    {
        foreach (var (grid, chunks) in _dirtyChunks)
        {
            if (_wfPendingChunks.TryGetValue(grid, out var pending))
                pending.UnionWith(chunks);
            else
                _wfPendingChunks[grid] = chunks;
        }

        _dirtyChunks.Clear();

        if (++_wfDecalTicks < WfDecalFlushTicks)
            return false;

        _wfDecalTicks = 0;
        foreach (var (grid, chunks) in _wfPendingChunks)
        {
            _dirtyChunks[grid] = chunks;
        }

        _wfPendingChunks.Clear();
        return true;
    }
}
