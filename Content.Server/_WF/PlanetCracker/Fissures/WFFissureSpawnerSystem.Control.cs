using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Fissures;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Fissures;

/// <summary>
/// The public admin and test surface, on the WFGravityAnchorSystem.Control.cs pattern: it reaches the private ring and
/// surge writers without duplicating their guards. No subscriptions.
/// </summary>
public sealed partial class WFFissureSpawnerSystem
{
    /// <summary>Spreads the next ring now, ignoring the schedule, and pushes the following one a full interval out.</summary>
    public bool ForceRing(Entity<WFFissureSpawnerComponent> ent)
    {
        if (!TryComp<WFGravityAnchorComponent>(ent.Owner, out var anchor) ||
            anchor.State != WFAnchorState.Drilling)
        {
            return false;
        }

        // The sweep's own stopping condition (WFFissureSpawnerSystem.cs Update). Without it an admin ring past the
        // count would push RingsDone beyond RingCount, which silently ends the scheduled spread for the rest of the
        // drill and keeps growing the forced radius past the intended eight tiles.
        if (ent.Comp.RingsDone >= ent.Comp.RingCount)
            return false;

        ent.Comp.NextRing = _timing.CurTime + RingInterval(ent.Comp, anchor);
        SpreadRing(ent, anchor);
        ent.Comp.RingsDone++;
        return true;
    }

    /// <summary>Runs the extraction surge for a hull's pair at its current cut circle; returns how many threats came up.</summary>
    public int ForceSurge(EntityUid cracker)
    {
        if (!TryComp<WFPlanetCrackerComponent>(cracker, out var crackerComp))
            return 0;

        var ent = new Entity<WFPlanetCrackerComponent>(cracker, crackerComp);

        if (!_crackers.TryGetTargetedPair(ent, out var a, out var b) &&
            !_crackers.TryGetOwnedPair(ent, out a, out b, false))
        {
            return 0;
        }

        if (!_crackers.TryGetCircle(a.Owner, b.Owner, out var centre, out var radius))
            return 0;

        // A ground layer's map entity IS its grid, so the anchor's map uid is the grid the band is built on.
        var groundMap = Transform(a.Owner).MapUid ?? EntityUid.Invalid;

        if (!groundMap.IsValid() ||
            !TryComp<MapGridComponent>(groundMap, out var mapGrid) ||
            !TryComp<BiomeComponent>(groundMap, out var biome))
        {
            return 0;
        }

        var ground = new Entity<MapGridComponent>(groundMap, mapGrid);

        return Surge(a.Owner, ground, biome, centre, radius) + Surge(b.Owner, ground, biome, centre, radius);
    }
}
