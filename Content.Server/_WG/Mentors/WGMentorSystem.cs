using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Shared._WG.Mentors;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.Mentors;

/// <summary>
/// The mentor roster: loads a connecting player's row, keeps active mentors and their duty in memory, tells each
/// client its own status and everyone the on-duty count, and makes the roster changes staff and the panel ask for.
/// </summary>
public sealed class WGMentorSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private INetConfigurationManager _netCfg = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>Raised when a connected player's mentor status or duty changes.</summary>
    public event Action<ICommonSession>? StatusChanged;

    /// <summary>Active mentors who are connected, by user.</summary>
    private readonly Dictionary<NetUserId, WolfgateMentor> _active = new();
    private readonly HashSet<NetUserId> _onDuty = new();
    private readonly Dictionary<NetUserId, SemaphoreSlim> _gates = new();
    private readonly Dictionary<NetUserId, int> _generations = new();
    private int _sentCount = -1;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGMentorSetDutyEvent>(OnSetDuty);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        _admin.OnPermsChanged += OnPermsChanged;
        Subs.CVar(_cfg, MentorCVars.Enabled, _ => RefreshAll());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        _admin.OnPermsChanged -= OnPermsChanged;
    }

    private bool Enabled => _cfg.GetCVar(MentorCVars.Enabled);

    /// <summary>Whether a connected player is an active mentor.</summary>
    public bool IsMentor(NetUserId user)
    {
        return Enabled && _active.ContainsKey(user);
    }

    /// <summary>Whether a connected player is an active mentor on duty.</summary>
    public bool IsOnDuty(NetUserId user)
    {
        return IsMentor(user) && _onDuty.Contains(user);
    }

    /// <summary>A connected active mentor's tier, or null.</summary>
    public MentorTier? GetTier(NetUserId user)
    {
        return IsMentor(user) ? MentorRows.TierFromDb(_active[user].Tier) : null;
    }

    /// <summary>The connected mentors on duty.</summary>
    public IEnumerable<ICommonSession> OnDutySessions()
    {
        if (!Enabled)
            yield break;

        foreach (var user in _onDuty)
        {
            if (_players.TryGetSessionById(user, out var session))
                yield return session;
        }
    }

    public int OnDutyCount => Enabled ? _onDuty.Count : 0;

    /// <summary>The connected active mentors, on duty or not.</summary>
    public IEnumerable<ICommonSession> ActiveSessions()
    {
        if (!Enabled)
            yield break;

        foreach (var user in _active.Keys)
        {
            if (_players.TryGetSessionById(user, out var session))
                yield return session;
        }
    }

    /// <summary>Puts a mentor on or off duty. False when they aren't an active mentor.</summary>
    public bool SetDuty(ICommonSession session, bool onDuty)
    {
        if (!IsMentor(session.UserId))
            return false;

        var changed = onDuty ? _onDuty.Add(session.UserId) : _onDuty.Remove(session.UserId);
        if (!changed)
            return true;

        _adminLog.Add(LogType.WGMentor, LogImpact.Low,
            $"{session.Name} went {(onDuty ? "on" : "off")} duty as a mentor");
        Changed(session);
        return true;
    }

    /// <summary>Every row in the table, on the roster or not.</summary>
    public Task<List<WolfgateMentor>> ListAsync()
    {
        return _db.GetMentorsAsync();
    }

    /// <summary>
    /// Puts a player on the roster at a tier, or changes the tier of one already on it. A row left by revocation or
    /// resignation is reused, keeping its history.
    /// </summary>
    private async Task GrantCore(NetUserId user, MentorTier tier, NetUserId? actor, string source)
    {
        var now = DateTime.UtcNow;
        var row = await _db.GetMentorAsync(user.UserId);
        var wasActive = row?.State == MentorRows.StateActive;
        row ??= new WolfgateMentor { UserId = user.UserId };

        if (!wasActive)
        {
            row.GrantedAt = now;
            row.GrantedBy = actor?.UserId;
            row.Source = source;
            row.LastActiveAt = now;
            row.PromotedAt = null;
        }

        row.State = MentorRows.StateActive;
        row.Tier = MentorRows.TierToDb(tier);
        MentorRows.AddHistory(row, now, actor?.UserId, wasActive ? $"tier {row.Tier}" : $"granted {row.Tier}");
        await _db.SaveMentorAsync(row);

        _adminLog.Add(LogType.WGMentor, LogImpact.Medium,
            $"{Name(actor)} {(wasActive ? "changed the mentor tier of" : "granted mentor to")} {Name(user)}: {row.Tier}");

        Notify(user, wasActive
            ? Loc.GetString("wg-mentor-tier-changed", ("tier", MentorRows.TierName(tier)))
            : Loc.GetString("wg-mentor-granted", ("tier", MentorRows.TierName(tier))));
        Reload(user, row);
    }

    private async Task<bool> SetTierCore(NetUserId user, MentorTier tier, NetUserId? actor)
    {
        var row = await _db.GetMentorAsync(user.UserId);
        if (row == null || !MentorRows.OnRoster(row))
            return false;

        var now = DateTime.UtcNow;
        var promoted = MentorRows.TierFromDb(row.Tier) == MentorTier.Trainee && tier != MentorTier.Trainee;
        row.Tier = MentorRows.TierToDb(tier);
        if (promoted)
            row.PromotedAt = now;

        MentorRows.AddHistory(row, now, actor?.UserId, $"tier {row.Tier}");
        await _db.SaveMentorAsync(row);

        _adminLog.Add(LogType.WGMentor, LogImpact.Medium,
            $"{Name(actor)} changed the mentor tier of {Name(user)}: {row.Tier}");

        Notify(user, Loc.GetString("wg-mentor-tier-changed", ("tier", MentorRows.TierName(tier))));
        Reload(user, row);
        return true;
    }

    private async Task<bool> RevokeCore(NetUserId user, NetUserId? actor, string reason)
    {
        var row = await _db.GetMentorAsync(user.UserId);
        if (row == null || !MentorRows.OnRoster(row))
            return false;

        row.State = MentorRows.StateRevoked;
        MentorRows.AddHistory(row, DateTime.UtcNow, actor?.UserId, "revoked", reason);
        await _db.SaveMentorAsync(row);

        _adminLog.Add(LogType.WGMentor, LogImpact.Medium, $"{Name(actor)} revoked mentor from {Name(user)}: {reason}");

        Notify(user, Loc.GetString("wg-mentor-revoked", ("reason", reason)));
        Reload(user, row);
        return true;
    }

    private async Task<MentorTier?> RestoreCore(NetUserId user, NetUserId? actor)
    {
        var row = await _db.GetMentorAsync(user.UserId);
        if (row == null || row.State is not (MentorRows.StateSuspended or MentorRows.StateInactive))
            return null;

        row.State = MentorRows.StateActive;
        row.LastActiveAt = DateTime.UtcNow;
        MentorRows.AddHistory(row, row.LastActiveAt, actor?.UserId, "restored");
        await _db.SaveMentorAsync(row);

        _adminLog.Add(LogType.WGMentor, LogImpact.Medium, $"{Name(actor)} restored mentor to {Name(user)}");

        Notify(user, Loc.GetString("wg-mentor-restored"));
        Reload(user, row);
        return MentorRows.TierFromDb(row.Tier);
    }

    /// <summary>
    /// Puts a player on the roster at a tier, or changes the tier of one already on it. A row left by revocation or
    /// resignation is reused, keeping its history.
    /// </summary>
    public Task GrantAsync(NetUserId user, MentorTier tier, NetUserId? actor, string source)
    {
        return Serialized(user, async () =>
        {
            await GrantCore(user, tier, actor, source);
            return true;
        });
    }

    /// <summary>Changes the tier of a player on the roster. False when they aren't on it.</summary>
    public Task<bool> SetTierAsync(NetUserId user, MentorTier tier, NetUserId? actor)
    {
        return Serialized(user, () => SetTierCore(user, tier, actor));
    }

    /// <summary>Takes a player off the roster with a reason, keeping the row's history. False when they weren't on it.</summary>
    public Task<bool> RevokeAsync(NetUserId user, NetUserId? actor, string reason)
    {
        return Serialized(user, () => RevokeCore(user, actor, reason));
    }

    /// <summary>Makes a suspended or inactive mentor active again. Their stored tier, or null when they were neither.</summary>
    public Task<MentorTier?> RestoreAsync(NetUserId user, NetUserId? actor)
    {
        return Serialized(user, () => RestoreCore(user, actor));
    }

    /// <summary>
    /// Runs one roster change for a player at a time, read to save, and marks the player's row as changed so a
    /// connection load that read it earlier doesn't apply its stale copy.
    /// </summary>
    private async Task<T> Serialized<T>(NetUserId user, Func<Task<T>> change)
    {
        if (!_gates.TryGetValue(user, out var gate))
            _gates[user] = gate = new SemaphoreSlim(1, 1);

        await gate.WaitAsync();
        try
        {
            return await change();
        }
        finally
        {
            _generations[user] = Generation(user) + 1;
            gate.Release();
        }
    }

    private int Generation(NetUserId user)
    {
        return _generations.GetValueOrDefault(user);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        switch (args.NewStatus)
        {
            case SessionStatus.InGame:
                RaiseNetworkEvent(new WGMentorCountEvent { OnDuty = OnDutyCount }, args.Session);
                SendRosterToStaff(args.Session);
                _ = LoadAsync(args.Session);
                break;
            case SessionStatus.Disconnected:
                var wasOnDuty = _onDuty.Remove(args.Session.UserId);
                if (_active.Remove(args.Session.UserId))
                    SendRosterToStaff();

                if (wasOnDuty)
                    BroadcastCount();
                break;
        }
    }

    private async Task LoadAsync(ICommonSession session)
    {
        var generation = Generation(session.UserId);
        WolfgateMentor? row;
        try
        {
            row = await _db.GetMentorAsync(session.UserId.UserId);
            if (row != null && MentorRows.OnRoster(row))
                await _db.SetMentorLastActiveAsync(session.UserId.UserId, DateTime.UtcNow);
        }
        catch (Exception e)
        {
            Log.Error($"Could not load the mentor row of {session.Name}: {e}");
            return;
        }

        // A roster change since the read has already put the newer row in memory.
        if (session.Status == SessionStatus.Disconnected || Generation(session.UserId) != generation)
            return;

        Reload(session.UserId, row, fresh: true);
    }

    /// <summary>Takes a changed row into memory for a connected player and tells them and everyone else.</summary>
    private void Reload(NetUserId user, WolfgateMentor? row, bool fresh = false)
    {
        if (!_players.TryGetSessionById(user, out var session) || session.Status == SessionStatus.Disconnected)
            return;

        var wasMentor = _active.ContainsKey(user);
        if (row?.State == MentorRows.StateActive)
        {
            _active[user] = row;
            if (fresh || !wasMentor)
            {
                if (_netCfg.GetClientCVar(session.Channel, MentorCVars.StartOffDuty))
                    _onDuty.Remove(user);
                else
                    _onDuty.Add(user);
            }
        }
        else
        {
            _active.Remove(user);
            _onDuty.Remove(user);
        }

        Changed(session);
    }

    private void RefreshAll()
    {
        foreach (var session in _players.Sessions)
        {
            SendStatus(session);
        }

        BroadcastCount();
        SendRosterToStaff();
    }

    private void Changed(ICommonSession session)
    {
        SendStatus(session);
        BroadcastCount();
        SendRosterToStaff();
        StatusChanged?.Invoke(session);
    }

    /// <summary>Sends staff the connected active mentors, for the badge in the ahelp player list.</summary>
    private void SendRosterToStaff(ICommonSession? only = null)
    {
        var ev = new WGMentorRosterEvent { Mentors = ActiveSessions().Select(s => s.UserId).ToList() };
        foreach (var admin in _admin.ActiveAdmins)
        {
            if (only != null && admin != only)
                continue;

            if (admin.Status == SessionStatus.InGame
                && (_admin.HasAdminFlag(admin, AdminFlags.Adminhelp) || _admin.HasAdminFlag(admin, AdminFlags.Event)))
            {
                RaiseNetworkEvent(ev, admin);
            }
        }
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        SendRosterToStaff(args.Player);
    }

    private void SendStatus(ICommonSession session)
    {
        if (session.Status != SessionStatus.InGame)
            return;

        RaiseNetworkEvent(new WGMentorStatusEvent
        {
            IsMentor = IsMentor(session.UserId),
            Tier = GetTier(session.UserId) ?? MentorTier.Trainee,
            OnDuty = IsOnDuty(session.UserId),
        }, session);
    }

    private void BroadcastCount()
    {
        var count = OnDutyCount;
        if (count == _sentCount)
            return;

        _sentCount = count;
        var ev = new WGMentorCountEvent { OnDuty = count };
        foreach (var session in _players.Sessions.Where(s => s.Status == SessionStatus.InGame))
        {
            RaiseNetworkEvent(ev, session);
        }
    }

    private void OnSetDuty(WGMentorSetDutyEvent ev, EntitySessionEventArgs args)
    {
        SetDuty(args.SenderSession, ev.OnDuty);
    }

    private void Notify(NetUserId user, string message)
    {
        if (_players.TryGetSessionById(user, out var session) && session.Status != SessionStatus.Disconnected)
            _chat.DispatchServerMessage(session, message);
    }

    private string Name(NetUserId? user)
    {
        if (user == null)
            return "the server";

        return _players.TryGetSessionById(user.Value, out var session) ? session.Name : user.Value.ToString();
    }
}
