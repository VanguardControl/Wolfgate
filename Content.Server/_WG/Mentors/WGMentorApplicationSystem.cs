using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Symphony;
using Content.Shared._WG.Mentors;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Players.PlayTimeTracking;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.Mentors;

/// <summary>
/// Becoming a mentor: the Mentoring page's state, the 100 h notice, applying under the server's mode (auto, review,
/// hybrid), reviews by staff and Senior Mentors, resigning, reactivating and linking Discord.
/// </summary>
public sealed class WGMentorApplicationSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private WGMentorSystem _mentors = default!;
    [Dependency] private INetConfigurationManager _netCfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PlayTimeTrackingManager _playTime = default!;

    public const string StatePending = "pending";
    public const string StateApproved = "approved";
    public const string StateAutoApproved = "auto_approved";
    public const string StateDenied = "denied";

    /// <summary>Raised on the main thread when the pending applications change, for the staff tab.</summary>
    public event Action? ApplicationsChanged;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMentorPageRequestEvent>((ev, args) => _ = Guard(() => SendPageAsync(args.SenderSession)));
        SubscribeNetworkEvent<WGMentorApplyEvent>((ev, args) => _ = Guard(() => ApplyAsync(args.SenderSession, ev)));
        SubscribeNetworkEvent<WGMentorPageActionEvent>((ev, args) => _ = Guard(() => ActAsync(args.SenderSession, ev.Action)));
        SubscribeLocalEvent<RoundStartedEvent>(_ => CheckNotices());
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    #region Page

    /// <summary>Works out where a player stands: their roster state, or what stops them applying.</summary>
    public async Task<WGMentorPageStateEvent> BuildPageAsync(NetUserId user, ICommonSession? session)
    {
        var state = new WGMentorPageStateEvent
        {
            NeededHours = _cfg.GetCVar(MentorCVars.MinHours),
            CleanDays = _cfg.GetCVar(MentorCVars.CleanDays),
            ReapplyDays = _cfg.GetCVar(MentorCVars.ReapplyDays),
        };

        if (!_cfg.GetCVar(MentorCVars.Enabled))
        {
            state.Status = MentorPageStatus.Disabled;
            return state;
        }

        var record = await GatherAsync(user, session);
        state.Hours = (float) record.Playtime.TotalHours;

        var row = await _db.GetMentorAsync(user.UserId);
        if (row != null && MentorRows.OnRoster(row))
        {
            state.Tier = MentorRows.TierFromDb(row.Tier);
            state.Status = row.State switch
            {
                MentorRows.StateInactive => MentorPageStatus.Inactive,
                MentorRows.StateSuspended => MentorPageStatus.Suspended,
                _ => MentorPageStatus.Active,
            };
        }
        else if ((await _db.GetMentorApplicationsAsync(StatePending, user.UserId)).Count > 0)
        {
            state.Status = MentorPageStatus.Pending;
        }
        else
        {
            state.Status = MentorPageStatus.Apply;
            var result = MentorEligibility.Check(record, state.NeededHours, DateTime.UtcNow);
            state.Blocks = result.Blocks;
            state.MentorBanUntil = record.MentorBanUntil;
            state.ReapplyAfter = record.ReapplyAfter;
        }

        state.Discord = await DiscordStateAsync(user);
        return state;
    }

    private async Task SendPageAsync(ICommonSession session)
    {
        var state = await BuildPageAsync(session.UserId, session);
        if (session.Status != SessionStatus.Disconnected)
            RaiseNetworkEvent(state, session);
    }

    private async Task<MentorDiscordState> DiscordStateAsync(NetUserId user)
    {
        if (_cfg.GetCVar(CCVars.SymphonyUrl).Trim().Length == 0)
            return MentorDiscordState.Unavailable;

        try
        {
            return await SymphonyLinkTickets.IsLinkedAsync(_cfg, user) switch
            {
                true => MentorDiscordState.Linked,
                false => MentorDiscordState.Unlinked,
                null => MentorDiscordState.Unavailable,
            };
        }
        catch (Exception e)
        {
            Log.Warning($"Could not read the Discord link of {user}: {e.Message}");
            return MentorDiscordState.Unavailable;
        }
    }

    /// <summary>Everything the application checks look at, from the database (and the live playtime when online).</summary>
    public async Task<MentorRecord> GatherAsync(NetUserId user, ICommonSession? session)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - TimeSpan.FromDays(_cfg.GetCVar(MentorCVars.CleanDays));
        var record = new MentorRecord();

        if (session != null && _playTime.TryGetTrackerTime(session, PlayTimeTrackingShared.TrackerOverall, out var live))
        {
            record.Playtime = live.Value;
        }
        else
        {
            record.Playtime = (await _db.GetPlayTimes(user.UserId))
                .Find(p => p.Tracker == PlayTimeTrackingShared.TrackerOverall)?.TimeSpent ?? TimeSpan.Zero;
        }

        var ban = await _mentors.BanStateAsync(user);
        record.MentorBanned = ban.MentorBan;
        record.MentorBanUntil = ban.MentorBan ? ban.Until : null;
        record.ActiveBan = (ban.Banned && !ban.MentorBan)
                           || (await _db.GetServerRoleBansAsync(null, user, null, null, false))
                           .Any(b => b.Role != MentorBans.MentorRole);

        foreach (var remark in await _db.GetAllAdminRemarks(user.UserId))
        {
            if (remark.Deleted || remark.CreatedAt.UtcDateTime < cutoff)
                continue;

            switch (remark)
            {
                case ServerBanNoteRecord:
                case ServerRoleBanNoteRecord:
                    record.RecentBans++;
                    break;
                case AdminNoteRecord { Severity: >= NoteSeverity.Medium }:
                    record.SeriousNotes++;
                    break;
                case AdminNoteRecord { Severity: NoteSeverity.Minor }:
                    record.MinorNotes++;
                    break;
            }
        }

        var row = await _db.GetMentorAsync(user.UserId);
        var reapplyDays = TimeSpan.FromDays(_cfg.GetCVar(MentorCVars.ReapplyDays));
        if (row != null && row.State is MentorRows.StateRevoked or MentorRows.StateResigned)
        {
            record.RevokedBefore = row.State == MentorRows.StateRevoked;
            if (MentorRows.LastEntryTime(row, row.State) is { } left)
                record.ReapplyAfter = left + reapplyDays;
        }
        else if (row != null && MentorRows.History(row).Any(e => e.What == MentorRows.StateRevoked))
        {
            record.RevokedBefore = true;
        }

        var denied = (await _db.GetMentorApplicationsAsync(StateDenied, user.UserId))
            .Select(a => a.ReapplyAfter)
            .Max();

        if (denied > record.ReapplyAfter || record.ReapplyAfter == null)
            record.ReapplyAfter = denied;

        return record;
    }

    #endregion

    #region Applying

    private async Task ApplyAsync(ICommonSession session, WGMentorApplyEvent ev)
    {
        var refusal = await ApplyCoreAsync(session.UserId, session, ev.Answers, ev.AgreedToCode);
        if (refusal != null)
            _chat.DispatchServerMessage(session, refusal);

        await SendPageAsync(session);
    }

    /// <summary>
    /// Takes an application: checks it, stores it, and grants Trainee at once or queues it by the server's mode.
    /// Returns a refusal to show the player, or null when it went in.
    /// </summary>
    public async Task<string?> ApplyCoreAsync(NetUserId user, ICommonSession? session, Dictionary<string, string> answers, bool agreed)
    {
        if (!_cfg.GetCVar(MentorCVars.Enabled) || !agreed)
            return null;

        var row = await _db.GetMentorAsync(user.UserId);
        if (row != null && MentorRows.OnRoster(row))
            return null;

        if ((await _db.GetMentorApplicationsAsync(StatePending, user.UserId)).Count > 0)
            return Loc.GetString("wg-mentor-page-pending");

        var record = await GatherAsync(user, session);
        var result = MentorEligibility.Check(record, _cfg.GetCVar(MentorCVars.MinHours), DateTime.UtcNow);
        if (!result.CanApply)
            return BlockText(result.Blocks[0], record);

        var clean = new Dictionary<string, string>();
        foreach (var id in MentorForm.Questions)
        {
            if (!answers.TryGetValue(id, out var text) || string.IsNullOrWhiteSpace(text))
                continue;

            text = text.Trim();
            clean[id] = text.Length > MentorForm.MaxAnswerLength ? text[..MentorForm.MaxAnswerLength] : text;
        }

        var mode = _cfg.GetCVar(MentorCVars.ApplicationMode).Trim().ToLowerInvariant();
        var auto = mode == "auto" || mode != "review" && result.Spotless;

        var application = new WolfgateMentorApplication
        {
            UserId = user.UserId,
            CreatedAt = DateTime.UtcNow,
            PlaytimeMinutes = (int) record.Playtime.TotalMinutes,
            Answers = JsonSerializer.Serialize(clean),
            RecordSummary = MentorEligibility.Summary(record, result),
            State = auto ? StateAutoApproved : StatePending,
            ReviewedAt = auto ? DateTime.UtcNow : null,
        };

        var applicationId = await _db.AddMentorApplicationAsync(application);
        _adminLog.Add(LogType.WGMentor, LogImpact.Low,
            $"{NameOf(user, session)} applied to be a mentor (#{applicationId}, {(auto ? "approved at once" : "queued")})");

        if (auto)
        {
            await _mentors.GrantAsync(user, MentorTier.Trainee, null, MentorRows.SourceAuto, notify: false);
            Notify(user, Loc.GetString("wg-mentor-app-auto"));
        }
        else
        {
            Notify(user, Loc.GetString("wg-mentor-app-queued"));
            ApplicationsChanged?.Invoke();
        }

        return null;
    }

    /// <summary>Whether a player may review applications: staff with Moderator, or a connected Senior Mentor.</summary>
    public bool CanReview(ICommonSession session)
    {
        return _admin.HasAdminFlag(session, AdminFlags.Moderator)
               || _mentors.GetTier(session.UserId) == MentorTier.Senior;
    }

    /// <summary>Approves or denies a pending application. Returns a problem to show the reviewer, or null.</summary>
    public async Task<string?> ReviewAsync(int id, bool approve, string reason, int reapplyDays, NetUserId? actor)
    {
        var application = (await _db.GetMentorApplicationsAsync(StatePending, null)).FirstOrDefault(a => a.Id == id);
        if (application == null)
            return Loc.GetString("wg-mentor-review-gone");

        reason = reason.Trim();
        if (!approve && reason.Length == 0)
            return Loc.GetString("wg-mentor-review-no-reason");

        var user = new NetUserId(application.UserId);
        var now = DateTime.UtcNow;
        application.ReviewedBy = actor?.UserId;
        application.ReviewedAt = now;

        if (approve)
        {
            application.State = StateApproved;
            await _db.UpdateMentorApplicationAsync(application);
            await _mentors.GrantAsync(user, MentorTier.Trainee, actor, MentorRows.SourceStaff, notify: false);
            Notify(user, Loc.GetString("wg-mentor-app-approved"));
        }
        else
        {
            var days = reapplyDays > 0 ? reapplyDays : _cfg.GetCVar(MentorCVars.ReapplyDays);
            application.State = StateDenied;
            application.Reason = reason;
            application.ReapplyAfter = now + TimeSpan.FromDays(days);
            await _db.UpdateMentorApplicationAsync(application);
            Notify(user, Loc.GetString("wg-mentor-app-denied",
                ("reason", reason), ("date", application.ReapplyAfter.Value.ToString("yyyy-MM-dd"))));
        }

        _adminLog.Add(LogType.WGMentor, LogImpact.Medium,
            $"{NameOf(actor, null)} {(approve ? "approved" : "denied")} the mentor application #{id} of {NameOf(user, null)}{(approve ? "" : $": {reason}")}");

        ApplicationsChanged?.Invoke();
        return null;
    }

    /// <summary>The player-facing reason an application can't go in.</summary>
    public string BlockText(MentorBlock block, MentorRecord record)
    {
        return block switch
        {
            MentorBlock.Hours => Loc.GetString("wg-mentor-page-no-hours", ("needed", _cfg.GetCVar(MentorCVars.MinHours))),
            MentorBlock.Ban => Loc.GetString("wg-mentor-page-no-ban"),
            MentorBlock.MentorBan => record.MentorBanUntil is { } until
                ? Loc.GetString("wg-mentor-page-no-mentor-ban", ("until", until.ToString("yyyy-MM-dd")))
                : Loc.GetString("wg-mentor-page-no-mentor-ban-permanent"),
            MentorBlock.Record => Loc.GetString("wg-mentor-page-no-record", ("days", _cfg.GetCVar(MentorCVars.CleanDays))),
            _ => Loc.GetString("wg-mentor-page-no-reapply",
                ("date", (record.ReapplyAfter ?? DateTime.UtcNow).ToString("yyyy-MM-dd"))),
        };
    }

    #endregion

    #region Page actions

    private async Task ActAsync(ICommonSession session, MentorPageAction action)
    {
        switch (action)
        {
            case MentorPageAction.Resign:
                await _mentors.ResignAsync(session.UserId);
                break;

            case MentorPageAction.Reactivate:
                var ban = await _mentors.BanStateAsync(session.UserId);
                if (ban.Banned)
                    _chat.DispatchServerMessage(session, Loc.GetString("wg-mentor-page-no-ban"));
                else
                    await _mentors.ReactivateAsync(session.UserId);
                break;

            case MentorPageAction.LinkDiscord:
                await LinkDiscordAsync(session);
                return;
        }

        await SendPageAsync(session);
    }

    /// <summary>Mints or finds the panel's one-time link and sends it to the client to open.</summary>
    private async Task LinkDiscordAsync(ICommonSession session)
    {
        var baseUrl = _cfg.GetCVar(CCVars.SymphonyUrl).Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
            return;

        var (linked, ticket) = await SymphonyLinkTickets.GetOrMintAsync(_cfg, session.UserId);
        if (session.Status == SessionStatus.Disconnected)
            return;

        if (!linked && ticket is { } token)
            RaiseNetworkEvent(new WGMentorDiscordLinkEvent { Url = $"{baseUrl}/auth/start?ticket={token}" }, session);

        await SendPageAsync(session);
    }

    #endregion

    #region Eligibility notice

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame)
            _ = Guard(() => CheckNoticeAsync(args.Session));
    }

    private void CheckNotices()
    {
        foreach (var session in _players.Sessions.Where(s => s.Status == SessionStatus.InGame).ToList())
        {
            _ = Guard(() => CheckNoticeAsync(session));
        }
    }

    /// <summary>Once a player reaches the hours, tells them once that they can apply (the client keeps the flag).</summary>
    private async Task CheckNoticeAsync(ICommonSession session)
    {
        if (!_cfg.GetCVar(MentorCVars.Enabled) || _netCfg.GetClientCVar(session.Channel, MentorCVars.EligibleNoticed))
            return;

        if (!_playTime.TryGetTrackerTime(session, PlayTimeTrackingShared.TrackerOverall, out var played)
            || played.Value < TimeSpan.FromHours(_cfg.GetCVar(MentorCVars.MinHours)))
        {
            return;
        }

        var row = await _db.GetMentorAsync(session.UserId.UserId);
        if (row != null || session.Status == SessionStatus.Disconnected)
            return;

        _chat.DispatchServerMessage(session,
            Loc.GetString("wg-mentor-page-eligible", ("hours", (int) played.Value.TotalHours)));
        RaiseNetworkEvent(new WGMentorEligibleNoticeEvent(), session);
    }


    #endregion

    private void Notify(NetUserId user, string message)
    {
        if (_players.TryGetSessionById(user, out var session) && session.Status != SessionStatus.Disconnected)
            _chat.DispatchServerMessage(session, message);
    }

    private string NameOf(NetUserId? user, ICommonSession? session)
    {
        if (session != null)
            return session.Name;

        if (user == null)
            return "the server";

        return _players.TryGetSessionById(user.Value, out var found) ? found.Name : user.Value.ToString();
    }

    private async Task Guard(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e)
        {
            Log.Error($"Mentor application work failed: {e}");
        }
    }
}
