using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.Stunnable;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Concussions: what a hard knock to the head does to the patient rather than to the part. While any
/// concussion wound is open the body carries <see cref="WolfmedConcussionComponent"/>, which blurs sight
/// and slurs speech; the blow itself puts them on the floor for a moment. Nothing treats it, it fades with
/// time, and it fades several times faster while the patient sleeps or lies in a medical bed.
/// </summary>
/// <remarks>
/// Shared rather than server-only because the blur is contributed through <see cref="GetBlurEvent"/>, which
/// the client re-raises whenever eyewear changes; the state changes themselves are server-gated.
/// </remarks>
public sealed class WolfmedConcussionSystem : EntitySystem
{
    /// <summary>Recovery is applied on a slow tick; nothing here needs frame resolution.</summary>
    private const float TickSeconds = 1f;

    [Dependency] private BlurryVisionSystem _blurry = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedStutteringSystem _stutter = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedConcussionComponent, GetBlurEvent>(OnGetBlur);
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnGetBlur(Entity<WolfmedConcussionComponent> body, ref GetBlurEvent args)
    {
        args.Blur += body.Comp.Blur;
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (!_net.IsServer || !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedConcussionBehavior behavior) ||
            CompOrNull<BodyPartComponent>(args.Part)?.Body is not { } body)
            return;

        var landed = args.Kind == WolfmedWoundLifecycle.Created ||
                     args.Kind == WolfmedWoundLifecycle.Changed && args.Severity > args.OldSeverity;
        if (landed)
            _stun.TryKnockdown(body, behavior.Knockdown, refresh: true);

        Refresh(body);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        // Buffered: recovery raises wound events that can remove the component being enumerated.
        var due = new List<Entity<WolfmedConcussionComponent>>();
        var query = EntityQueryEnumerator<WolfmedConcussionComponent>();
        while (query.MoveNext(out var uid, out var concussion))
        {
            concussion.Accumulator += frameTime;
            if (concussion.Accumulator < TickSeconds)
                continue;

            due.Add((uid, concussion));
        }

        foreach (var body in due)
        {
            var elapsed = body.Comp.Accumulator;
            body.Comp.Accumulator = 0f;
            if (!Deleted(body) && body.Comp.Stutter)
                _stutter.DoStutter(body, TimeSpan.FromSeconds(elapsed * 2f + 2f), refresh: true);

            Recover(body.Owner, elapsed);
        }
    }

    /// <summary>
    /// Fades every concussion on the body by the time given. Rest (asleep, or buckled to a medical bed) is
    /// the only thing that speeds it up.
    /// </summary>
    public void Recover(EntityUid body, float seconds)
    {
        if (seconds <= 0f || Deleted(body))
            return;

        var resting = IsResting(body);
        foreach (var (part, _) in _body.GetBodyChildren(body).ToArray())
        {
            foreach (var wound in _wounds.GetWounds(part).ToArray())
            {
                if (!TryGetBehavior(wound, out var behavior))
                    continue;

                var rate = resting ? behavior.RestMultiplier : 1f;
                _wounds.ChangeSeverity(wound.Owner, behavior.RecoveryPerMinute * -(seconds * rate / 60f));
            }
        }
    }

    /// <summary>Whether the patient is resting: asleep, or lying in something that heals them.</summary>
    public bool IsResting(EntityUid body)
    {
        if (HasComp<SleepingComponent>(body))
            return true;

        return TryComp(body, out BuckleComponent? buckle) && buckle.BuckledTo is { } strap &&
               HasComp<WolfmedBedHealMarkerComponent>(strap);
    }

    /// <summary>Recomputes the body's concussion state from the wounds it is actually carrying.</summary>
    public void Refresh(EntityUid body)
    {
        if (Deleted(body))
            return;

        var blur = 0f;
        var stutter = false;
        foreach (var (part, _) in _body.GetBodyChildren(body).ToArray())
        {
            foreach (var wound in _wounds.GetWounds(part))
            {
                if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                    !TryGetBehavior(wound, out var behavior))
                    continue;

                blur = Math.Max(blur, behavior.Blur);
                stutter |= behavior.Stutter;
            }
        }

        if (blur <= 0f && !stutter)
        {
            if (HasComp<WolfmedConcussionComponent>(body))
            {
                RemComp<WolfmedConcussionComponent>(body);
                _blurry.UpdateBlurMagnitude(body);
            }

            return;
        }

        var concussion = EnsureComp<WolfmedConcussionComponent>(body);
        var changed = concussion.Blur != blur || concussion.Stutter != stutter;
        concussion.Blur = blur;
        concussion.Stutter = stutter;
        if (!changed)
            return;

        Dirty(body, concussion);
        _blurry.UpdateBlurMagnitude(body);
        if (stutter)
            _stutter.DoStutter(body, TimeSpan.FromSeconds(4), refresh: true);
    }

    private bool TryGetBehavior(Entity<WoundComponent> wound, out WolfmedConcussionBehavior behavior)
    {
        behavior = null!;
        return _prototypes.TryIndex(wound.Comp.Prototype, out var prototype) &&
               prototype.TryGetBehavior(wound.Comp.Severity, out behavior);
    }
}
