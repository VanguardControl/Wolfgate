using Content.Server._Mono.ShipShields.Components;
using Content.Server._NF.Power.Components;
using Content.Server._WG.ShipPa;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Pinpointer;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Wires;
using System.Numerics;
using Content.Server.Research.Systems;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared.Research.Components;
using Robust.Shared.Map;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Economy.Components;
using Content.Shared._WG.ShipPa;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Pinpointer;
using Content.Server.Storage.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Shipyard;

/// <summary>
/// Puts a used ship back on its feet after it has been loaded from YAML. The loader marks everything
/// it reads as map-initialised without ever raising MapInitEvent, so anything a system only sets up
/// on map init is simply missing: device net addresses, wire layouts, nav map data, gravity. Only
/// the setup that is safe to repeat is redone here - a blanket map init would refill every locker,
/// vending machine and light on the hull.
/// </summary>
public sealed partial class UsedShipReinitSystem : EntitySystem
{
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private NavMapSystem _navMap = default!;
    [Dependency] private PowerChargeSystem _powerCharge = default!;
    [Dependency] private ShipPaSystem _shipPa = default!;
    [Dependency] private WiresSystem _wires = default!;
    [Dependency] private SharedHardpointSystem _hardpoints = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Walks a freshly loaded grid and redoes the map-init work its entities never got. Every step is
    /// idempotent and skips silently when the component or the system behind it is not there.
    /// </summary>
    public void ReinitLoadedShip(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid))
            return;

        // The seller's shield bubble: the emitter's own link to it is not saved, so the leftover
        // bubble would wedge the emitter into never raising a new one.
        RemComp<ShipShieldedComponent>(grid);

        var pending = new Queue<EntityUid>();
        pending.Enqueue(grid);
        var researchClients = new List<EntityUid>();
        var researchServers = new List<EntityUid>();

        while (pending.TryDequeue(out var parent))
        {
            var children = Transform(parent).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<ShipShieldComponent>(child))
                {
                    QueueDel(child);
                    continue;
                }

                pending.Enqueue(child);
                ReinitEntity(child);

                if (HasComp<ResearchServerComponent>(child))
                    researchServers.Add(child);

                if (TryComp<ResearchClientComponent>(child, out var client) && client.Server == null)
                    researchClients.Add(child);
            }
        }

        // A research client finds its grid's servers at map init, which a load never raises.
        foreach (var client in researchClients)
        {
            foreach (var server in researchServers)
            {
                _research.RegisterClient(client, server);
            }
        }

        // Nav map chunks are rebuilt from the tiles, not saved, so the console has nothing to draw.
        RemComp<NavMapComponent>(grid);
        _navMap.EnsureGridNavMap(grid);
    }

    /// <summary>
    /// The same re-init for loose entities that were loaded back onto a grid or map that stayed, such as a planet's
    /// ground: each entity's own setup, and research clients paired with the servers that came back on their grid.
    /// </summary>
    public void ReinitLoadedEntities(List<EntityUid> loaded)
    {
        List<EntityUid>? clients = null;
        List<EntityUid>? servers = null;
        foreach (var uid in loaded)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            // A shield bubble's link to its emitter is not saved; the emitter raises a new one.
            if (HasComp<ShipShieldComponent>(uid))
            {
                QueueDel(uid);
                continue;
            }

            ReinitEntity(uid);
            if (HasComp<ResearchServerComponent>(uid))
                (servers ??= new List<EntityUid>()).Add(uid);

            if (TryComp<ResearchClientComponent>(uid, out var client) && client.Server == null)
                (clients ??= new List<EntityUid>()).Add(uid);
        }

        if (clients == null || servers == null)
            return;

        foreach (var client in clients)
        {
            var grid = Transform(client).GridUid;
            foreach (var server in servers)
            {
                if (Transform(server).GridUid == grid)
                    _research.RegisterClient(client, server);
            }
        }
    }

    private void ReinitEntity(EntityUid uid)
    {
        if (TryComp<DeviceNetworkComponent>(uid, out var device))
            _deviceNetwork.ReinitLoadedDevice(uid, device);

        if (TryComp<WiresComponent>(uid, out var wires))
            _wires.ReinitLoadedWires(uid, wires);

        if (TryComp<ShipShieldEmitterComponent>(uid, out var emitter))
            ResetShieldEmitter(uid, emitter);

        if (TryComp<ShipPaSpeakerComponent>(uid, out var speaker))
            _shipPa.ReindexSpeaker((uid, speaker));

        if (TryComp<PowerChargeComponent>(uid, out var charge))
            _powerCharge.ReinitLoadedCharge(uid, charge);

        ResetUpgradeBaselines(uid);
        EnsureCashSlot(uid);
        RelinkUnsaved(uid);
    }

    /// <summary>
    /// Redoes links and figures that are not data fields, so a load leaves them empty: a ship gun's hardpoint (without
    /// it the gun never joins gunnery), which locker an item is in (its air comes from there), and a gun's modified
    /// fire rate and spread (zero until something refreshes them, and a gun with no fire rate can't shoot).
    /// </summary>
    private void RelinkUnsaved(EntityUid uid)
    {
        if (TryComp<HardpointAnchorableOnlyComponent>(uid, out var mount)
            && !_hardpoints.WgIsMounted(mount)
            && Transform(uid).Anchored)
        {
            _hardpoints.TryAnchorToHardpoint(uid, mount);
        }

        if (TryComp<EntityStorageComponent>(uid, out var storage) && storage.Contents != null)
        {
            foreach (var held in storage.Contents.ContainedEntities)
            {
                EnsureComp<InsideEntityStorageComponent>(held).Storage = uid;
            }
        }

        if (HasComp<GunComponent>(uid))
            _guns.RefreshModifiers(uid);

        // A reactor or turbine makes its inlet and outlet pipes when it has none, and its links to them are not
        // saved: without these it would make a second pair beside the saved ones on every load.
        if (TryComp<TurbineComponent>(uid, out var turbine))
        {
            turbine.InletEnt ??= FindMachinePipe(uid, turbine.PipePrototype, turbine.InletPos);
            turbine.OutletEnt ??= FindMachinePipe(uid, turbine.PipePrototype, turbine.OutletPos);
        }

        if (TryComp<NuclearReactorComponent>(uid, out var reactor))
        {
            reactor.InletEnt ??= FindMachinePipe(uid, reactor.PipePrototype, reactor.InletPos);
            reactor.OutletEnt ??= FindMachinePipe(uid, reactor.PipePrototype, reactor.OutletPos);
        }
    }

    /// <summary>The pipe of a prototype that sits at an offset from a machine, wherever it is parented; null if none.</summary>
    private EntityUid? FindMachinePipe(EntityUid machine, EntProtoId prototype, Vector2 offset)
    {
        var want = _transform.ToMapCoordinates(new EntityCoordinates(machine, offset)).Position;
        var xform = Transform(machine);
        var owners = new List<EntityUid> { machine };
        if (xform.GridUid is { } grid && grid != machine)
            owners.Add(grid);

        // On bare planet ground the machine and its pipes hang off the map.
        if (xform.ParentUid.IsValid() && !owners.Contains(xform.ParentUid))
            owners.Add(xform.ParentUid);

        foreach (var owner in owners)
        {
            var children = Transform(owner).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (MetaData(child).EntityPrototype?.ID != prototype.Id)
                    continue;

                if ((_transform.GetWorldPosition(child) - want).LengthSquared() < 0.25f)
                    return child;
            }
        }

        return null;
    }

    /// <summary>
    /// Clears an emitter's dead link to the old bubble and undoes any ramping the hull lived through,
    /// which multiplies the draw fields in place and would otherwise compound on every resale.
    /// </summary>
    private void ResetShieldEmitter(EntityUid uid, ShipShieldEmitterComponent emitter)
    {
        emitter.Shield = null;
        emitter.Shielded = null;

        if (MetaData(uid).EntityPrototype is { } proto
            && proto.TryComp<ShipShieldEmitterComponent>(out var original, _compFactory))
        {
            emitter.BaseDraw = original.BaseDraw;
            emitter.MaxDraw = original.MaxDraw;
        }

        if (TryComp<ShipShieldRampingLoadComponent>(uid, out var ramping))
            ramping.NextUpdate = _timing.CurTime + ramping.MultiplicationInterval;
    }

    /// <summary>
    /// Recaptures the unmodified power figures machine upgrades scale from. They are not data fields,
    /// so a loaded machine starts at zero and the next parts refresh would zero its draw or supply.
    /// </summary>
    private void ResetUpgradeBaselines(EntityUid uid)
    {
        if (TryComp<UpgradePowerDrawComponent>(uid, out var draw))
        {
            if (TryComp<PowerConsumerComponent>(uid, out var consumer))
                draw.BaseLoad = consumer.DrawRate;
            else if (TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
                draw.BaseLoad = receiver.Load;
        }

        if (TryComp<UpgradePowerSupplierComponent>(uid, out var supplier)
            && TryComp<PowerSupplierComponent>(uid, out var power))
        {
            supplier.BaseSupplyRate = power.MaxSupply;
        }

        if (TryComp<UpgradePowerSupplyRampingComponent>(uid, out var ramping)
            && TryComp<PowerNetworkBatteryComponent>(uid, out var battery))
        {
            ramping.BaseRampRate = battery.SupplyRampRate;
        }
    }

    /// <summary>
    /// Puts a credit receiver's cash slot back. The slot dictionary is a read-only data field, so a
    /// slot added at map init is never written out and the machine comes back taking no money.
    /// </summary>
    private void EnsureCashSlot(EntityUid uid)
    {
        if (!TryComp<CreditReceiverComponent>(uid, out var receiver)
            || _itemSlots.TryGetSlot(uid, receiver.CashSlotName, out _))
        {
            return;
        }

        _itemSlots.AddItemSlot(uid, receiver.CashSlotName, receiver.CashSlot);
    }
}
