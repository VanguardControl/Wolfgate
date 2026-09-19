using System.Linq;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stunnable;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// What a shock leaves past the surface: current takes the shortest path through the body, cooking tissue
/// on the way, and it does not care what organ is in that path. A strong enough discharge can reach the
/// heart, and the muscles it crosses lock and then let go of whatever they were holding.
/// </summary>
/// <remarks>
/// Server-side: organ health is. Answers <see cref="WolfmedWoundLifecycleEvent"/> because the directed
/// wound-lifecycle subscriptions are taken.
/// </remarks>
public sealed class WolfmedElectricalBurnSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        // A second shock merges into the existing wound, so worsening shocks them again.
        if (args.Kind == WolfmedWoundLifecycle.Removed || args.Severity <= args.OldSeverity ||
            !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedElectricalShockBehavior behavior) ||
            CompOrNull<BodyPartComponent>(args.Part)?.Body is not { } body)
            return;

        if (_random.Prob(Math.Clamp(behavior.OrganDamageChance, 0f, 1f)))
            TryShockOrgan(body, behavior.OrganSlot, behavior.OrganDamage);

        Spasm(body, behavior.Spasm, behavior.DropHeld);
    }

    /// <summary>
    /// Takes health off the organ in the named slot. Returns the organ, or null when the body has none
    /// there. Public so a test or a future effect can skip the roll.
    /// </summary>
    public EntityUid? TryShockOrgan(EntityUid body, string slot, FixedPoint2 damage)
    {
        if (damage <= FixedPoint2.Zero || TerminatingOrDeleted(body))
            return null;

        foreach (var (organ, component) in _body.GetBodyOrgans(body).ToArray())
        {
            if (!string.Equals(component.SlotId, slot, StringComparison.OrdinalIgnoreCase) ||
                !TryComp(organ, out WolfmedOrganComponent? health) ||
                health.Health <= FixedPoint2.Zero)
                continue;

            _organs.ChangeHealth((organ, health), -FixedPoint2.Min(damage, health.Health));
            return organ;
        }

        return null;
    }

    /// <summary>Locks the patient up for a moment and, optionally, empties their hands.</summary>
    public void Spasm(EntityUid body, TimeSpan duration, bool dropHeld)
    {
        if (TerminatingOrDeleted(body))
            return;

        if (dropHeld && HasComp<BodyComponent>(body))
        {
            foreach (var held in _hands.EnumerateHeld(body).ToArray())
                _hands.TryDrop(body, held, checkActionBlocker: false);
        }

        _stun.TryUpdateParalyzeDuration(body, duration);
    }
}
