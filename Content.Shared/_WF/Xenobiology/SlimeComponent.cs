using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenobiology;

/// <summary>
/// A xenobiology slime: what it eats, what it splits into and the extract it yields.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlimeComponent : Component
{
    /// <summary>
    /// Damage dealt to the target on each bite.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public DamageSpecifier DamageOnEat = new();

    /// <summary>
    /// Nutrition gained on each bite that did damage.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public FixedPoint2 NutritionOnHit;

    /// <summary>
    /// What this slime splits into when it doesn't mutate.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId SplitInto;

    /// <summary>
    /// The extract the slime processor makes from this slime.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId Extract;

    /// <summary>
    /// Chance, from 0 to 1, that each child mutates.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public FixedPoint2 MutationChance;

    /// <summary>
    /// Slimes a mutating child can become. Empty means it never mutates.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntProtoId> SplitIntoMutation = new();

    /// <summary>
    /// What every child becomes at 100% mutation chance, if set.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId? MutationOnMaxSplit;

    /// <summary>
    /// Steroid potions applied; each adds one extract when processed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int SlimeSteroidAmount;
}
