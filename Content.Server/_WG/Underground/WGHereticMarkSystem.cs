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
        if (TryComp<ActorComponent>(mob, out var actor))
            _resolved.Add(actor.PlayerSession.UserId);
    }

    /// <summary>Whether a player's mark was already settled this round.</summary>
    public bool Resolved(NetUserId user)
    {
        return _resolved.Contains(user);
    }

    private void OnAttempt(Entity<AddComponentsAfterTimeComponent> ent, ref WGMarkAttemptEvent args)
    {
        if (!TryComp<ActorComponent>(ent, out var actor))
            return;

        if (HasComp<WGNewbieComponent>(ent) || _resolved.Contains(actor.PlayerSession.UserId))
            args.Cancelled = true;
    }

    private void OnMarked(Entity<HereticComponent> ent, ref ComponentStartup args)
    {
        // The mark replaces the body's contraband values, a member's warrant included.
        _warrants.Refresh(ent);
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
    }
}
