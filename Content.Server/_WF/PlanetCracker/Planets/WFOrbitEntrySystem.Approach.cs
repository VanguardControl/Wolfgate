using Content.Server.Shuttles.Systems;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._WF.PlanetCracker.Planets;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>Marks a hull for the planet approach its crew's clients draw over the hop into or out of orbit.</summary>
public sealed partial class WFOrbitEntrySystem
{
    private readonly List<EntityUid> _approachDone = new();

    /// <summary>Stamps the hop's travel leg on the hull. A body outside a star system has no planet to draw, and gets none.</summary>
    public void MarkApproach(EntityUid grid, EntityUid body, bool arriving)
    {
        if (!TryComp<StarSystemMapComponent>(Transform(body).MapUid, out var starMap) || starMap.System is not { } system)
            return;

        var comp = EnsureComp<WFPlanetApproachComponent>(grid);
        comp.System = system;
        comp.PlanetPosition = _transform.GetWorldPosition(body);
        comp.ShipPosition = _transform.GetWorldPosition(grid);
        comp.Arriving = arriving;
        comp.Start = _timing.CurTime + TimeSpan.FromSeconds(ShuttleSystem.WfOrbitStartupTime);
        comp.End = comp.Start + TimeSpan.FromSeconds(ShuttleSystem.WfOrbitTravelTime);
        Dirty(grid, comp);
    }

    /// <summary>Drops the mark once the hop is over; the client stops drawing at the end time on its own.</summary>
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
