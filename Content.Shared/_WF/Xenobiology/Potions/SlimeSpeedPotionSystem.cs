using Content.Shared.Clothing;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenobiology.Potions;

/// <summary>
/// Clothing a speed potion was used on; each use halves what is left of its slowdown.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlimeSpeedBoostedComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Applications;
}

/// <summary>
/// Applies speed potions to clothing and scales its speed modifier.
/// </summary>
public sealed partial class SlimeSpeedPotionSystem : EntitySystem
{
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeSpeedPotionComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<SlimeSpeedBoostedComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshSpeed);
    }

    private void OnAfterInteract(Entity<SlimeSpeedPotionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target
            || !TryComp<ClothingSpeedModifierComponent>(target, out var modifier))
            return;

        args.Handled = true;
        var boosted = EnsureComp<SlimeSpeedBoostedComponent>(target);
        boosted.Applications++;
        Dirty(target, boosted);

        if (_container.TryGetContainingContainer((target, null), out var container))
            _movementSpeed.RefreshMovementSpeedModifiers(container.Owner);

        _popup.PopupClient(Loc.GetString("slime-potion-speed-applied",
            ("target", target),
            ("walk", Boosted(modifier.WalkModifier, boosted.Applications)),
            ("sprint", Boosted(modifier.SprintModifier, boosted.Applications))), args.User, args.User);
        PredictedQueueDel(ent.Owner);
    }

    private void OnRefreshSpeed(Entity<SlimeSpeedBoostedComponent> ent, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (!TryComp<ClothingSpeedModifierComponent>(ent, out var modifier) || !_toggle.IsActivated(ent.Owner))
            return;

        args.Args.ModifySpeed(Factor(modifier.WalkModifier, ent.Comp.Applications),
            Factor(modifier.SprintModifier, ent.Comp.Applications));
    }

    /// <summary>
    /// The modifier after halving its slowdown <paramref name="applications"/> times.
    /// </summary>
    public static float Boosted(float modifier, int applications)
    {
        return 1f - (1f - modifier) / MathF.Pow(2f, applications);
    }

    private static float Factor(float modifier, int applications)
    {
        if (modifier <= 0f || modifier >= 1f)
            return 1f;

        return Boosted(modifier, applications) / modifier;
    }
}
