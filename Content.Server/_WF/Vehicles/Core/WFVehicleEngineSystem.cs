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

    private float _elapsed;

    public override void Initialize()
    {
        SubscribeNetworkEvent<WFVehicleControlRequest>(OnControl);
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

        if (ev.Accessories && engine.Electrics)
        {
            engine.Running = false;
            engine.Electrics = false;
            Dirty(uid, engine);
            return;
        }

        if (!engine.Running)
        {
            if (!ev.Accessories && engine.Fuel <= 0f)
            {
                _popup.PopupEntity(Loc.GetString("wf-vehicle-no-fuel"), user, user);
                return;
            }

            var matched = false;
            if (TryComp(uid, out VehicleLockComponent? vehicleLock))
            {
                foreach (var held in _hands.EnumerateHeld(user))
                {
                    if (TryComp(held, out VehicleKeyComponent? key) && key.KeyId != null && key.KeyId == vehicleLock.KeyId)
                        matched = true;
                }
            }

            if (!matched)
            {
                _popup.PopupEntity(Loc.GetString("wf-vehicle-key-required"), user, user);
                return;
            }
        }

        if (!engine.Running && !ev.Accessories)
        {
            if (engine.BatteryCharge < engine.StarterEnergy)
            {
                _popup.PopupEntity(Loc.GetString("wf-vehicle-battery-flat"), user, user);
                return;
            }
            engine.BatteryCharge -= engine.StarterEnergy;
        }
        engine.Electrics = true;
        if (!ev.Accessories)
            engine.Running = !engine.Running;
        Dirty(uid, engine);
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
