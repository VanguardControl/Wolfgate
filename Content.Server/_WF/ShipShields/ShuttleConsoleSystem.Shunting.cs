using System.Linq;
using Content.Server._WF.ShipShields;
using Content.Shared._WF.ShipShields;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.BUIStates;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WFShipShieldShuntSystem _wfShieldShunts = default!;

    private float _wfShieldHelmAccumulator;
    private readonly Dictionary<EntityUid, WFShipShieldShuntState> _wfShieldHelmStates = new();

    /// <summary>Refreshes open helms when shield availability or visible integrity changes.</summary>
    private void UpdateWolfgateShieldHelms(float frameTime)
    {
        _wfShieldHelmAccumulator += frameTime;
        if (_wfShieldHelmAccumulator < 0.2f)
            return;
        _wfShieldHelmAccumulator = 0f;
        var open = new HashSet<EntityUid>();
        DockingInterfaceState? docks = null;
        var query = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key))
                continue;
            open.Add(uid);
            var selected = new ConsoleShuttleEvent { Console = uid };
            RaiseLocalEvent(uid, ref selected);
            var grid = selected.Console is { } console && TryComp<TransformComponent>(console, out var transform)
                ? transform.GridUid : null;
            var current = GetWolfgateShieldShuntState(grid);
            if (_wfShieldHelmStates.TryGetValue(uid, out var previous) &&
                previous.Available == current.Available && previous.Active == current.Active &&
                MathF.Round(previous.Health * 100f) == MathF.Round(current.Health * 100f) &&
                previous.DirectionRadians == current.DirectionRadians &&
                previous.Concentration == current.Concentration && previous.ArcRadians == current.ArcRadians)
                continue;
            _wfShieldHelmStates[uid] = current;
            UpdateState(uid, ref docks);
        }
        foreach (var uid in _wfShieldHelmStates.Keys.Where(uid => !open.Contains(uid)).ToArray())
            _wfShieldHelmStates.Remove(uid);
    }

    /// <summary>Includes the target ship's shield allocation in each helm refresh.</summary>
    private WFShipShieldShuntState GetWolfgateShieldShuntState(EntityUid? grid)
    {
        return _wfShieldShunts.GetState(grid);
    }
}
