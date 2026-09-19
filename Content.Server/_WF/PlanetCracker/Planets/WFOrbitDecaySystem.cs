using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.ShipPa;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared._WF.ShipPa;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>
/// Orbit decay (F11): an orbit layer holds up only what is holding itself up. A grid keeps station while it has at
/// least one linear thruster running, or is force-anchored, or is docked to something that does - anything else is a
/// ship whose power died, a hull whose thrusters were shot off, a fragment, or debris, and it comes down.
/// A sweep rather than subscriptions: thruster power, docking and the map a grid is on all change through paths this
/// feature does not own, and ThrusterComponent's directed subscriptions are claimed already.
/// </summary>
public sealed partial class WFOrbitDecaySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ShipAlertSystem _alert = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private WFOrbitEntrySystem _orbitEntry = default!;

    /// <summary>The situation code a decaying hull is put on; not selectable, so no pilot can set it by hand.</summary>
    public const string AlertOrbitDecay = "WFAlertOrbitDecay";

    /// <summary>How often the orbit layers are swept; a minute of grace does not need a finer clock than the readout.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Settle a hull is given from the moment it first appears on an orbit layer before it can be stamped. An FTL
    /// arrival lands with its thrusters between updates - the hop itself disables them and they come back on their own
    /// power event - so the first sweeps after an arrival read a powered ship as adrift and a vessel a crew had only
    /// just boarded was dropped into the atmosphere on a minute's countdown it never earned.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(10);

    /// <summary>Grids parked on an orbit layer this sweep, collected before anything is added or dropped.</summary>
    private readonly List<EntityUid> _orbiters = new();

    /// <summary>Grids still carrying the component after having left the orbit layer by some other route.</summary>
    private readonly List<EntityUid> _stale = new();

    /// <summary>When each grid on an orbit layer may first be stamped, from the sweep that first saw it there.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _settleAt = new();

    /// <summary>Settle stamps belonging to grids that are no longer on an orbit layer.</summary>
    private readonly List<EntityUid> _settleStale = new();

    /// <summary>Grids seen adrift on the previous sweep; a stamp takes two sweeps running, never one reading.</summary>
    private readonly HashSet<EntityUid> _adriftLastSweep = new();

    private readonly HashSet<EntityUid> _adriftThisSweep = new();

    /// <summary>Grids with a linear thruster actually running, rebuilt once per sweep rather than per grid.</summary>
    private readonly HashSet<EntityUid> _thrusting = new();

    private readonly HashSet<EntityUid> _dockSeen = new();
    private readonly Queue<EntityUid> _dockQueue = new();

    private TimeSpan _nextSweep;

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;
        Sweep();
    }

    /// <summary>One pass over everything parked in orbit, plus the grids that have since left it.</summary>
    private void Sweep()
    {
        CollectThrusting();
        CollectOrbiters();

        _adriftThisSweep.Clear();

        foreach (var grid in _orbiters)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            // First sight of this grid on the layer starts its settle; an arrival is the one moment the thruster
            // readings are not the ship's own.
            _settleAt.TryAdd(grid, _timing.CurTime + SettleDelay);

            // A chunk that was stamped while it was still a bare grid loses the countdown here rather than carrying
            // it: the exemption is cleared, not merely skipped.
            if (HasComp<WFPlanetChunkComponent>(grid) || HasStationKeeping(grid))
            {
                ClearDecay(grid);
                continue;
            }

            _adriftThisSweep.Add(grid);

            if (_timing.CurTime < _settleAt.GetValueOrDefault(grid))
                continue;

            // Two sweeps running: one reading between a thruster's own updates is not a ship that has lost its engines.
            if (!_adriftLastSweep.Contains(grid))
                continue;

            Decay(grid);
        }

        _adriftLastSweep.Clear();
        _adriftLastSweep.UnionWith(_adriftThisSweep);

        // Entering the atmosphere clears the component on its way out, so what is left here is a grid that left orbit
        // some other way: the leave-orbit hop, an admin move, a tow.
        _stale.Clear();

        var query = EntityQueryEnumerator<WFOrbitDecayComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            if (Transform(uid).MapUid is not { } mapUid || !HasComp<WFOrbitLayerComponent>(mapUid))
                _stale.Add(uid);
        }

        foreach (var grid in _stale)
        {
            ClearDecay(grid);
        }

        // A settle is taken against the layer the grid is on, so it goes when the grid does; a hull that comes back
        // has arrived again and is given its moment again.
        _settleStale.Clear();

        foreach (var (grid, _) in _settleAt)
        {
            if (TerminatingOrDeleted(grid) || Transform(grid).MapUid is not { } mapUid || !HasComp<WFOrbitLayerComponent>(mapUid))
                _settleStale.Add(grid);
        }

        foreach (var grid in _settleStale)
        {
            _settleAt.Remove(grid);
        }
    }

    /// <summary>Every grid with a linear thruster that is switched on and actually running.</summary>
    private void CollectThrusting()
    {
        _thrusting.Clear();

        var query = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();

        while (query.MoveNext(out _, out var thruster, out var xform))
        {
            // Gyroscopes spin a hull; they do not hold it anywhere. A broken or unpowered thruster still carries its
            // component, so IsOn is what is asked, exactly as the lift sweep asks it.
            if (thruster.Type != ThrusterType.Linear || !thruster.Enabled || !thruster.IsOn)
                continue;

            if (xform.ParentUid is { Valid: true } grid && HasComp<MapGridComponent>(grid))
                _thrusting.Add(grid);
        }
    }

    /// <summary>Every grid parked on an orbit layer: not the orbit marker, and nothing that is not a grid at all.</summary>
    private void CollectOrbiters()
    {
        _orbiters.Clear();

        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            // The orbit map is itself a grid with no thrusters; it is the layer, not an orbiter, and must never be dropped.
            if (HasComp<MapComponent>(uid) || xform.MapUid is not { } mapUid || !HasComp<WFOrbitLayerComponent>(mapUid))
                continue;

            _orbiters.Add(uid);
        }
    }

    /// <summary>
    /// True when this grid, or anything docked to it however far down the chain, is holding the whole set up. The
    /// flood is over docking ports only: a tender welded to a powered hull rides its station-keeping, a wreck lying
    /// beside one does not.
    /// </summary>
    public bool HasStationKeeping(EntityUid grid)
    {
        _dockSeen.Clear();
        _dockQueue.Clear();
        _dockSeen.Add(grid);
        _dockQueue.Enqueue(grid);

        while (_dockQueue.Count > 0)
        {
            var member = _dockQueue.Dequeue();

            if (_thrusting.Contains(member) || HasComp<ForceAnchorComponent>(member))
                return true;

            foreach (var dock in _docking.GetDocks(member))
            {
                if (dock.Comp.DockedWith is not { } other || TerminatingOrDeleted(other))
                    continue;

                if (Transform(other).GridUid is not { } neighbour || !_dockSeen.Add(neighbour))
                    continue;

                _dockQueue.Enqueue(neighbour);
            }
        }

        return false;
    }

    /// <summary>Starts, or carries on, one grid's countdown.</summary>
    private void Decay(EntityUid grid)
    {
        var comp = EnsureComp<WFOrbitDecayComponent>(grid);

        // Announced latches the whole stamp, so a hull that is already coming down is not given a fresh minute every
        // second, and a component placed by hand keeps the grace it was given.
        if (!comp.Announced)
        {
            comp.DecayAt = _timing.CurTime + comp.Grace;
            comp.PriorCode = CompOrNull<ShipAlertComponent>(grid)?.Code;
            comp.Announced = true;

            Warn(grid, comp);
        }

        if (_timing.CurTime < comp.DecayAt)
            return;

        Drop(grid, comp);
    }

    /// <summary>
    /// The PA warning, set as a situation code but announced by hand: SetCode's own announcement takes only the ship
    /// name, and the one thing the crew needs here is how long they have.
    /// </summary>
    private void Warn(EntityUid grid, WFOrbitDecayComponent comp)
    {
        _alert.SetCode(grid, AlertOrbitDecay, announce: false);

        if (!_proto.TryIndex<ShipAlertCodePrototype>(AlertOrbitDecay, out var proto))
            return;

        _pa.Announce(grid,
            Loc.GetString("wf-alert-orbit-decay-countdown",
                ("ship", _pa.GetShipName(grid)),
                ("seconds", (int) Math.Round(comp.Grace.TotalSeconds))),
            proto.Sound,
            color: proto.Color);
    }

    /// <summary>
    /// Hands the hull to the atmosphere through the very routine the console's own descent uses, so an unmanned
    /// wreck falls with the same seed, the same transit and the same GPWS sequence a piloted hull gets.
    /// </summary>
    private void Drop(EntityUid grid, WFOrbitDecayComponent comp)
    {
        // The code goes back first: the lift-lost state remembers whatever the ship is on when the fall starts, and
        // that has to be the ship's own code rather than this warning, or landing would restore the warning.
        Restore(grid, comp);

        if (!_orbitEntry.TryDropFromOrbit(grid, out _))
        {
            // Refused because the hull is mid-hop or its stack has nowhere below it. Put the warning back up and try
            // again on the next sweep; the deadline has already passed, so nothing is re-granted.
            if (comp.Announced)
                _alert.SetCode(grid, AlertOrbitDecay, announce: false);

            return;
        }

        RemComp<WFOrbitDecayComponent>(grid);
    }

    /// <summary>Drops the countdown and gives the ship its own situation code back.</summary>
    public void ClearDecay(EntityUid grid)
    {
        if (!TryComp<WFOrbitDecayComponent>(grid, out var comp))
            return;

        Restore(grid, comp);
        RemComp<WFOrbitDecayComponent>(grid);
    }

    /// <summary>
    /// Hands the ship its own code back, silently, the way the lift-lost state does. Only ever undoes this feature's
    /// own writing: a code set by the crew while the warning was up is theirs and stays.
    /// </summary>
    private void Restore(EntityUid grid, WFOrbitDecayComponent comp)
    {
        if (!comp.Announced || comp.PriorCode is not { } prior)
            return;

        if (CompOrNull<ShipAlertComponent>(grid)?.Code != AlertOrbitDecay)
            return;

        _alert.SetCode(grid, prior, announce: false);
    }
}
