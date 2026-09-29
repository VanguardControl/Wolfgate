using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._WF.Planets;

namespace Content.Server._WF.Planets;

/// <summary>Marks a hull and the grids docked to it for the planet approach their crews' clients draw over the hop into or out of orbit.</summary>
public sealed partial class WFOrbitEntrySystem
{
    private readonly List<EntityUid> _approachDone = new();
    private readonly HashSet<EntityUid> _approachGrids = new();

    /// <summary>Records the hop's travel leg on the hull and every grid the hop carries; skipped for bodies outside a star system.</summary>
    public void MarkApproach(EntityUid grid, EntityUid body, bool arriving)
    {
        if (!TryComp<StarSystemMapComponent>(Transform(body).MapUid, out var starMap) || starMap.System is not { } system)
            return;

        var planetPosition = _transform.GetWorldPosition(body);
        var shipPosition = _transform.GetWorldPosition(grid);
        var start = _timing.CurTime + TimeSpan.FromSeconds(ShuttleSystem.WfOrbitStartupTime);
        var end = start + TimeSpan.FromSeconds(ShuttleSystem.WfOrbitTravelTime);

        // The overlay reads the grid the local player stands on, so docked crews need the mark too.
        _approachGrids.Clear();
        _shuttle.GetAllDockedShuttles(grid, _approachGrids);

        foreach (var uid in _approachGrids)
        {
            var comp = EnsureComp<WFPlanetApproachComponent>(uid);
            comp.Hull = grid;
            comp.System = system;
            comp.PlanetPosition = planetPosition;
            comp.ShipPosition = shipPosition;
            comp.Arriving = arriving;
            comp.Start = start;
            comp.End = end;
            Dirty(uid, comp);
        }
    }

    /// <summary>Drops the hop's marks on the arrival tick, so clients get the move off the FTL map and the removal in one state.</summary>
    private void OnApproachCompleted(Entity<WFPlanetApproachComponent> ent, ref FTLCompletedEvent args)
    {
        _approachDone.Clear();
        _approachDone.Add(ent.Owner);

        var query = EntityQueryEnumerator<WFPlanetApproachComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Hull == ent.Owner && uid != ent.Owner)
                _approachDone.Add(uid);
        }

        foreach (var uid in _approachDone)
        {
            RemComp<WFPlanetApproachComponent>(uid);
        }
    }

    /// <summary>Fallback for a hop that never arrives: drops marks well past their end time.</summary>
    private void SweepApproaches()
    {
        _approachDone.Clear();

        var query = EntityQueryEnumerator<WFPlanetApproachComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime > comp.End + RefreshInterval)
                _approachDone.Add(uid);
        }

        foreach (var uid in _approachDone)
        {
            RemComp<WFPlanetApproachComponent>(uid);
        }
    }
}
