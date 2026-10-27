using Content.Shared._WF.Xenobiology;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Xenobiology;

/// <summary>
/// Food every slime knows about, so nearby slimes crowd the same target.
/// </summary>
public sealed partial class SlimeBrainSystem : EntitySystem
{
    /// <summary>
    /// How far a slime smells food, through walls.
    /// </summary>
    public const float FoodSearchRange = 5f;

    /// <summary>
    /// Slimes stop eating a target once it has this much damage.
    /// </summary>
    public static readonly FixedPoint2 TargetDamageThreshold = 100;

    private static readonly ProtoId<DamageContainerPrototype> EdibleContainer = "Biological";

    private readonly HashSet<EntityUid> _targetFood = new();

    public bool IsEdible(EntityUid target)
    {
        return !HasComp<SlimeComponent>(target)
            && HasComp<MobStateComponent>(target)
            && TryComp<DamageableComponent>(target, out var damage)
            && damage.DamageContainerID == EdibleContainer
            && damage.TotalDamage < TargetDamageThreshold;
    }

    public bool TryAddTargetFood(EntityUid target)
    {
        if (!IsEdible(target))
            return false;

        _targetFood.Add(target);
        return true;
    }

    /// <summary>
    /// Whether the target is known food that is still edible; drops it if not.
    /// </summary>
    public bool IsKnownFood(EntityUid target)
    {
        if (!_targetFood.Contains(target))
            return false;

        if (!Deleted(target) && IsEdible(target))
            return true;

        _targetFood.Remove(target);
        return false;
    }
}
