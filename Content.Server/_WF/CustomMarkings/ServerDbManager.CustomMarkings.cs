using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.CustomMarkings;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>A player's library of custom markings, oldest first.</summary>
    Task<List<WolfgateCustomMarking>> GetCustomMarkingsAsync(Guid userId, CancellationToken cancel = default);

    /// <summary>
    /// Adds a marking to a player's library, or changes their entry with this id. New art is stored under its
    /// hash; null art keeps the entry's own.
    /// </summary>
    /// <param name="limit">Most entries the library may hold; adding past it fails.</param>
    Task<CustomMarkingSaveResult> SaveCustomMarkingAsync(
        Guid userId,
        int id,
        string name,
        int placement,
        string? hash,
        byte[]? png,
        int limit,
        CancellationToken cancel = default);

    /// <summary>Removes an entry from a player's library. Its art stays, as saved characters may wear it.</summary>
    Task<bool> DeleteCustomMarkingAsync(Guid userId, int id, CancellationToken cancel = default);

    /// <summary>The PNG sheet stored under a hash, or null when there is none or it is blocked.</summary>
    Task<byte[]?> GetCustomMarkingArtAsync(string hash, CancellationToken cancel = default);

    /// <summary>Blocks or unblocks art. Returns who first saved it, or null when no art has this hash.</summary>
    Task<Guid?> SetCustomMarkingArtBlockedAsync(string hash, bool blocked, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<List<WolfgateCustomMarking>> GetCustomMarkingsAsync(Guid userId, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetCustomMarkingsAsync(userId, cancel));
    }

    public Task<CustomMarkingSaveResult> SaveCustomMarkingAsync(
        Guid userId,
        int id,
        string name,
        int placement,
        string? hash,
        byte[]? png,
        int limit,
        CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SaveCustomMarkingAsync(userId, id, name, placement, hash, png, limit, cancel));
    }

    public Task<bool> DeleteCustomMarkingAsync(Guid userId, int id, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteCustomMarkingAsync(userId, id, cancel));
    }

    public Task<byte[]?> GetCustomMarkingArtAsync(string hash, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetCustomMarkingArtAsync(hash, cancel));
    }

    public Task<Guid?> SetCustomMarkingArtBlockedAsync(string hash, bool blocked, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetCustomMarkingArtBlockedAsync(hash, blocked, cancel));
    }
}
