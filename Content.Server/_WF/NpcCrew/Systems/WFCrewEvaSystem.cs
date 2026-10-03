using Content.Server._WF.NpcCrew.Components;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Body.Systems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Connects real suit internals before crew work and when their current tile loses safe air.</summary>
public sealed class WFCrewEvaSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedGasTankSystem _tanks = default!;
    [Dependency] private SharedInternalsSystem _internals = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    private readonly Dictionary<EntityUid, TimeSpan> _prepared = new();
    private readonly Dictionary<EntityUid, EntityCoordinates> _safetyDestinations = new();
    private const float ReservePressure = 300;
    private float _timer;

    /// <summary>Requires a pressure suit, helmet, breathing interface and an actual nonempty oxygen tank.</summary>
    public bool Prepare(EntityUid mob)
    {
        TryReplaceLowTank(mob);
        if (!_inventory.TryGetSlotEntity(mob, "outerClothing", out var suit)
            || !TryComp<PressureProtectionComponent>(suit, out var suitPressure) || suitPressure.LowPressureMultiplier < 100
            || !_inventory.TryGetSlotEntity(mob, "head", out var helmet)
            || !TryComp<PressureProtectionComponent>(helmet, out var helmetPressure) || helmetPressure.LowPressureMultiplier < 100
            || !_inventory.TryGetSlotEntity(mob, "suitstorage", out var tank)
            || !TryComp<GasTankComponent>(tank, out var gas) || gas.Air.Pressure <= ReservePressure || gas.Air.GetMoles(Content.Shared.Atmos.Gas.Oxygen) <= 0)
            return false;
        if (!gas.IsConnected)
            _tanks.ConnectToInternals((tank.Value, gas), mob);
        if (!_internals.AreInternalsWorking(mob))
            return false;
        _prepared[mob] = _timing.CurTime + TimeSpan.FromSeconds(5);
        _safetyDestinations.Remove(mob);
        return true;
    }

    /// <summary>Low-air crew walk to safe air or a replacement tank through the normal crew work branch.</summary>
    public bool TryGetSafetyDestination(EntityUid mob, out EntityCoordinates coordinates) => _safetyDestinations.TryGetValue(mob, out coordinates);

    private void TryReplaceLowTank(EntityUid mob)
    {
        if (!_inventory.TryGetSlotEntity(mob, "suitstorage", out var old)
            || !TryComp<GasTankComponent>(old, out var oldGas) || oldGas.Air.Pressure > ReservePressure)
            return;
        var tanks = EntityQueryEnumerator<GasTankComponent, TransformComponent>();
        while (tanks.MoveNext(out var replacement, out var gas, out var xform))
        {
            if (replacement == old || xform.ParentUid != Transform(mob).GridUid || gas.Air.Pressure <= ReservePressure * 2
                || gas.Air.GetMoles(Content.Shared.Atmos.Gas.Oxygen) <= 0 || !_interaction.InRangeUnobstructed(mob, replacement))
                continue;
            if (oldGas.IsConnected)
                _tanks.DisconnectFromInternals((old.Value, oldGas), mob);
            if (!_inventory.TryUnequip(mob, "suitstorage", silent: true))
                return;
            if (!_inventory.TryEquip(mob, replacement, "suitstorage", silent: true))
                _inventory.TryEquip(mob, old.Value, "suitstorage", silent: true);
            return;
        }
    }

    private void FindSafety(EntityUid mob, WFCrewComponent crew, bool safe)
    {
        var grid = crew.Post?.EntityId ?? Transform(mob).GridUid;
        if (grid is not { } home || !TryComp<MapGridComponent>(home, out var map))
            return;
        EntityCoordinates? best = null;
        var distance = float.MaxValue;
        var here = _transform.GetWorldPosition(mob);
        var tiles = _maps.GetAllTilesEnumerator(home, map);
        while (tiles.MoveNext(out var tile))
        {
            if (safe || !_planner.IsSafePost(home, tile.Value.GridIndices, map))
                continue;
            var point = _maps.GridTileToLocal(home, map, tile.Value.GridIndices);
            var squared = (_transform.ToWorldPosition(point) - here).LengthSquared();
            if (squared < distance) { best = point; distance = squared; }
        }
        if (safe)
        {
            var tanks = EntityQueryEnumerator<GasTankComponent, TransformComponent>();
            while (tanks.MoveNext(out _, out var gas, out var xform))
            {
                if (xform.ParentUid != home || gas.Air.Pressure <= ReservePressure * 2
                    || gas.Air.GetMoles(Content.Shared.Atmos.Gas.Oxygen) <= 0
                    || !_planner.IsSafePost(home, _maps.TileIndicesFor(home, map, xform.Coordinates), map))
                    continue;
                var squared = (_transform.GetWorldPosition(xform) - here).LengthSquared();
                if (squared < distance) { best = xform.Coordinates; distance = squared; }
            }
        }
        if (best is { } destination)
            _safetyDestinations[mob] = destination;
        else
            _safetyDestinations.Remove(mob);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 0.5f)
            return;
        _timer = 0;
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (!_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid))
                continue;
            var safe = xform.GridUid is { } grid && TryComp<MapGridComponent>(grid, out var map)
                && _planner.IsSafePost(grid, _maps.TileIndicesFor(grid, map, xform.Coordinates), map);
            if (_inventory.TryGetSlotEntity(uid, "suitstorage", out var worn)
                && TryComp<GasTankComponent>(worn, out var wornGas) && wornGas.Air.Pressure <= ReservePressure
                && !Prepare(uid))
            {
                EntityManager.System<WFCrewWorkSystem>().CancelWorker(uid);
                FindSafety(uid, crew, safe);
                if (!safe && !wornGas.IsConnected)
                    _tanks.ConnectToInternals((worn.Value, wornGas), uid);
            }
            if (!safe)
                Prepare(uid);
            else if ((!_prepared.TryGetValue(uid, out var until) || _timing.CurTime >= until)
                && _inventory.TryGetSlotEntity(uid, "suitstorage", out var tank)
                && TryComp<GasTankComponent>(tank, out var gas) && gas.IsConnected)
            {
                _tanks.DisconnectFromInternals((tank.Value, gas), uid);
                _prepared.Remove(uid);
            }
        }
    }
}
