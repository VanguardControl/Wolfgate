using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Reads the Wolfmed wound behaviors (<see cref="WolfmedArterialBleedBehavior"/>,
/// <see cref="WolfmedInfectionRiskBehavior"/>) off a live wound, and enforces the one rule that belongs
/// nowhere else: an arterial bleed refuses treatment until the bleeding itself has been stopped.
/// </summary>
public sealed class WolfmedWoundTraitSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundComponent, WoundTreatmentAttemptEvent>(OnTreatmentAttempt);
        SubscribeLocalEvent<WoundComponent, WoundCreatedEvent>(OnWoundCreated);
        SubscribeLocalEvent<WoundComponent, WoundChangedEvent>(OnWoundChanged);
        SubscribeLocalEvent<WoundComponent, WoundRemovedEvent>(OnWoundRemoved);
    }

    // Onyx refreshes movement speed off fracture events, which a limb-penalty wound does not raise.
    // The relay is W3's extension point: the directed subscriptions for these three events are all taken,
    // so other Wolfmed systems (concussion, organ contusion) answer the broadcast instead.
    private void OnWoundCreated(Entity<WoundComponent> wound, ref WoundCreatedEvent args)
    {
        RefreshLimb(wound, args.Part);
        Relay(WolfmedWoundLifecycle.Created, args.Part, wound, FixedPoint2.Zero);
    }

    private void OnWoundChanged(Entity<WoundComponent> wound, ref WoundChangedEvent args)
    {
        RefreshLimb(wound, args.Part);
        Relay(WolfmedWoundLifecycle.Changed, args.Part, wound, args.OldSeverity);
    }

    private void OnWoundRemoved(Entity<WoundComponent> wound, ref WoundRemovedEvent args)
    {
        RefreshLimb(wound, args.Part);
        Relay(WolfmedWoundLifecycle.Removed, args.Part, wound, wound.Comp.Severity);
    }

    private void Relay(WolfmedWoundLifecycle kind, EntityUid part, Entity<WoundComponent> wound, FixedPoint2 old)
    {
        var severity = kind == WolfmedWoundLifecycle.Removed ? FixedPoint2.Zero : wound.Comp.Severity;
        var relayed = new WolfmedWoundLifecycleEvent(kind, part, wound, wound.Comp.Prototype, old, severity);
        RaiseLocalEvent(ref relayed);
    }

    private void RefreshLimb(Entity<WoundComponent> wound, EntityUid part)
    {
        if (!TryGetBehavior(wound.AsNullable(), out WolfmedLimbPenaltyBehavior _) ||
            CompOrNull<BodyPartComponent>(part)?.Body is not { } body)
            return;

        _movement.RefreshMovementSpeedModifiers(body);
    }

    private void OnTreatmentAttempt(Entity<WoundComponent> wound, ref WoundTreatmentAttemptEvent args)
    {
        if (args.Cancelled || !TryGetBehavior(wound.AsNullable(), out WolfmedArterialBleedBehavior behavior) ||
            !behavior.RequiresStoppedBleedToTreat)
            return;

        // No bleeding component, or a rate already held at zero by a tourniquet, clamp or cautery: the
        // wound can be closed. Anything still pumping is refused, dressings included.
        if (TryComp(wound, out WoundBleedingComponent? bleeding) && bleeding.CurrentRate > 0f)
            args.Cancelled = true;
    }

    /// <summary>The wound's behavior of this type at its current severity, if its prototype declares one.</summary>
    public bool TryGetBehavior<T>(Entity<WoundComponent?> wound, out T behavior) where T : WoundBehavior
    {
        behavior = null!;
        return Resolve(wound, ref wound.Comp, false) &&
               _prototypes.TryIndex(wound.Comp.Prototype, out var prototype) &&
               prototype.TryGetBehavior(wound.Comp.Severity, out behavior);
    }

    /// <summary>
    /// How much more likely this wound is to become infected than an ordinary one. 1 for everything that
    /// says nothing. W5's infection timer is the intended reader.
    /// </summary>
    public float GetInfectionRisk(Entity<WoundComponent?> wound) =>
        TryGetBehavior(wound, out WolfmedInfectionRiskBehavior behavior) ? behavior.RiskMultiplier : 1f;

    /// <summary>The highest infection risk among the wounds a part is carrying.</summary>
    public float GetPartInfectionRisk(Entity<WoundableComponent?> part)
    {
        var risk = 1f;
        foreach (var wound in _wounds.GetWounds(part))
            risk = Math.Max(risk, GetInfectionRisk(wound.Owner));

        return risk;
    }

    /// <summary>
    /// The worst limb penalty the part's wounds impose, or false when none of them impose one.
    /// <paramref name="mobility"/> picks the movement multiplier (which scales down) over the
    /// manipulation duration multiplier (which scales up).
    /// </summary>
    public bool TryGetLimbPenalty(Entity<WoundableComponent?> part, bool mobility, out float modifier)
    {
        modifier = 1f;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !TryGetBehavior(wound.Owner, out WolfmedLimbPenaltyBehavior behavior))
                continue;

            var value = mobility ? behavior.MovementModifier : behavior.ManipulationModifier;
            modifier = mobility ? Math.Min(modifier, value) : Math.Max(modifier, value);
        }

        return modifier != 1f;
    }

    /// <summary>Whether a tourniquet on this part can do anything for this wound.</summary>
    public bool CanTourniquet(Entity<WoundComponent?> wound, EntityUid part)
    {
        if (!TryGetBehavior(wound, out WolfmedArterialBleedBehavior behavior))
            return true;

        return TryComp(part, out BodyPartComponent? bodyPart) &&
               behavior.TourniquetableParts.Contains(bodyPart.PartType);
    }

    /// <summary>Whether a tourniquet on this part would stop any of the bleeding it is currently doing.</summary>
    public bool CanTourniquetPart(Entity<WoundableComponent?> part)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (TryComp(wound, out WoundBleedingComponent? bleeding) && bleeding.CurrentRate > 0f &&
                CanTourniquet(wound.Owner, part))
                return true;
        }

        return false;
    }
}
