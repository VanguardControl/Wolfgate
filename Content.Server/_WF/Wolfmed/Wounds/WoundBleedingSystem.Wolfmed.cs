// WOLFGATE: W2. The bleeding rules an arterial bleed plays by, kept out of the vendored bleeding system,
// which carries only the three call sites that route through here.

using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.FixedPoint;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class WoundBleedingSystem
{
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;

    /// <summary>
    /// The rate multiplier a treatment earns on this wound. An arterial bleed may override the shared
    /// table, which is how a dressing slows it instead of nearly stopping it.
    /// </summary>
    private float GetTreatmentMultiplier(EntityUid wound, BleedingTreatment treatment)
    {
        if (_traits.TryGetBehavior(wound, out WolfmedArterialBleedBehavior behavior) &&
            behavior.TreatmentMultipliers.TryGetValue(treatment, out var multiplier))
            return multiplier;

        return TreatmentMultiplier(treatment);
    }

    /// <summary>Whether a topical's bloodloss modifier is allowed to eat this wound's bleeding severity.</summary>
    private bool AllowsTopicalBleedReduction(Entity<WoundComponent> wound) =>
        !_traits.TryGetBehavior(wound.AsNullable(), out WolfmedArterialBleedBehavior behavior) ||
        behavior.TopicalsReduceBleeding;

    /// <summary>
    /// Records a dressing on every untreated arterial bleed in the part. The dressing cannot close the
    /// wound or take its severity, so this is the only thing gauze achieves there: a slower rate.
    /// </summary>
    public bool BandageArterialBleeds(Entity<WoundableComponent?> part)
    {
        var bandaged = false;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State != WoundState.Open ||
                !_traits.TryGetBehavior(wound.Owner, out WolfmedArterialBleedBehavior _) ||
                !TryComp(wound, out WoundBleedingComponent? bleeding) ||
                bleeding.Treatment != BleedingTreatment.None ||
                bleeding.BleedingSeverity <= FixedPoint2.Zero)
                continue;

            bandaged |= SetTreatment(wound.Owner, BleedingTreatment.Bandaged);
        }

        return bandaged;
    }

    /// <summary>
    /// Whether a dressing can still do anything for this part. An arterial bleed that is already dressed keeps
    /// a rate above zero that no topical can lower, and asking "is it bleeding" there made gauze apply itself
    /// until the stack ran out.
    /// </summary>
    public bool CanDressBleeding(Entity<WoundableComponent?> part)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (!TryComp(wound, out WoundBleedingComponent? bleeding) || bleeding.CurrentRate <= 0f)
                continue;

            if (AllowsTopicalBleedReduction(wound) || bleeding.Treatment == BleedingTreatment.None)
                return true;
        }

        return false;
    }

    /// <summary>Whether the part still has a bleed that only a tourniquet or surgery will stop.</summary>
    public bool HasUndressableBleed(Entity<WoundableComponent?> part)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (TryComp(wound, out WoundBleedingComponent? bleeding) && bleeding.CurrentRate > 0f &&
                !AllowsTopicalBleedReduction(wound))
                return true;
        }

        return false;
    }
}
