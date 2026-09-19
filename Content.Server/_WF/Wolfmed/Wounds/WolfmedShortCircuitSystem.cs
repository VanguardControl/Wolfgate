using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Stunnable;
using Robust.Server.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// A short circuit inside a chassis: the frame locks up for a moment and the part throws sparks. The
/// mechanical counterpart of <see cref="WolfmedElectricalBurnSystem"/>, which has an organ to reach and
/// hands to open; a chassis has neither, so what a short costs is the moment and the noise that tells
/// everyone nearby what just happened.
/// </summary>
/// <remarks>
/// Server-side for the sound and the effect spawn. Answers <see cref="WolfmedWoundLifecycleEvent"/>
/// because the directed wound-lifecycle subscriptions are taken.
/// </remarks>
public sealed class WolfmedShortCircuitSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        // A second shock merges into the existing wound, so worsening shorts the chassis again.
        if (args.Kind == WolfmedWoundLifecycle.Removed || args.Severity <= args.OldSeverity ||
            !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedShortCircuitBehavior behavior) ||
            CompOrNull<BodyPartComponent>(args.Part)?.Body is not { } body)
            return;

        Arc(body, behavior);
    }

    /// <summary>Locks the chassis up and sparks. Public so a test and any future arc source can call it.</summary>
    public void Arc(EntityUid body, WolfmedShortCircuitBehavior behavior)
    {
        if (TerminatingOrDeleted(body))
            return;

        if (behavior.Effect is { } effect)
            Spawn(effect, Transform(body).Coordinates);

        if (behavior.Sound != null)
            _audio.PlayPvs(behavior.Sound, body);

        if (behavior.Stun > TimeSpan.Zero)
            _stun.TryUpdateParalyzeDuration(body, behavior.Stun);
    }
}
