using Content.Server._WG.AsteroidMining;
using Content.Server._WG.Dormancy;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.Power.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared.Power.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.ColdStorage;

/// <summary>Which grids may enter stasis, and whether a player ship is working.</summary>
public sealed partial class WGGridStasisSystem
{
    [Dependency] private WGDormancySystem _dormancy = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private readonly HashSet<Entity<ApcComponent>> _apcs = new();
    private readonly HashSet<Entity<PowerSupplierComponent>> _suppliers = new();

    /// <summary>Starts watching new grids and refreshes what decides each one's policy, which rarely changes.</summary>
    private void RefreshRegistry()
    {
        var query = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (!_grids.TryGetValue(uid, out var rec))
                _grids[uid] = rec = new WGStasisGrid { Grid = uid };

            rec.Policy = PolicyOf(uid, xform);
        }
    }

    /// <summary>What may happen to a grid by where it is and who owns it.</summary>
    public WGStasisPolicy PolicyOf(EntityUid grid, TransformComponent xform)
    {
        // Planets and anything standing on one are Dormancy's; a jump, a vehicle's cabin and a rock are never worth it.
        if (xform.MapUid is not { } map
            || map == grid
            || xform.ParentUid != map
            || _dormancy.PlanetOf(map) != null
            || HasComp<FTLMapComponent>(map)
            || HasComp<VehicleInteriorLinkComponent>(map)
            || HasComp<VehicleInteriorComponent>(grid)
            || HasComp<WGEncounterGridComponent>(grid)
            || HasComp<WGCrewShipProfileComponent>(grid)
            || HasComp<WGAsteroidGridComponent>(grid)
            || IsStation(grid))
            return WGStasisPolicy.Never;

        return HasComp<ShuttleDeedComponent>(grid) ? WGStasisPolicy.PlayerShip : WGStasisPolicy.Ownerless;
    }

    /// <summary>A station's grid. A bought ship may be its own station for late joins, but its deed makes it a ship.</summary>
    private bool IsStation(EntityUid grid)
    {
        return HasComp<StationMemberComponent>(grid) && !HasComp<ShuttleDeedComponent>(grid);
    }

    /// <summary>
    /// Whether a player ship is running: a supplier putting out power, or an APC with its breaker on and charge left.
    /// Asked only when the ship would enter stasis; a working ship stays awake.
    /// </summary>
    private bool IsWorking(EntityUid grid)
    {
        if (!_gridQuery.TryComp(grid, out var gridComp))
            return false;

        _suppliers.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, gridComp.LocalAABB, _suppliers);
        foreach (var supplier in _suppliers)
        {
            if (supplier.Comp.Enabled && supplier.Comp.CurrentSupply > 0f)
                return true;
        }

        _apcs.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, gridComp.LocalAABB, _apcs);
        foreach (var apc in _apcs)
        {
            if (apc.Comp.MainBreakerEnabled
                && TryComp<BatteryComponent>(apc, out var battery)
                && battery.CurrentCharge > battery.MaxCharge * 0.01f)
                return true;
        }

        return false;
    }
}
