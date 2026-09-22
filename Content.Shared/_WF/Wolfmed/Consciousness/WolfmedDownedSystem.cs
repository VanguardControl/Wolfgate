using Content.Shared._Goobstation.DoAfter;
using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Events;
using Content.Shared.Pulling.Events;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// What Downed costs: the body stays on the floor and can only reach itself. Crawling, talking, radio and
/// examining are untouched; doors, buttons, containers, other people, melee and guns are not.
/// </summary>
/// <remarks>
/// Every restriction is an existing ActionBlocker attempt event, so no item, door or gun needed changing.
/// The floor itself is Wolfgate's standing state, the same one knockdown uses.
/// </remarks>
public sealed class WolfmedDownedSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedDownedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WolfmedDownedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WolfmedDownedComponent, StandAttemptEvent>(OnStandAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, ShotAttemptedEvent>(OnShotAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, ThrowAttemptEvent>(OnThrowAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, StartPullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, GetDoAfterDelayMultiplierEvent>(OnGetDelayMultiplier);
    }

    private void OnStartup(Entity<WolfmedDownedComponent> ent, ref ComponentStartup args)
    {
        // Held items stay held: everything a Downed player is allowed to do is done with them.
        _standing.Down(ent, true, false, false);
        _alerts.ShowAlert(ent, ent.Comp.Alert);
        _blocker.UpdateCanMove(ent);
    }

    private void OnShutdown(Entity<WolfmedDownedComponent> ent, ref ComponentShutdown args)
    {
        _alerts.ClearAlert(ent, ent.Comp.Alert);
        if (TerminatingOrDeleted(ent))
            return;

        // Anything else holding them down (knockdown, crit, buckling) refuses this on its own.
        _standing.Stand(ent);
        _blocker.UpdateCanMove(ent);
    }

    private void OnStandAttempt(EntityUid uid, WolfmedDownedComponent component, StandAttemptEvent args)
    {
        if (component.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }

    /// <summary>The whole of "self only": the target has to be the body or something the body is carrying.</summary>
    private void OnInteractionAttempt(Entity<WolfmedDownedComponent> ent, ref InteractionAttemptEvent args)
    {
        // AUTODOC: a pod marked reachable is the one thing off the body a Downed player may still touch.
        if (args.Target is { } target && !IsSelfOrCarried(ent, target) &&
            !HasComp<WolfmedDownedReachableComponent>(target))
            args.Cancelled = true;
    }

    private void OnAttackAttempt(EntityUid uid, WolfmedDownedComponent component, AttackAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnShotAttempt(Entity<WolfmedDownedComponent> ent, ref ShotAttemptedEvent args)
    {
        // No one-handed pistol fire from the floor: a gun is out entirely while Downed.
        args.Cancel();
    }

    private void OnThrowAttempt(EntityUid uid, WolfmedDownedComponent component, ThrowAttemptEvent args)
    {
        args.Cancel();
    }

    /// <summary>
    /// Only the attempt raised on the puller. A medic dragging the body away is the point of Downed, so the
    /// event raised on the pullable is left alone.
    /// </summary>
    private void OnPullAttempt(EntityUid uid, WolfmedDownedComponent component, StartPullAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnGetDelayMultiplier(Entity<WolfmedDownedComponent> ent, ref GetDoAfterDelayMultiplierEvent args)
    {
        args.Multiplier *= ent.Comp.DoAfterMultiplier;
    }

    /// <summary>True for the body itself and for anything in its hands, pockets, bag or belt.</summary>
    public bool IsSelfOrCarried(EntityUid user, EntityUid target)
    {
        if (target == user)
            return true;

        var parent = Transform(target).ParentUid;
        while (parent.IsValid())
        {
            if (parent == user)
                return true;

            parent = Transform(parent).ParentUid;
        }

        return false;
    }
}
