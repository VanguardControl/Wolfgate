using Content.Server._Mono.PDV.Components;
using Content.Server.Chat.Managers;
using Content.Shared._WG.Newbie;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.Underground;

/// <summary>
/// The rules a PDV heretic mark follows: the marked player is told, a tagged new player is never marked, and a player
/// who was handed in or killed as a heretic isn't marked again that round.
/// </summary>
public sealed partial class WGHereticMarkSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WGUndergroundWarrantSystem _warrants = default!;

    /// <summary>Players whose mark was settled this round.</summary>
    private readonly HashSet<NetUserId> _resolved = new();

    /// <summary>Who each marked body belonged to when the mark landed.</summary>
    private readonly Dictionary<EntityUid, NetUserId> _marked = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AddComponentsAfterTimeComponent, WGMarkAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<HereticComponent, ComponentStartup>(OnMarked);
        SubscribeLocalEvent<HereticComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    /// <summary>Counts a player's mark as settled, so their later bodies this round aren't marked.</summary>
    public void Resolve(EntityUid mob)
    {
        // The player may have left the body since it was marked.
        if (_warrants.PlayerOf(mob) is { } user || _marked.TryGetValue(mob, out user))
            _resolved.Add(user);
    }

    /// <summary>Whether a player's mark was already settled this round.</summary>
    public bool Resolved(NetUserId user)
    {
        return _resolved.Contains(user);
    }

    private void OnAttempt(Entity<AddComponentsAfterTimeComponent> ent, ref WGMarkAttemptEvent args)
    {
        if (HasComp<WGNewbieComponent>(ent) || _warrants.PlayerOf(ent) is { } user && _resolved.Contains(user))
            args.Cancelled = true;
    }

    private void OnMarked(Entity<HereticComponent> ent, ref ComponentStartup args)
    {
        // The mark replaces the body's contraband values, a member's warrant included.
        _warrants.Refresh(ent);
        if (_warrants.PlayerOf(ent) is { } user)
            _marked[ent] = user;

        if (!TryComp<ActorComponent>(ent, out var actor))
            return;

        _chat.DispatchServerMessage(actor.PlayerSession, Loc.GetString("wg-heretic-marked"));
        _popup.PopupEntity(Loc.GetString("wg-heretic-marked-popup"), ent, ent, PopupType.LargeCaution);
    }

    private void OnMobState(EntityUid uid, HereticComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Resolve(uid);
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _resolved.Clear();
        _marked.Clear();
    }
}
