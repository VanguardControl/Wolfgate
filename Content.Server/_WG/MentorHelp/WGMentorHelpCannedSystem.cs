using System.Linq;
using System.Threading.Tasks;
using Content.Server._WG.Mentors;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared._WG.MentorHelp;
using Content.Shared._WG.Mentors;
using Content.Shared.Administration;
using Content.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Timer = Robust.Shared.Timing.Timer;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WG.MentorHelp;

/// <summary>
/// The canned answers mentors insert into replies: loaded once from the database, sent to responders, and edited by
/// Senior Mentors and staff with Adminhelp.
/// </summary>
public sealed class WGMentorHelpCannedSystem : EntitySystem
{
    private const int MaxTitle = 64;
    private const int MaxText = 1000;
    private const int MaxGuide = 128;

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private WGMentorHelpSystem _help = default!;
    [Dependency] private WGMentorSystem _mentors = default!;
    [Dependency] private IPlayerManager _players = default!;

    private static readonly TimeSpan ReloadRetryDelay = TimeSpan.FromSeconds(30);

    private List<MentorCannedAnswer> _answers = new();
    private int _loadVersion;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMentorHelpRefreshRequest>(OnRefresh);
        SubscribeNetworkEvent<WGMentorCannedSaveRequest>(OnSave);
        SubscribeNetworkEvent<WGMentorCannedDeleteRequest>(OnDelete);
        _ = LoadAsync();
    }

    /// <summary>The canned answers, by title.</summary>
    public IReadOnlyList<MentorCannedAnswer> Answers => _answers;

    /// <summary>Senior Mentors and staff with Adminhelp may edit canned answers.</summary>
    public bool CanEdit(ICommonSession session)
    {
        return _admin.HasAdminFlag(session, AdminFlags.Adminhelp) || _mentors.GetTier(session.UserId) == MentorTier.Senior;
    }

    /// <summary>Adds (id 0) or changes a canned answer. False when refused.</summary>
    public async Task<bool> SaveAsync(ICommonSession editor, MentorCannedAnswer answer)
    {
        if (!CanEdit(editor))
            return false;

        var title = FormattedMessage.RemoveMarkupPermissive(answer.Title).Trim();
        var text = FormattedMessage.RemoveMarkupPermissive(answer.Text).Trim();
        var guide = string.IsNullOrWhiteSpace(answer.GuideEntry) ? null : answer.GuideEntry.Trim();
        if (title.Length is 0 or > MaxTitle || text.Length is 0 or > MaxText || guide?.Length > MaxGuide)
            return false;

        var row = new WolfgateMentorCanned
        {
            Id = answer.Id,
            Title = title,
            Text = text,
            GuideEntry = guide,
            UpdatedBy = editor.UserId.UserId,
            UpdatedAt = DateTime.UtcNow,
        };

        try
        {
            await _db.SaveMentorCannedAsync(row);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another editor deleted it while this one had it open.
            RaiseNetworkEvent(new WGMentorHelpNoticeEvent { Text = Loc.GetString("wg-mhelp-canned-gone") }, editor);
            await LoadAsync();
            return false;
        }

        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"{editor.Name} saved canned answer #{row.Id} \"{title}\"");
        await LoadAsync();
        return true;
    }

    /// <summary>Deletes a canned answer. False when refused or there was none.</summary>
    public async Task<bool> DeleteAsync(ICommonSession editor, int id)
    {
        if (!CanEdit(editor))
            return false;

        var deleted = await _db.DeleteMentorCannedAsync(id);
        if (deleted)
            _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"{editor.Name} deleted canned answer #{id}");

        // Reload either way: a delete that found nothing may mean the cached list is stale.
        await LoadAsync();
        return deleted;
    }

    /// <summary>
    /// Reads the answers and sends them to responders. Only the newest load applies its result; a failed read is tried
    /// again later, so a committed edit always reaches the cache.
    /// </summary>
    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        List<WolfgateMentorCanned> rows;
        try
        {
            rows = await _db.GetMentorCannedAsync();
        }
        catch (Exception e)
        {
            Log.Error($"Could not load the canned answers: {e}");
            Timer.Spawn(ReloadRetryDelay, () =>
            {
                if (version == _loadVersion)
                    _ = LoadAsync();
            });
            return;
        }

        if (version != _loadVersion)
            return;

        _answers = rows.Select(r => new MentorCannedAnswer
        {
            Id = r.Id,
            Title = r.Title,
            Text = r.Text,
            GuideEntry = r.GuideEntry,
        }).ToList();

        foreach (var session in _players.Sessions)
        {
            if (session.Status == SessionStatus.InGame && _help.IsResponder(session))
                Send(session);
        }
    }

    private void Send(ICommonSession session)
    {
        RaiseNetworkEvent(new WGMentorCannedListEvent { Answers = _answers, CanEdit = CanEdit(session) }, session);
    }

    private void OnRefresh(WGMentorHelpRefreshRequest ev, EntitySessionEventArgs args)
    {
        if (_help.IsResponder(args.SenderSession))
            Send(args.SenderSession);
    }

    private void OnSave(WGMentorCannedSaveRequest ev, EntitySessionEventArgs args)
    {
        _ = Guard(SaveAsync(args.SenderSession, ev.Answer));
    }

    private void OnDelete(WGMentorCannedDeleteRequest ev, EntitySessionEventArgs args)
    {
        _ = Guard(DeleteAsync(args.SenderSession, ev.Id));
    }

    /// <summary>Logs a failed edit instead of leaving it unobserved.</summary>
    private async Task Guard(Task<bool> edit)
    {
        try
        {
            await edit;
        }
        catch (Exception e)
        {
            Log.Error($"Could not edit a canned answer: {e}");
        }
    }
}
