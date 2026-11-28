using Content.Shared._WG.Mentors;
using Content.Shared.Ghost;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.Mentors;

/// <summary>
/// Marks the body an on-duty mentor is controlling, not a ghost, with their tier and crown setting; takes the mark off
/// when they go off duty, leave the body or stop being a mentor.
/// </summary>
public sealed class WGMentorMarkTagSystem : EntitySystem
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    [Dependency] private WGMentorSystem _mentors = default!;
    [Dependency] private INetConfigurationManager _netCfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;

    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnDetached);
        _mentors.StatusChanged += Refresh;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _mentors.StatusChanged -= Refresh;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRefresh)
            return;

        // Also picks up a change to the crown setting, which arrives without an event.
        _nextRefresh = _timing.CurTime + RefreshInterval;
        foreach (var session in _players.Sessions)
        {
            Refresh(session);
        }
    }

    /// <summary>Brings a player's body in line with their mentor status.</summary>
    public void Refresh(ICommonSession session)
    {
        if (session.AttachedEntity is { } body)
            SetMark(body, session);
    }

    private void OnAttached(PlayerAttachedEvent args)
    {
        SetMark(args.Entity, args.Player);
    }

    private void OnDetached(PlayerDetachedEvent args)
    {
        RemComp<WGMentorMarkComponent>(args.Entity);
    }

    private void SetMark(EntityUid body, ICommonSession session)
    {
        if (!_mentors.IsOnDuty(session.UserId) || HasComp<GhostComponent>(body) || _mentors.GetTier(session.UserId) is not { } tier)
        {
            RemComp<WGMentorMarkComponent>(body);
            return;
        }

        var show = _netCfg.GetClientCVar(session.Channel, MentorCVars.ShowCrown);
        var mark = EnsureComp<WGMentorMarkComponent>(body);
        if (mark.Tier == tier && mark.ShowCrown == show)
            return;

        mark.Tier = tier;
        mark.ShowCrown = show;
        Dirty(body, mark);
    }
}
