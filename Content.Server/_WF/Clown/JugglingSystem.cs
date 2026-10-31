using System.Linq;
using Content.Shared._WF.Clown;
using Content.Shared.Actions;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Timing;

namespace Content.Server._WF.Clown;

/// <summary>Moves a clown's held items into the air while juggling and back to the hands when it stops.</summary>
public sealed partial class JugglingSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedMoverController _mover = default!;

    private const string JuggleContainerId = "juggle";
    private const string NoGravityMsg = "juggling-no-gravity";
    private const int MaxJuggledItems = 3;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JugglingComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<JugglingComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<JugglingComponent, JuggleActionEvent>(OnJuggle);

        SubscribeLocalEvent<JugglingActiveComponent, ComponentInit>(OnActiveInit);
        SubscribeLocalEvent<JugglingActiveComponent, ComponentShutdown>(OnActiveShutdown);

        // Ignore the walk toggle while juggling, or it would switch the player back to running.
        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.Walk, new JuggleWalkBlocker(), typeof(SharedMoverController))
            .Register<JugglingSystem>();

        SubscribeLocalEvent<JugglingActiveComponent, DidEquipHandEvent>(OnDidEquipHand);
        SubscribeLocalEvent<JugglingActiveComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<JugglingActiveComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<JugglingActiveComponent, DownedEvent>(OnDowned);
        SubscribeLocalEvent<JugglingActiveComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<JugglingActiveComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
        SubscribeLocalEvent<GravityChangedEvent>(OnGravityChanged);
    }

    private void OnMapInit(Entity<JugglingComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.JuggleAction, ent.Comp.JuggleActionId);
    }

    private void OnShutdown(Entity<JugglingComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.JuggleAction);
    }

    private void OnJuggle(Entity<JugglingComponent> ent, ref JuggleActionEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<JugglingActiveComponent>(ent))
            StopJuggling(ent);
        else
            StartJuggling(ent);

        args.Handled = true;
    }

    private void StartJuggling(EntityUid uid)
    {
        var held = _hands.EnumerateHeld(uid).Where(CanJuggle).ToList();
        if (held.Count < 2)
            return;

        if (_gravity.IsWeightless(uid))
        {
            _popup.PopupEntity(Loc.GetString(NoGravityMsg), uid, uid);
            return;
        }

        var container = _containers.EnsureContainer<Container>(uid, JuggleContainerId);
        var active = AddComp<JugglingActiveComponent>(uid);
        active.StartTime = _timing.CurTime;

        foreach (var item in held)
        {
            if (active.JuggledItems.Count >= MaxJuggledItems)
                break;

            if (_containers.TryGetContainingContainer(item, out var handContainer))
                _containers.Remove(item, handContainer, force: true);

            if (_containers.Insert(item, container))
                active.JuggledItems.Add(GetNetEntity(item));
        }

        Dirty(uid, active);

        _popup.PopupEntity(Loc.GetString("juggling-action-popup"), uid, uid);
    }

    private void StopJuggling(EntityUid uid)
    {
        if (!RemComp<JugglingActiveComponent>(uid))
            return;

        if (!_containers.TryGetContainer(uid, JuggleContainerId, out var container))
            return;

        foreach (var item in container.ContainedEntities.ToList())
        {
            _containers.Remove(item, container);
            _hands.TryPickupAnyHand(uid, item);
        }
    }

    private bool CanJuggle(EntityUid item)
        => !HasComp<VirtualItemComponent>(item) && !HasComp<UnremoveableComponent>(item);

    private void OnItemRemoved(Entity<JugglingActiveComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != JuggleContainerId)
            return;

        if (ent.Comp.JuggledItems.Remove(GetNetEntity(args.Entity)))
            Dirty(ent);
    }

    /// <summary>Puts the player in walk mode when juggling starts.</summary>
    private void OnActiveInit(Entity<JugglingActiveComponent> ent, ref ComponentInit args)
    {
        if (TryComp<InputMoverComponent>(ent.Owner, out var mover))
            _mover.SetSprinting((ent.Owner, mover), 0, true);
    }

    /// <summary>Returns the player to running when juggling ends.</summary>
    private void OnActiveShutdown(Entity<JugglingActiveComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<InputMoverComponent>(ent.Owner, out var mover))
            _mover.SetSprinting((ent.Owner, mover), 0, false);
    }

    private void OnDidEquipHand(Entity<JugglingActiveComponent> ent, ref DidEquipHandEvent args)
    {
        var item = args.Equipped;
        if (!CanJuggle(item))
            return;

        var netItem = GetNetEntity(item);

        if (ent.Comp.JuggledItems.Contains(netItem))
            return;

        if (ent.Comp.JuggledItems.Count >= MaxJuggledItems)
            return;

        var container = _containers.EnsureContainer<Container>(ent.Owner, JuggleContainerId);

        if (_containers.TryGetContainingContainer(item, out var handContainer))
            _containers.Remove(item, handContainer, force: true);

        if (_containers.Insert(item, container))
        {
            ent.Comp.JuggledItems.Add(netItem);
            Dirty(ent, ent.Comp);
        }
    }

    private void OnDamaged(Entity<JugglingActiveComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageDelta != null && args.DamageDelta.GetTotal() > FixedPoint2.Zero)
            StopJuggling(ent.Owner);
    }

    private void OnMobState(Entity<JugglingActiveComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState is MobState.Critical or MobState.Dead)
            StopJuggling(ent.Owner);
    }

    /// <summary>Slipping, stuns and paralysed legs all raise DownedEvent.</summary>
    private void OnDowned(Entity<JugglingActiveComponent> ent, ref DownedEvent args)
        => StopJuggling(ent.Owner);

    /// <summary>Shared by the two ways a juggling clown becomes weightless.</summary>
    private void StopWeightless(EntityUid uid)
    {
        _popup.PopupEntity(Loc.GetString(NoGravityMsg), uid, uid);
        StopJuggling(uid);
    }

    /// <summary>Catches the case of a clown walking off a gravity grid mid-juggle.</summary>
    private void OnParentChanged(Entity<JugglingActiveComponent> ent, ref EntParentChangedMessage args)
    {
        if (_gravity.IsWeightless(ent.Owner))
            StopWeightless(ent.Owner);
    }

    /// <summary>Catches the case of grid gravity being lost while a clown stands on it.</summary>
    private void OnGravityChanged(ref GravityChangedEvent ev)
    {
        if (ev.HasGravity)
            return;

        var query = EntityQueryEnumerator<JugglingActiveComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != ev.ChangedGridIndex)
                continue;

            StopWeightless(uid);
        }
    }
}
