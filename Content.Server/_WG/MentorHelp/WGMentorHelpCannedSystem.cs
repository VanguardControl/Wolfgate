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

    private List<MentorCannedAnswer> _answers = new();

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

        await _db.SaveMentorCannedAsync(row);
        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"{editor.Name} saved canned answer #{row.Id} \"{title}\"");
        await LoadAsync();
        return true;
    }

    /// <summary>Deletes a canned answer. False when refused or there was none.</summary>
    public async Task<bool> DeleteAsync(ICommonSession editor, int id)
    {
        if (!CanEdit(editor) || !await _db.DeleteMentorCannedAsync(id))
            return false;

        _adminLog.Add(LogType.WGMentorHelp, LogImpact.Low, $"{editor.Name} deleted canned answer #{id}");
        await LoadAsync();
        return true;
    }

    private async Task LoadAsync()
    {
        try
        {
            var rows = await _db.GetMentorCannedAsync();
            _answers = rows.Select(r => new MentorCannedAnswer
            {
                Id = r.Id,
                Title = r.Title,
                Text = r.Text,
                GuideEntry = r.GuideEntry,
            }).ToList();
        }
        catch (Exception e)
        {
            Log.Error($"Could not load the canned answers: {e}");
            return;
        }

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
        _ = SaveAsync(args.SenderSession, ev.Answer);
    }

    private void OnDelete(WGMentorCannedDeleteRequest ev, EntitySessionEventArgs args)
    {
        _ = DeleteAsync(args.SenderSession, ev.Id);
    }
}
