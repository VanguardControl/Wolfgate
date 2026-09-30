using Content.Server._Crescent.ShipShields;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Shared.Access.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WF.ShipShields;

/// <summary>Applies authorized helm allocations to the ship selected by the console.</summary>
public sealed class WFShipShieldShuntSystem : EntitySystem
{
    [Dependency] private ShipShieldsSystem _shields = default!;
    [Dependency] private ShuttleConsoleLockSystem _locks = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;

    public override void Initialize()
    {
        base.Initialize();
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<WFShipShieldSetShuntMessage>(OnSetShunt);
        });
    }

    private void OnSetShunt(Entity<ShuttleConsoleComponent> ent, ref WFShipShieldSetShuntMessage args)
    {
        if (!TryComp<PilotComponent>(args.Actor, out var pilot) || pilot.Console != ent.Owner ||
            !Transform(ent).Anchored || !this.IsPowered(ent, EntityManager) ||
            !_blocker.CanInteract(args.Actor, ent) || !_access.IsAllowed(args.Actor, ent) || IsLocked(ent))
            return;
        var selected = new ConsoleShuttleEvent { Console = ent.Owner };
        RaiseLocalEvent(ent.Owner, ref selected);
        if (selected.Console is not { } console || IsLocked(console) || !Transform(console).Anchored || !this.IsPowered(console, EntityManager) ||
            Transform(console).GridUid is not { } grid || !HasShieldGenerator(grid))
            return;
        _shields.SetWolfgateShieldShunt(grid, args.DirectionRadians, args.Concentration, args.ArcRadians);
    }

    private bool IsLocked(EntityUid console)
    {
        return TryComp<ShuttleConsoleLockComponent>(console, out var component) &&
            _locks.GetEffectiveLockState(console, component);
    }

    private bool HasShieldGenerator(EntityUid grid)
    {
        if (HasComp<ShipShieldedComponent>(grid))
            return true;
        var query = EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var transform))
        {
            if (transform.GridUid == grid)
                return true;
        }
        return false;
    }

    /// <summary>Returns allocation and availability for the console's authoritative target grid.</summary>
    public WFShipShieldShuntState GetState(EntityUid? grid)
    {
        var allocation = grid is { } uid && TryComp<WFShipShieldShuntComponent>(uid, out var component) ? component : null;
        var health = 0f;
        var active = false;
        if (grid is { } activeGrid && TryComp<ShipShieldedComponent>(activeGrid, out var shielded) &&
            TryComp<WFShipShieldVisualsComponent>(shielded.Shield, out var visuals))
        {
            health = visuals.Health;
            active = true;
        }
        return new WFShipShieldShuntState(grid is { } availableGrid && HasShieldGenerator(availableGrid), active, health,
            allocation?.DirectionRadians ?? MathF.PI / 2f, allocation?.Concentration ?? 0f,
            allocation?.ArcRadians ?? MathF.PI / 2f);
    }
}
