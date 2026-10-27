using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenobiology.Potions;

/// <summary>
/// The potions used by clicking a slime, an extract or a body: steroid, mutation, enhancer and mind transference.
/// </summary>
public sealed partial class SlimePotionSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeSteroidPotionComponent, AfterInteractEvent>(OnSteroid);
        SubscribeLocalEvent<SlimeMutationPotionComponent, AfterInteractEvent>(OnMutation);
        SubscribeLocalEvent<SlimeExtractEnhancerPotionComponent, AfterInteractEvent>(OnEnhancer);
        SubscribeLocalEvent<SlimeMindTransferencePotionComponent, AfterInteractEvent>(OnMindTransference);
    }

    private void OnSteroid(Entity<SlimeSteroidPotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !TryComp<SlimeComponent>(args.Target, out var slime))
            return;

        args.Handled = true;
        slime.SlimeSteroidAmount++;
        Dirty(args.Target.Value, slime);
        _popup.PopupClient(Loc.GetString("slime-potion-steroid-applied",
            ("target", args.Target.Value), ("amount", slime.SlimeSteroidAmount)), args.User, args.User);
        PredictedQueueDel(ent.Owner);
    }

    private void OnMutation(Entity<SlimeMutationPotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !TryComp<SlimeComponent>(args.Target, out var slime))
            return;

        args.Handled = true;
        if (!TryApplyMutation((args.Target.Value, slime), ent.Comp.Amount, out var message))
        {
            _popup.PopupClient(message, args.User, args.User);
            return;
        }

        _popup.PopupClient(message, args.User, args.User);
        PredictedQueueDel(ent.Owner);
    }

    /// <summary>
    /// The mutation change of a potion prototype, or zero if it isn't one.
    /// </summary>
    public FixedPoint2 PotionAmount(EntProtoId potion)
    {
        return _proto.Index(potion).TryComp<SlimeMutationPotionComponent>(out var comp, _factory)
            ? comp.Amount
            : FixedPoint2.Zero;
    }

    /// <summary>
    /// Shifts a slime's mutation chance, clamped to 0..1. Fails when it is already at the limit it moves towards.
    /// </summary>
    public bool TryApplyMutation(Entity<SlimeComponent> slime, FixedPoint2 amount, out string message)
    {
        var chance = slime.Comp.MutationChance;
        if (amount > 0 && chance >= 1)
        {
            message = Loc.GetString("slime-potion-mutation-max", ("target", slime.Owner));
            return false;
        }

        if (amount < 0 && chance <= 0)
        {
            message = Loc.GetString("slime-potion-mutation-min", ("target", slime.Owner));
            return false;
        }

        slime.Comp.MutationChance = FixedPoint2.Clamp(chance + amount, 0, 1);
        Dirty(slime);
        message = Loc.GetString("slime-potion-mutation-chance",
            ("target", slime.Owner), ("chance", slime.Comp.MutationChance * 100));
        return true;
    }

    private void OnEnhancer(Entity<SlimeExtractEnhancerPotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !TryComp<SlimeExtractComponent>(args.Target, out var extract))
            return;

        args.Handled = true;
        extract.RemainingUses++;
        Dirty(args.Target.Value, extract);
        _popup.PopupClient(Loc.GetString("slime-potion-enhancer-applied",
            ("target", args.Target.Value), ("uses", extract.RemainingUses)), args.User, args.User);
        PredictedQueueDel(ent.Owner);
    }

    private void OnMindTransference(Entity<SlimeMindTransferencePotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || target == args.User)
            return;

        if (!TryComp<MindContainerComponent>(args.User, out var userMind) || userMind.Mind is not { } mind)
            return;

        if (!TryComp<MindContainerComponent>(target, out var targetMind) || targetMind.HasMind)
            return;

        args.Handled = true;
        _mind.TransferTo(mind, target);
        PredictedQueueDel(ent.Owner);
    }
}
