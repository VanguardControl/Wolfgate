using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.FixedPoint;
using Robust.Server.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// The noise and the mess a wound makes (V1/V4). A wound that lands, or jumps a stage, plays a sound keyed
/// by what it is and what it is in; a hit that wounds throws blood mist or sparks off the body. Both are
/// read from <see cref="WolfmedSfxProfilePrototype"/>, both are throttled per body, and both are silent
/// unless the wound moved because something hit the body.
/// </summary>
/// <remarks>
/// Server-side because wound creation is. Sounds play at the BODY: a wound lives in the part's container
/// and a part lives in nullspace, so neither has a position to play from. The damage gate is what keeps
/// an infection, a necrosis clock or a healing tick from making a sound; it is stamped by
/// <see cref="WolfmedPartDamageEvent"/>, which is raised inside the same tick as the wound it causes.
/// </remarks>
public sealed class WolfmedWoundSfxSystem : EntitySystem
{
    /// <summary>The shipped profile. Retuning the feedback layer is an edit to that prototype.</summary>
    public const string DefaultProfile = "WolfmedSfxDefault";

    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedPartDamageEvent>(OnPartDamage);
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    /// <summary>Server time, so a system sharing the throttle stamps it the same way this one does.</summary>
    public TimeSpan Now => _timing.CurTime;

    /// <summary>The shipped profile, or null when a server has deleted it.</summary>
    public WolfmedSfxProfilePrototype? Profile =>
        _prototypes.TryIndex<WolfmedSfxProfilePrototype>(DefaultProfile, out var profile) ? profile : null;

    private void OnPartDamage(ref WolfmedPartDamageEvent args)
    {
        if (TerminatingOrDeleted(args.Body))
            return;

        EnsureComp<WolfmedWoundSfxComponent>(args.Body).LastHit = _timing.CurTime;
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (args.Kind == WolfmedWoundLifecycle.Removed ||
            args.Severity <= args.OldSeverity ||
            CompOrNull<BodyPartComponent>(args.Part)?.Body is not { } body ||
            !TryComp(body, out WolfmedWoundSfxComponent? state) ||
            state.LastHit != _timing.CurTime)
            return;

        if (Profile is not { } profile)
            return;

        var organic = _traits.IsOrganic(args.Part);
        _prototypes.TryIndex(args.Prototype, out var prototype);

        if (args.Severity >= profile.MinSeverity &&
            (args.Kind == WolfmedWoundLifecycle.Created || JumpedStage(prototype, args.OldSeverity, args.Severity)))
            TryPlayWound(body, state, profile, args.Prototype, prototype, organic, args.Severity);

        TrySpawnDebris(body, state, profile, organic, args.Severity - args.OldSeverity);
    }

    /// <summary>
    /// Plays the sound this wound calls for. Public so the dismemberment system and a test can share the
    /// one throttle: two feedback sources on one body in one tick is one noise, not two.
    /// </summary>
    public bool TryPlayWound(
        EntityUid body,
        WolfmedWoundSfxComponent state,
        WolfmedSfxProfilePrototype profile,
        ProtoId<WoundPrototype> wound,
        WoundPrototype? prototype,
        bool organic,
        FixedPoint2 severity)
    {
        if (!_config.GetCVar(WolfmedCVars.WoundSfx) ||
            _timing.CurTime < state.NextSound ||
            profile.GetWoundSound(wound, prototype, organic, severity) is not { } sound)
            return false;

        state.NextSound = _timing.CurTime + profile.SoundInterval;
        _audio.PlayPvs(sound, body);
        return true;
    }

    /// <summary>Spawns the debris tier this severity earns, if the body is off its throttle.</summary>
    public EntityUid? TrySpawnDebris(
        EntityUid body,
        WolfmedWoundSfxComponent state,
        WolfmedSfxProfilePrototype profile,
        bool organic,
        FixedPoint2 severity)
    {
        if (!_config.GetCVar(WolfmedCVars.HitDebris) ||
            _timing.CurTime < state.NextDebris ||
            profile.GetDebris(organic, severity) is not { } effect)
            return null;

        state.NextDebris = _timing.CurTime + profile.DebrisInterval;
        return Spawn(effect, _transform.GetMapCoordinates(body));
    }

    /// <summary>Whether the wound crossed into a new severity stage. A wound with no stages never does.</summary>
    public static bool JumpedStage(WoundPrototype? prototype, FixedPoint2 old, FixedPoint2 severity) =>
        prototype != null && prototype.GetStage(old) != prototype.GetStage(severity);
}
