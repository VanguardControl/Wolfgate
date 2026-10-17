using Robust.Shared.Containers;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;

namespace Content.Server._WF.Vehicles.Core;

/// <summary>Authorizes ignition with the matching key and consumes or replenishes vehicle fuel.</summary>
public sealed class WFVehicleEngineSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;

    [Dependency] private SharedContainerSystem _containers = default!;

    private float _elapsed;

    public override void Initialize()
    {
        SubscribeNetworkEvent<WFVehicleControlRequest>(OnControl);
        SubscribeLocalEvent<WFVehicleEngineComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WFVehicleEngineComponent, EntRemovedFromContainerMessage>(OnKeyRemoved);
        SubscribeLocalEvent<WFVehicleEngineComponent, InteractUsingEvent>(OnRefuel);
    }

    private void OnControl(WFVehicleControlRequest ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !_blocker.CanConsciouslyPerformAction(user))
            return;

        var uid = GetEntity(ev.Vehicle);
        if (!TryComp(uid, out RMCVehicleComponent? vehicle) || vehicle.Operator != user ||
            !TryComp(uid, out WFVehicleEngineComponent? engine))
            return;

        if (ev.Brake)
        {
            engine.ParkingBrake = !engine.ParkingBrake;
            Dirty(uid, engine);
            return;
        }

        var slot = _containers.EnsureContainer<ContainerSlot>(uid, WFVehicleEngineComponent.IgnitionContainer);
        if (ev.EjectKey)
        {
            if (engine.Electrics || slot.ContainedEntity is not { } inserted)
                return;
            _hands.TryPickupAnyHand(user, inserted);
            return;
        }

        if (ev.Accessories)
        {
            if (slot.ContainedEntity == null)
            {
                if (!_hands.TryGetActiveItem(user, out var held) ||
                    !TryComp(held, out VehicleKeyComponent? key) ||
                    !TryComp(uid, out VehicleLockComponent? vehicleLock) ||
                    key.KeyId == null || key.KeyId != vehicleLock.KeyId)
                {
                    _popup.PopupEntity(Loc.GetString("wf-vehicle-key-required"), user, user);
                    return;
                }
                if (_hands.TryDropIntoContainer(user, held.Value, slot))
                {
                    engine.IgnitionKey = held;
                    Dirty(uid, engine);
                }
                return;
            }
            engine.Electrics = !engine.Electrics;
            if (!engine.Electrics)
                engine.Running = false;
            Dirty(uid, engine);
            return;
        }

        if (!engine.Electrics || slot.ContainedEntity == null)
            return;
        if (!engine.Running)
        {
            if (engine.Fuel <= 0f)
            {
                _popup.PopupEntity(Loc.GetString("wf-vehicle-no-fuel"), user, user);
                return;
            }
            if (engine.BatteryCharge < engine.StarterEnergy)
            {
                _popup.PopupEntity(Loc.GetString("wf-vehicle-battery-flat"), user, user);
                return;
            }
            engine.BatteryCharge -= engine.StarterEnergy;
        }
        engine.Running = !engine.Running;
        Dirty(uid, engine);
    }

    private void OnInit(Entity<WFVehicleEngineComponent> ent, ref ComponentInit args)
    {
        _containers.EnsureContainer<ContainerSlot>(ent, WFVehicleEngineComponent.IgnitionContainer);
    }

    private void OnKeyRemoved(Entity<WFVehicleEngineComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != WFVehicleEngineComponent.IgnitionContainer)
            return;
        ent.Comp.IgnitionKey = null;
        ent.Comp.Electrics = false;
        ent.Comp.Running = false;
        Dirty(ent);
    }

    private void OnRefuel(Entity<WFVehicleEngineComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp(args.Used, out DrainableSolutionComponent? drain) ||
            !_solutions.TryGetSolution(args.Used, drain.Solution, out var solutionEntity, out var solution))
            return;

        var available = solution.GetTotalPrototypeQuantity("WeldingFuel");
        if (available <= FixedPoint2.Zero)
            return;

        args.Handled = true;
        if (ent.Comp.Running)
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-refuel-stop"), args.User, args.User);
            return;
        }

        var amount = FixedPoint2.Min(available, FixedPoint2.New(MathF.Max(0f, ent.Comp.Capacity - ent.Comp.Fuel)));
        if (amount <= FixedPoint2.Zero)
            return;

        _solutions.RemoveReagent(solutionEntity.Value, "WeldingFuel", amount);
        ent.Comp.Fuel = MathF.Min(ent.Comp.Capacity, ent.Comp.Fuel + (float) amount);
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("wf-vehicle-refueled", ("amount", (float) amount)), args.User, args.User);
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 0.25f)
            return;

        var elapsed = _elapsed;
        _elapsed = 0f;
        var query = EntityQueryEnumerator<WFVehicleEngineComponent, GridVehicleMoverComponent>();
        while (query.MoveNext(out var uid, out var engine, out var mover))
        {
            if (!engine.Running)
                continue;

            var load = Math.Clamp(MathF.Abs(mover.CurrentSpeed) / MathF.Max(1f, mover.MaxSpeed), 0f, 1f);
            engine.Fuel = MathF.Max(0f, engine.Fuel - elapsed * (engine.IdleConsumption + engine.DrivingConsumption * load));
            if (engine.Fuel <= 0f)
                engine.Running = false;
            Dirty(uid, engine);
        }
    }
}
