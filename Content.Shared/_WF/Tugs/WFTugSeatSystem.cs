using Content.Shared._WF.Vehicles.Base;
using Content.Shared.Buckle.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WF.Tugs;

/// <summary>The tug's seat: the rider drives, and the tug never shoves its own rider off.</summary>
public sealed class WFTugSeatSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private RMCVehicleSystem _vehicles = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFTugComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<WFTugComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<WFTugComponent, UnstrappedEvent>(OnUnstrapped);
    }

    private void OnPreventCollide(Entity<WFTugComponent> ent, ref PreventCollideEvent args)
    {
        if (TryComp(args.OtherEntity, out BuckleComponent? buckle) && buckle.BuckledTo == ent.Owner)
            args.Cancelled = true;
    }

    private void OnStrapped(Entity<WFTugComponent> ent, ref StrappedEvent args)
    {
        // Contacts made while the rider stood on the tug would keep pushing them off the seat.
        _physics.RegenerateContacts(args.Buckle.Owner);

        if (_net.IsServer && TryComp(ent, out RMCVehicleComponent? vehicle))
            _vehicles.TrySetOperator((ent.Owner, vehicle), args.Buckle.Owner);
    }

    private void OnUnstrapped(Entity<WFTugComponent> ent, ref UnstrappedEvent args)
    {
        _physics.RegenerateContacts(args.Buckle.Owner);

        if (_net.IsServer && TryComp(ent, out RMCVehicleComponent? vehicle) && vehicle.Operator == args.Buckle.Owner)
            _vehicles.TryRemoveOperator((ent.Owner, vehicle));
    }
}
