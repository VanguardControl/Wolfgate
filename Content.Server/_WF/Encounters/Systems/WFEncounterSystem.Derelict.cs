using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.Encounters;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterSystem
{
    /// <summary>
    /// Makes a hulk of a freshly spawned hull: no crew and no fuel, litter across its decks, squatters living in it
    /// and their chief, who carries the claim to the ship.
    /// </summary>
    private void Wreck(WFEncounterShipState state, WFEncounterDerelict derelict, string vessel)
    {
        state.Derelict = true;
        LeaveStranded(state.Grid, WFEncounterStranding.Fuel);

        // Every tile a mob can stand on, nearest the middle of the ship first.
        var tiles = _planner.HoldTiles(state.Grid, int.MaxValue);
        if (tiles.Count == 0)
            return;

        if (derelict.King is { } kingId)
        {
            var king = Spawn(kingId, tiles[0]);
            var drop = EnsureComp<WFSalvageClaimDropComponent>(king);
            drop.Ship = state.Grid;
            drop.Claim = derelict.Claim;
            drop.Vessel = vessel;
            drop.Resale = derelict.Resale;
        }

        for (var i = 0; i < derelict.Dwellers && derelict.DwellerPool.Count > 0; i++)
        {
            Spawn(_random.Pick(derelict.DwellerPool), _random.Pick(tiles));
        }

        for (var i = 0; i < derelict.Debris && derelict.DebrisPool.Count > 0; i++)
        {
            var at = _random.Pick(tiles);
            Spawn(_random.Pick(derelict.DebrisPool), new EntityCoordinates(at.EntityId, at.Position + _random.NextVector2(0.35f)));
        }
    }

    /// <summary>
    /// A ship somebody has claimed is theirs from then on: it leaves its encounter, which is over, and is never
    /// cleaned up with it.
    /// </summary>
    public void Release(EntityUid grid)
    {
        if (!TryComp<WFEncounterGridComponent>(grid, out var marker))
            return;

        var owner = marker.Encounter;
        var key = marker.Key;
        RemComp<WFEncounterGridComponent>(grid);
        if (!TryComp<WFEncounterComponent>(owner, out var encounter))
            return;

        encounter.Ships.Remove(key);
        Resolve((owner, encounter), WFEncounterResolution.Completed);
    }
}
