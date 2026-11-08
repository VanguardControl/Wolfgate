using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server._WF.CustomMarkings;
using Content.Server._WF.FeatureBans;
using Content.Shared._WF.AppearanceStudio;
using Content.Shared._WF.CustomMarkings;
using Content.Shared._WF.FeatureBans;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.AppearanceStudio;

/// <summary>
/// Stores the warps players paint, keeps banned players' characters plain, and gives admins a reset. Bodies get
/// their tweaks from the profile (SharedHumanoidAppearanceSystem).
/// </summary>
public sealed class StudioSystem : EntitySystem
{
    public static readonly ProtoId<WFFeatureBanPrototype> Feature = StudioRules.Feature;

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private FeatureBanSystem _featureBans = default!;

    private static readonly TimeSpan SaveCooldown = TimeSpan.FromSeconds(1);

    /// <summary>When each account may save a warp again; by account, so the web creator shares the cooldown.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _nextSave = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<StudioWarpSaveEvent>(OnWarpSave);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
        SubscribeLocalEvent<FeatureBannedEvent>(OnFeatureBanned);
        SubscribeLocalEvent<CustomMarkingArtBlockedEvent>(OnArtBlocked);
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);

        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public bool IsBanned(ICommonSession session)
    {
        return _featureBans.IsBanned(session.UserId, Feature);
    }

    private async void OnWarpSave(StudioWarpSaveEvent msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        string? hash = null;
        string? error;
        try
        {
            (hash, error) = await SaveWarp(session.UserId, session.Name, msg.Pixels, session);
        }
        catch (Exception e)
        {
            Log.Error($"Saving a studio warp for {session.Name} threw: {e}");
            error = "wf-studio-error-failed";
        }

        if (session.Status != SessionStatus.Disconnected)
            RaiseNetworkEvent(new StudioWarpSaveResultEvent(msg.Request, hash, error), session);
    }

    /// <summary>How long an account must wait before it may save a warp again; zero when it may now.</summary>
    public TimeSpan CooldownLeft(NetUserId user)
    {
        var left = _nextSave.GetValueOrDefault(user) - _timing.RealTime;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// Checks and stores a warp for an account, from the game's editor or the web creator. Returns its hash, or a
    /// locale id saying why not. <paramref name="session"/> only names the player in the admin log.
    /// </summary>
    public async Task<(string? Hash, string? Error)> SaveWarp(NetUserId user, string actor, byte[]? pixels, ICommonSession? session = null)
    {
        if (!_cfg.GetCVar(StudioCVars.Enabled) || !_cfg.GetCVar(StudioCVars.Warps))
            return (null, "wf-studio-error-disabled");

        if (_featureBans.IsBanned(user, Feature))
            return (null, "wf-feature-banned");

        var now = _timing.RealTime;
        if (_nextSave.TryGetValue(user, out var next) && now < next)
            return (null, "wf-studio-error-cooldown");

        if (CustomMarkingArt.FromPixels(pixels, maxFrames: 1) is not { } art || StudioWarp.FromArt(art) is not { } warp)
            return (null, "wf-studio-error-invalid");

        if (warp.IsNeutral())
            return (null, "wf-studio-error-neutral");

        _nextSave[user] = now + SaveCooldown;

        var hash = CustomMarkingSystem.Hash(art);
        var error = await _db.StoreStudioWarpAsync(user, hash, new CustomMarkingStoredArt(art.ToPng(), null, null),
            _cfg.GetCVar(CustomMarkingCVars.DailyArtLimit));

        if (error != null)
            return (null, error);

        if (session != null)
            _adminLog.Add(LogType.Identity, LogImpact.Low, $"{session:player} saved studio warp {hash}");
        else
            _adminLog.Add(LogType.Identity, LogImpact.Low, $"{actor} ({user}) saved studio warp {hash} from the web creator");
        return (hash, null);
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (IsBanned(ev.Player))
            Strip(ev.Mob);
    }

    private void OnFeatureBanned(ref FeatureBannedEvent ev)
    {
        if (ev.Feature == Feature && ev.Session.AttachedEntity is { } mob)
            Strip(mob);
    }

    /// <summary>Takes the studio tweaks and custom markings off a body.</summary>
    public void Strip(EntityUid uid)
    {
        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid)
            || humanoid.StudioTweaks.Count == 0 && humanoid.CustomMarkings.Count == 0)
            return;

        humanoid.StudioTweaks.Clear();
        humanoid.CustomMarkings.Clear();
        Dirty(uid, humanoid);
    }

    private void OnArtBlocked(ref CustomMarkingArtBlockedEvent ev)
    {
        var hash = ev.Hash;
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent>();
        while (query.MoveNext(out var uid, out var humanoid))
        {
            if (!humanoid.StudioTweaks.Any(t => t.Warp == hash))
                continue;

            var tweaks = new List<StudioTweak>();
            foreach (var tweak in humanoid.StudioTweaks)
            {
                var copy = tweak.Clone();
                if (copy.Warp == hash)
                    copy.Warp = null;

                if (!copy.IsEmpty())
                    tweaks.Add(copy);
            }

            humanoid.StudioTweaks = tweaks;
            Dirty(uid, humanoid);
        }
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<HumanoidAppearanceComponent>(args.Target, out var humanoid)
            || humanoid.StudioTweaks.Count == 0
            || !TryComp<ActorComponent>(args.User, out var actor)
            || !_admin.HasAdminFlag(actor.PlayerSession, AdminFlags.Admin))
            return;

        var user = args.User;
        var target = args.Target;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wf-studio-admin-reset"),
            Category = VerbCategory.Admin,
            Impact = LogImpact.Medium,
            ConfirmationPopup = true,
            Act = () =>
            {
                if (!TryComp<HumanoidAppearanceComponent>(target, out var body))
                    return;

                _adminLog.Add(LogType.Identity, LogImpact.Medium,
                    $"{ToPrettyString(user):actor} reset the studio tweaks of {ToPrettyString(target):target}: {StudioRules.ToStored(body.StudioTweaks)}");
                body.StudioTweaks = new List<StudioTweak>();
                Dirty(target, body);
            },
        });
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _nextSave.Remove(args.Session.UserId);
    }
}
