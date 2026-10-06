using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.CustomMarkings;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

// Custom marking art and each player's library of it.
public abstract partial class ServerDbBase
{
    public async Task<List<WolfgateCustomMarking>> GetCustomMarkingsAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        return await db.DbContext.WolfgateCustomMarking
            .Where(m => m.PlayerUserId == userId)
            .OrderBy(m => m.Id)
            .ToListAsync(cancel);
    }

    public async Task<CustomMarkingSaveResult> SaveCustomMarkingAsync(
        Guid userId,
        int id,
        string name,
        int placement,
        string? hash,
        byte[]? png,
        int limit,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;

        WolfgateCustomMarking? entry = null;
        if (id != 0)
        {
            entry = await ctx.WolfgateCustomMarking.SingleOrDefaultAsync(m => m.Id == id && m.PlayerUserId == userId, cancel);
            if (entry == null)
                return CustomMarkingSaveResult.Fail("wf-custom-marking-error-missing");
        }
        else if (hash == null || png == null)
        {
            return CustomMarkingSaveResult.Fail("wf-custom-marking-error-invalid");
        }
        else if (await ctx.WolfgateCustomMarking.CountAsync(m => m.PlayerUserId == userId, cancel) >= limit)
        {
            return CustomMarkingSaveResult.Fail("wf-custom-marking-error-full");
        }

        string? previous = null;
        if (hash != null && png != null)
        {
            var blocked = await ctx.WolfgateCustomMarkingArt
                .Where(a => a.Hash == hash)
                .Select(a => (bool?) a.Blocked)
                .SingleOrDefaultAsync(cancel);

            if (blocked == true)
                return CustomMarkingSaveResult.Fail("wf-custom-marking-error-blocked");

            if (blocked == null)
            {
                ctx.WolfgateCustomMarkingArt.Add(new WolfgateCustomMarkingArt
                {
                    Hash = hash,
                    Png = png,
                    UploaderUserId = userId,
                    UploadedAt = DateTime.UtcNow,
                });
            }

            if (entry != null && entry.ArtHash != hash)
                previous = entry.ArtHash;
        }

        if (entry == null)
        {
            entry = new WolfgateCustomMarking { PlayerUserId = userId };
            ctx.WolfgateCustomMarking.Add(entry);
        }

        if (hash != null)
            entry.ArtHash = hash;

        entry.Name = name;
        entry.Placement = placement;
        entry.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(cancel);

        return new CustomMarkingSaveResult(entry, previous, null);
    }

    public async Task<bool> DeleteCustomMarkingAsync(Guid userId, int id, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var entry = await db.DbContext.WolfgateCustomMarking
            .SingleOrDefaultAsync(m => m.Id == id && m.PlayerUserId == userId, cancel);

        if (entry == null)
            return false;

        db.DbContext.WolfgateCustomMarking.Remove(entry);
        await db.DbContext.SaveChangesAsync(cancel);
        return true;
    }

    public async Task<byte[]?> GetCustomMarkingArtAsync(string hash, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        return await db.DbContext.WolfgateCustomMarkingArt
            .Where(a => a.Hash == hash && !a.Blocked)
            .Select(a => a.Png)
            .SingleOrDefaultAsync(cancel);
    }

    public async Task<Guid?> SetCustomMarkingArtBlockedAsync(string hash, bool blocked, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var art = await db.DbContext.WolfgateCustomMarkingArt.SingleOrDefaultAsync(a => a.Hash == hash, cancel);
        if (art == null)
            return null;

        art.Blocked = blocked;
        await db.DbContext.SaveChangesAsync(cancel);
        return art.UploaderUserId;
    }
}
