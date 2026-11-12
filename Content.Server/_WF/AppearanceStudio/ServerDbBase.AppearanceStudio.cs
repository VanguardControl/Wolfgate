using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.CustomMarkings;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

// Studio warps, stored as custom marking art so they share its cleanup and blocking.
public abstract partial class ServerDbBase
{
    public async Task<int> CountCustomMarkingArtSinceAsync(Guid userId, DateTime since, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateCustomMarkingArt.CountAsync(a => a.UploaderUserId == userId && a.UploadedAt > since, cancel);
    }

    public async Task<string?> StoreStudioWarpAsync(
        Guid userId,
        string hash,
        CustomMarkingStoredArt art,
        int dailyArtLimit,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;

        var blocked = await ctx.WolfgateCustomMarkingArt
            .Where(a => a.Hash == hash)
            .Select(a => (bool?) a.Blocked)
            .SingleOrDefaultAsync(cancel);

        if (blocked == true)
            return "wf-studio-error-blocked";

        if (blocked == false)
            return null;

        if (dailyArtLimit > 0)
        {
            var since = DateTime.UtcNow - TimeSpan.FromDays(1);
            var today = await ctx.WolfgateCustomMarkingArt
                .CountAsync(a => a.UploaderUserId == userId && a.UploadedAt > since, cancel);

            if (today >= dailyArtLimit)
                return "wf-studio-error-daily";
        }

        ctx.WolfgateCustomMarkingArt.Add(new WolfgateCustomMarkingArt
        {
            Hash = hash,
            Png = art.Png,
            UploaderUserId = userId,
            UploadedAt = DateTime.UtcNow,
        });

        await ctx.SaveChangesAsync(cancel);
        return null;
    }
}

public partial interface IServerDbManager
{
    /// <summary>How much new art (custom markings and warps) a player stored since a time, for the daily limit.</summary>
    Task<int> CountCustomMarkingArtSinceAsync(Guid userId, DateTime since, CancellationToken cancel = default);

    /// <summary>
    /// Stores a warp under its hash unless the server holds it already. Returns a locale id when it can't: the art
    /// is blocked or the player stored their day's worth of new art.
    /// </summary>
    Task<string?> StoreStudioWarpAsync(Guid userId, string hash, CustomMarkingStoredArt art, int dailyArtLimit, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<int> CountCustomMarkingArtSinceAsync(Guid userId, DateTime since, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.CountCustomMarkingArtSinceAsync(userId, since, cancel));
    }

    public Task<string?> StoreStudioWarpAsync(Guid userId, string hash, CustomMarkingStoredArt art, int dailyArtLimit, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.StoreStudioWarpAsync(userId, hash, art, dailyArtLimit, cancel));
    }
}
