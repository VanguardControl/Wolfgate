using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WG.Mentors;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Robust.Shared.Network;

namespace Content.Server._WG.Mentors;

/// <summary>
/// The roster's own changes: suspension while banned, inactivity, resigning and reactivating, promotion, staff notes,
/// and re-reading rows the panel wrote.
/// </summary>
public sealed partial class WGMentorSystem
{
    private void InitializeUpkeep()
    {
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        _ = SweepAsync();
    }

    /// <summary>Whether a player has a server ban or a mentor ban in force, and its reason and end.</summary>
    public async Task<(bool Banned, bool MentorBan, string Reason, DateTime? Until)> BanStateAsync(NetUserId user)
    {
        if (await _db.GetServerBanAsync(null, user, null, null) is { } ban)
            return (true, false, ban.Reason, ban.ExpirationTime?.UtcDateTime);

        var now = DateTimeOffset.UtcNow;
        var roleBans = await _db.GetServerRoleBansAsync(null, user, null, null, false);
        var mentorBan = roleBans
            .Where(b => b.Role == MentorBans.MentorRole && (b.ExpirationTime == null || b.ExpirationTime > now))
            .MaxBy(b => b.ExpirationTime ?? DateTimeOffset.MaxValue);

        return mentorBan == null
            ? (false, false, "", null)
            : (true, true, mentorBan.Reason, mentorBan.ExpirationTime?.UtcDateTime);
    }

    /// <summary>A mentor ban was just placed: suspends the mentor and tells them. Called by the ban manager.</summary>
    public void OnMentorRoleBan(NetUserId user, string role, DateTimeOffset? until, string reason)
    {
        if (role != MentorBans.MentorRole)
            return;

        _ = Guard(async () =>
        {
            if (!await SuspendAsync(user, null, $"mentor ban: {reason}", notify: false))
                return;

            Notify(user, until == null
                ? Loc.GetString("wg-mentor-banned-permanent", ("reason", reason))
                : Loc.GetString("wg-mentor-banned", ("until", until.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm")), ("reason", reason)));
        });
    }

    /// <summary>Suspends an active mentor. False when they weren't active.</summary>
    public Task<bool> SuspendAsync(NetUserId user, NetUserId? actor, string reason, bool notify = true)
    {
        return Serialized(user, () => SetStateCore(user, actor, MentorRows.StateActive, MentorRows.StateSuspended,
            "suspended", reason, notify ? "wg-mentor-suspended" : null));
    }

    /// <summary>Marks an active mentor inactive after a long absence (D14).</summary>
    public Task<bool> MarkInactiveAsync(NetUserId user)
    {
        var days = _cfg.GetCVar(MentorCVars.InactiveDays);
        return Serialized(user, async () =>
        {
            var changed = await SetStateCore(user, null, MentorRows.StateActive, MentorRows.StateInactive, "inactive",
                $"no login for {days} days", null);

            if (changed)
                Notify(user, Loc.GetString("wg-mentor-inactive", ("days", days)));

            return changed;
        });
    }

    /// <summary>The player leaves the roster themselves. False when they weren't on it.</summary>
    public Task<bool> ResignAsync(NetUserId user)
    {
        return Serialized(user, async () =>
        {
            var row = await _db.GetMentorAsync(user.UserId);
            if (row == null || !MentorRows.OnRoster(row))
                return false;

            row.State = MentorRows.StateResigned;
            MentorRows.AddHistory(row, DateTime.UtcNow, user.UserId, MentorRows.StateResigned);
            await _db.SaveMentorAsync(row);

            _adminLog.Add(LogType.WGMentor, LogImpact.Medium, $"{Name(user)} resigned as a mentor");
            Notify(user, Loc.GetString("wg-mentor-resigned"));
            Commit(user, row);
            return true;
        });
    }

    /// <summary>An inactive mentor comes back themselves. Suspended ones need staff. False when they weren't inactive.</summary>
    public Task<bool> ReactivateAsync(NetUserId user)
    {
        return Serialized(user, async () =>
        {
            var row = await _db.GetMentorAsync(user.UserId);
            if (row == null || row.State != MentorRows.StateInactive)
                return false;

            row.State = MentorRows.StateActive;
            row.LastActiveAt = DateTime.UtcNow;
            MentorRows.AddHistory(row, row.LastActiveAt, user.UserId, "reactivated");
            await _db.SaveMentorAsync(row);

            _adminLog.Add(LogType.WGMentor, LogImpact.Low, $"{Name(user)} reactivated as a mentor");
            Notify(user, Loc.GetString("wg-mentor-restored"));
            Commit(user, row);
            return true;
        });
    }

    /// <summary>Replaces the staff notes on a roster row. False when the player has no row.</summary>
    public Task<bool> SetNotesAsync(NetUserId user, string notes, NetUserId? actor)
    {
        return Serialized(user, async () =>
        {
            var row = await _db.GetMentorAsync(user.UserId);
            if (row == null)
                return false;

            row.StaffNotes = notes;
            await _db.SaveMentorAsync(row);
            _adminLog.Add(LogType.WGMentor, LogImpact.Low, $"{Name(actor)} changed the mentor notes of {Name(user)}");
            Commit(user, row);
            return true;
        });
    }

    /// <summary>Re-reads rows written outside the game, for connected players; every connected player when null.</summary>
    public async Task<int> RefreshAsync(IReadOnlyCollection<Guid>? users)
    {
        var targets = (users ?? _players.Sessions.Select(s => s.UserId.UserId).ToList())
            .Select(u => new NetUserId(u))
            .Where(u => _players.TryGetSessionById(u, out _))
            .ToList();

        foreach (var user in targets)
        {
            await Serialized(user, async () =>
            {
                Commit(user, await _db.GetMentorAsync(user.UserId));
                return true;
            });
        }

        return targets.Count;
    }

    /// <summary>
    /// Promotes a Trainee to Mentor once they have the resolved tickets, the rating share and the days on the roster
    /// (D3). Run after each of their tickets is resolved or rated.
    /// </summary>
    public Task CheckPromotionAsync(NetUserId user)
    {
        if (!_active.TryGetValue(user, out var cached) || MentorRows.TierFromDb(cached.Tier) != MentorTier.Trainee)
            return Task.CompletedTask;

        return Guard(async () =>
        {
            var (resolved, rated, helpful) = await _db.GetMentorCreditAsync(user.UserId);
            await Serialized(user, async () =>
            {
                var row = await _db.GetMentorAsync(user.UserId);
                if (row == null || row.State != MentorRows.StateActive || MentorRows.TierFromDb(row.Tier) != MentorTier.Trainee)
                    return false;

                var now = DateTime.UtcNow;
                if (!MentorEligibility.Promotable(resolved, rated, helpful, now - row.GrantedAt,
                        _cfg.GetCVar(MentorCVars.PromoteTickets),
                        _cfg.GetCVar(MentorCVars.PromoteRating),
                        _cfg.GetCVar(MentorCVars.PromoteDays)))
                {
                    return false;
                }

                row.Tier = MentorRows.TierToDb(MentorTier.Mentor);
                row.PromotedAt = now;
                MentorRows.AddHistory(row, now, null, "promoted mentor", $"{resolved} resolved, {helpful} of {rated} rated helpful");
                await _db.SaveMentorAsync(row);

                _adminLog.Add(LogType.WGMentor, LogImpact.Medium, $"{Name(user)} was promoted to Mentor");
                Notify(user, Loc.GetString("wg-mentor-promoted"));
                Commit(user, row);
                return true;
            });
        });
    }

    /// <summary>On connect: an active mentor away too long goes inactive, a banned one is suspended, an inactive one is reminded.</summary>
    private async Task CheckOnConnectAsync(NetUserId user, WolfgateMentor row, DateTime lastActive)
    {
        if (row.State == MentorRows.StateInactive)
        {
            Notify(user, Loc.GetString("wg-mentor-page-inactive"));
            return;
        }

        if (row.State != MentorRows.StateActive)
            return;

        if (DateTime.UtcNow - lastActive > TimeSpan.FromDays(_cfg.GetCVar(MentorCVars.InactiveDays)))
        {
            await MarkInactiveAsync(user);
            return;
        }

        var ban = await BanStateAsync(user);
        if (ban.Banned)
            await SuspendAsync(user, null, ban.MentorBan ? $"mentor ban: {ban.Reason}" : $"ban: {ban.Reason}");
    }

    /// <summary>At round start: active mentors away too long go inactive, banned ones are suspended.</summary>
    private async Task SweepAsync()
    {
        try
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(_cfg.GetCVar(MentorCVars.InactiveDays));
            foreach (var row in await _db.GetMentorsAsync())
            {
                if (row.State != MentorRows.StateActive)
                    continue;

                var user = new NetUserId(row.UserId);
                if (row.LastActiveAt < cutoff && !_players.TryGetSessionById(user, out _))
                {
                    await MarkInactiveAsync(user);
                    continue;
                }

                var ban = await BanStateAsync(user);
                if (ban.Banned)
                    await SuspendAsync(user, null, ban.MentorBan ? $"mentor ban: {ban.Reason}" : $"ban: {ban.Reason}");
            }
        }
        catch (Exception e)
        {
            Log.Error($"The mentor roster sweep failed: {e}");
        }
    }

    private async Task<bool> SetStateCore(NetUserId user, NetUserId? actor, string from, string to, string what,
        string reason, string? notice)
    {
        var row = await _db.GetMentorAsync(user.UserId);
        if (row == null || row.State != from)
            return false;

        row.State = to;
        MentorRows.AddHistory(row, DateTime.UtcNow, actor?.UserId, what, reason);
        await _db.SaveMentorAsync(row);

        _adminLog.Add(LogType.WGMentor, LogImpact.Medium, $"{Name(actor)} set the mentor {Name(user)} {to}: {reason}");
        if (notice != null)
            Notify(user, Loc.GetString(notice));

        Commit(user, row);
        return true;
    }

    /// <summary>Runs background roster work and logs a failure instead of dropping it.</summary>
    private async Task Guard(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e)
        {
            Log.Error($"Mentor roster work failed: {e}");
        }
    }
}
