using Content.Shared._WF.Xenobiology;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Xenobiology;

/// <summary>
/// Slime feeding and splitting.
/// </summary>
public sealed partial class SlimeSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly SoundSpecifier BiteSound = new SoundPathSpecifier("/Audio/Effects/bite.ogg",
        AudioParams.Default.WithVariation(0.05f));

    private readonly List<(EntityUid Slime, int Amount)> _pendingSplits = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Splits run here rather than inside the NPC update that asked for them.
        foreach (var (slime, amount) in _pendingSplits)
        {
            TrySplit(slime, amount);
        }

        _pendingSplits.Clear();
    }

    /// <summary>
    /// Bites a target in reach, feeding the slime if it did damage.
    /// </summary>
    public bool TryEat(Entity<SlimeComponent> slime, EntityUid target)
    {
        if (!_interaction.InRangeUnobstructed(slime.Owner, target, range: 0.75f)
            || !HasComp<DamageableComponent>(target))
            return false;

        var dealt = _damageable.TryChangeDamage(target, slime.Comp.DamageOnEat, ignoreResistances: true, origin: slime);
        if (dealt == null)
            return false;

        _audio.PlayPvs(BiteSound, slime);

        var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(slime);
        if (direction.LengthSquared() > 0f)
        {
            RaiseNetworkEvent(new SlimeBiteAnimationMessage(GetNetEntity(slime), Angle.FromWorldVec(direction)),
                Filter.Pvs(slime, 0.5f));
        }

        if (dealt.AnyPositive())
            _hunger.ModifyHunger(slime, slime.Comp.NutritionOnHit.Float());

        return true;
    }

    /// <summary>
    /// Queues a split for the next update.
    /// </summary>
    public void QueueSplit(EntityUid slime, int amount)
    {
        _pendingSplits.Add((slime, amount));
    }

    /// <summary>
    /// Replaces the slime with <paramref name="amount"/> children that share its hunger and may mutate.
    /// </summary>
    public bool TrySplit(EntityUid uid, int amount)
    {
        if (amount <= 0 || TerminatingOrDeleted(uid) || !TryComp<SlimeComponent>(uid, out var slime))
            return false;

        var hunger = TryComp<HungerComponent>(uid, out var hungerComp) ? _hunger.GetHunger(hungerComp) / amount : 0f;
        var coords = Transform(uid).Coordinates;
        for (var i = 0; i < amount; i++)
        {
            var child = SpawnAtPosition(PickChild(slime), coords);
            if (!TryComp<SlimeComponent>(child, out var childSlime))
                continue;

            childSlime.MutationChance = slime.MutationChance;
            Dirty(child, childSlime);
            _hunger.SetHunger(child, hunger);
        }

        QueueDel(uid);
        return true;
    }

    private EntProtoId PickChild(SlimeComponent slime)
    {
        if (slime.MutationChance >= 1 && slime.MutationOnMaxSplit is { } max)
            return max;

        if (slime.SplitIntoMutation.Count > 0 && _random.Prob(slime.MutationChance.Float()))
            return _random.Pick(slime.SplitIntoMutation);

        return slime.SplitInto;
    }
}
